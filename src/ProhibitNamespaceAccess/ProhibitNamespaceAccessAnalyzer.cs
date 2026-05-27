using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.ProhibitNamespaceAccess
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ProhibitNamespaceAccessAnalyzer : DiagnosticAnalyzer
    {
        public const string TypeAccessDiagnosticId = "AN0105";
        public const string ConfigErrorDiagnosticId = "AN0106";
        private const string category = "TypeSafety";

        private static readonly DiagnosticDescriptor typeAccessRule = new DiagnosticDescriptor(
            TypeAccessDiagnosticId,
            "Namespace access prohibited",
            "Access to '{0}' in namespace '{1}' is prohibited by pattern '{2}' in <ProhibitNamespaceAccess>",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor typeAccessWithCountRule = new DiagnosticDescriptor(
            TypeAccessDiagnosticId,
            "Namespace access prohibited",
            "Access to '{0}' in namespace '{1}' is prohibited by pattern '{2}' in <ProhibitNamespaceAccess> ({3} references in this file)",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor usingDirectiveRule = new DiagnosticDescriptor(
            TypeAccessDiagnosticId,
            "Using directive for prohibited namespace",
            "Using directive for namespace '{0}' is prohibited by pattern '{1}' in <ProhibitNamespaceAccess>. Remove this unused using directive.",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor configParseErrorRule = new DiagnosticDescriptor(
            ConfigErrorDiagnosticId,
            "ProhibitNamespaceAccess config error",
            "Failed to parse <ProhibitNamespaceAccess> value: {0}. Expected format: {{ error = [ \"Namespace.Pattern\" ], warn = [ \"Other.Pattern\" ] }}",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(typeAccessRule, typeAccessWithCountRule, usingDirectiveRule, configParseErrorRule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterCompilationStartAction(compilationStartContext =>
            {
                var parsedConfig = parseConfig(compilationStartContext);
                if (parsedConfig == null || !parsedConfig.HasAnyPatterns)
                    return;

                // Use RegisterSemanticModelAction to process each file once with deduplication
                compilationStartContext.RegisterSemanticModelAction(semanticModelContext =>
                {
                    analyzeSemanticModel(semanticModelContext, parsedConfig);
                });
            });
        }

        private ProhibitNamespaceAccessConfig? parseConfig(CompilationStartAnalysisContext compilationStartContext)
        {
            // Read config from any syntax tree's analyzer options
            var firstSyntaxTree = compilationStartContext.Compilation.SyntaxTrees.FirstOrDefault();
            if (firstSyntaxTree == null)
                return null;

            var analyzerConfigOptions = compilationStartContext.Options.AnalyzerConfigOptionsProvider
                .GetOptions(firstSyntaxTree);

            if (!analyzerConfigOptions.TryGetValue("build_property.ProhibitNamespaceAccess", out var configValue)
                || string.IsNullOrWhiteSpace(configValue))
            {
                return null;
            }

            try
            {
                return ProhibitNamespaceAccessConfigParser.Parse(configValue);
            }
            catch (FormatException parseException)
            {
                var configErrorLocation = firstSyntaxTree.GetRoot().GetLocation();
                compilationStartContext.RegisterCompilationEndAction(compilationEndContext =>
                {
                    compilationEndContext.ReportDiagnostic(
                        Diagnostic.Create(configParseErrorRule, Location.None, parseException.Message));
                });
                return null;
            }
        }

        private void analyzeSemanticModel(
            SemanticModelAnalysisContext semanticModelContext,
            ProhibitNamespaceAccessConfig parsedConfig)
        {
            var semanticModel = semanticModelContext.SemanticModel;
            var syntaxTree = semanticModel.SyntaxTree;
            var rootNode = syntaxTree.GetRoot(semanticModelContext.CancellationToken);

            // Collect violations for deduplication: (typeName, namespace) → (firstLocation, count, pattern, severity)
            var typeViolationsByTypeKey = new Dictionary<(string typeName, string namespaceName), TypeViolationRecord>();

            // Walk all nodes
            foreach (var syntaxNode in rootNode.DescendantNodes())
            {
                semanticModelContext.CancellationToken.ThrowIfCancellationRequested();

                switch (syntaxNode)
                {
                    case UsingDirectiveSyntax usingDirective:
                        analyzeUsingDirective(semanticModelContext, usingDirective, parsedConfig);
                        break;

                    case IdentifierNameSyntax identifierName:
                        checkTypeReference(semanticModel, identifierName, parsedConfig, typeViolationsByTypeKey, semanticModelContext.CancellationToken);
                        break;

                    case GenericNameSyntax genericName:
                        checkTypeReference(semanticModel, genericName, parsedConfig, typeViolationsByTypeKey, semanticModelContext.CancellationToken);
                        break;
                }
            }

            // Report deduplicated type violations
            foreach (var violationEntry in typeViolationsByTypeKey)
            {
                var violationRecord = violationEntry.Value;
                reportDeduplicatedViolation(semanticModelContext, violationRecord);
            }
        }

        private void checkTypeReference(
            SemanticModel semanticModel,
            SimpleNameSyntax nameSyntax,
            ProhibitNamespaceAccessConfig parsedConfig,
            Dictionary<(string typeName, string namespaceName), TypeViolationRecord> typeViolationsByTypeKey,
            System.Threading.CancellationToken cancellationToken)
        {
            // Skip if this is part of a using directive (handled separately)
            if (nameSyntax.FirstAncestorOrSelf<UsingDirectiveSyntax>() != null)
                return;

            // Skip if this is a namespace declaration
            if (nameSyntax.Parent is QualifiedNameSyntax qualifiedParent &&
                qualifiedParent.FirstAncestorOrSelf<NamespaceDeclarationSyntax>()?.Name == qualifiedParent)
                return;
            if (nameSyntax.FirstAncestorOrSelf<FileScopedNamespaceDeclarationSyntax>()?.Name == nameSyntax)
                return;

            ITypeSymbol? resolvedTypeSymbol = null;

            // Check if this is 'var' — resolve inferred type
            if (nameSyntax is IdentifierNameSyntax identifierName && identifierName.Identifier.Text == "var")
            {
                var inferredTypeInfo = semanticModel.GetTypeInfo(identifierName, cancellationToken);
                resolvedTypeSymbol = inferredTypeInfo.Type;
            }
            else
            {
                // Try to resolve as a type symbol
                var symbolInfo = semanticModel.GetSymbolInfo(nameSyntax, cancellationToken);
                resolvedTypeSymbol = symbolInfo.Symbol as ITypeSymbol;

                // Also check if this is a constructor call (ObjectCreationExpression)
                if (resolvedTypeSymbol == null && symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Constructor)
                {
                    resolvedTypeSymbol = methodSymbol.ContainingType;
                }
            }

            if (resolvedTypeSymbol == null)
                return;

            // Get the fully-qualified namespace
            var containingNamespace = resolvedTypeSymbol.ContainingNamespace;
            if (containingNamespace == null || containingNamespace.IsGlobalNamespace)
                return;

            string fullyQualifiedNamespace = containingNamespace.ToDisplayString();
            string typeName = resolvedTypeSymbol.Name;

            // Check against error patterns first, then warn patterns
            NamespacePattern? matchedPattern = findMatchingPattern(fullyQualifiedNamespace, parsedConfig.ErrorPatterns);
            DiagnosticSeverity matchedSeverity = DiagnosticSeverity.Error;

            if (matchedPattern == null)
            {
                matchedPattern = findMatchingPattern(fullyQualifiedNamespace, parsedConfig.WarnPatterns);
                matchedSeverity = DiagnosticSeverity.Warning;
            }

            if (matchedPattern == null)
                return;

            // Record the violation for deduplication
            var typeKey = (typeName, fullyQualifiedNamespace);
            if (typeViolationsByTypeKey.TryGetValue(typeKey, out var existingViolationRecord))
            {
                existingViolationRecord.ReferenceCount++;
            }
            else
            {
                typeViolationsByTypeKey[typeKey] = new TypeViolationRecord
                {
                    TypeName = typeName,
                    NamespaceName = fullyQualifiedNamespace,
                    MatchedPatternString = matchedPattern.Value.OriginalPattern,
                    EffectiveSeverity = matchedSeverity,
                    FirstOccurrenceLocation = nameSyntax.GetLocation(),
                    ReferenceCount = 1
                };
            }
        }

        private void analyzeUsingDirective(
            SemanticModelAnalysisContext semanticModelContext,
            UsingDirectiveSyntax usingDirective,
            ProhibitNamespaceAccessConfig parsedConfig)
        {
            if (usingDirective.Name == null)
                return;

            var namespaceSymbol = semanticModelContext.SemanticModel.GetSymbolInfo(
                usingDirective.Name, semanticModelContext.CancellationToken).Symbol as INamespaceSymbol;

            if (namespaceSymbol == null)
                return;

            string fullyQualifiedNamespace = namespaceSymbol.ToDisplayString();

            // Check against all patterns (both error and warn) — using directives always produce warnings
            NamespacePattern? matchedPattern = findMatchingPattern(fullyQualifiedNamespace, parsedConfig.ErrorPatterns)
                ?? findMatchingPattern(fullyQualifiedNamespace, parsedConfig.WarnPatterns);

            if (matchedPattern == null)
                return;

            // Using directives always produce warnings, never errors
            semanticModelContext.ReportDiagnostic(
                Diagnostic.Create(usingDirectiveRule, usingDirective.GetLocation(),
                    fullyQualifiedNamespace, matchedPattern.Value.OriginalPattern));
        }

        private static NamespacePattern? findMatchingPattern(string fullyQualifiedNamespace, List<NamespacePattern> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.MatchesNamespace(fullyQualifiedNamespace))
                    return pattern;
            }
            return null;
        }

        private void reportDeduplicatedViolation(
            SemanticModelAnalysisContext semanticModelContext,
            TypeViolationRecord violationRecord)
        {
            DiagnosticDescriptor effectiveRule;
            Diagnostic diagnostic;

            if (violationRecord.ReferenceCount > 1)
            {
                effectiveRule = new DiagnosticDescriptor(
                    typeAccessWithCountRule.Id,
                    typeAccessWithCountRule.Title,
                    typeAccessWithCountRule.MessageFormat,
                    typeAccessWithCountRule.Category,
                    violationRecord.EffectiveSeverity,
                    typeAccessWithCountRule.IsEnabledByDefault);

                diagnostic = Diagnostic.Create(effectiveRule, violationRecord.FirstOccurrenceLocation,
                    violationRecord.TypeName, violationRecord.NamespaceName,
                    violationRecord.MatchedPatternString, violationRecord.ReferenceCount);
            }
            else
            {
                effectiveRule = new DiagnosticDescriptor(
                    typeAccessRule.Id,
                    typeAccessRule.Title,
                    typeAccessRule.MessageFormat,
                    typeAccessRule.Category,
                    violationRecord.EffectiveSeverity,
                    typeAccessRule.IsEnabledByDefault);

                diagnostic = Diagnostic.Create(effectiveRule, violationRecord.FirstOccurrenceLocation,
                    violationRecord.TypeName, violationRecord.NamespaceName,
                    violationRecord.MatchedPatternString);
            }

            semanticModelContext.ReportDiagnostic(diagnostic);
        }

        private sealed class TypeViolationRecord
        {
            public string TypeName { get; set; } = "";
            public string NamespaceName { get; set; } = "";
            public string MatchedPatternString { get; set; } = "";
            public DiagnosticSeverity EffectiveSeverity { get; set; }
            public Location FirstOccurrenceLocation { get; set; } = Location.None;
            public int ReferenceCount { get; set; }
        }
    }
}