using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.RequireTypedPointersNotIntPtr
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RequireTypedPointersNotIntPtrAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0100";
        private const string category = "TypeSafety";

        private const string helpLinkUrl =
            "https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md";

        // Rule for IntPtr/UIntPtr used anywhere
        private static readonly DiagnosticDescriptor intPtrRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Do not use raw IntPtr/UIntPtr",
            "Do not use '{0}'. IntPtr and UIntPtr erase type information and are not safe. Use typed structs for handles and unsafe T* for pointers.",
            category,
            DiagnosticSeverity.Warning, // default severity; overridden by config
            isEnabledByDefault: true,
            helpLinkUri: helpLinkUrl);

        // Rule for nint/nuint used in P/Invoke declarations
        private static readonly DiagnosticDescriptor nintInPInvokeRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Do not use nint/nuint in P/Invoke declarations",
            "Do not use '{0}' in P/Invoke declarations. Use typed structs for handles and unsafe T* for pointers.",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: helpLinkUrl);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(intPtrRule, nintInPInvokeRule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            // Register for all syntax nodes that can reference a type
            analysisContext.RegisterSyntaxNodeAction(analyzeTypeSyntax,
                SyntaxKind.IdentifierName,
                SyntaxKind.PredefinedType,
                SyntaxKind.CastExpression);
        }

        private void analyzeTypeSyntax(SyntaxNodeAnalysisContext nodeContext)
        {
            var enforcementLevel = getEnforcementLevel(nodeContext);
            if (enforcementLevel == "ignore")
                return;

            var effectiveSeverity = enforcementLevel == "disallow"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            switch (nodeContext.Node)
            {
                case IdentifierNameSyntax identifierNameSyntax:
                    analyzeIdentifierName(nodeContext, identifierNameSyntax, effectiveSeverity);
                    break;

                case PredefinedTypeSyntax predefinedTypeSyntax:
                    analyzePredefinedType(nodeContext, predefinedTypeSyntax, effectiveSeverity);
                    break;

                case CastExpressionSyntax castExpressionSyntax:
                    analyzeCastExpression(nodeContext, castExpressionSyntax, effectiveSeverity);
                    break;
            }
        }

        private void analyzeIdentifierName(
            SyntaxNodeAnalysisContext nodeContext,
            IdentifierNameSyntax identifierNameSyntax,
            DiagnosticSeverity effectiveSeverity)
        {
            var identifierText = identifierNameSyntax.Identifier.Text;

            // Quick text check before doing expensive semantic analysis
            if (identifierText != "IntPtr" && identifierText != "UIntPtr" &&
                identifierText != "nint" && identifierText != "nuint")
                return;

            // Skip if this is part of a member access (e.g., IntPtr.Zero — the "Zero" part)
            // We want to flag the "IntPtr" part, not the member being accessed
            if (identifierNameSyntax.Parent is MemberAccessExpressionSyntax memberAccessParent &&
                memberAccessParent.Name == identifierNameSyntax)
                return;

            var typeInfo = nodeContext.SemanticModel.GetTypeInfo(identifierNameSyntax, nodeContext.CancellationToken);
            var typeSymbol = typeInfo.Type ?? nodeContext.SemanticModel.GetSymbolInfo(identifierNameSyntax, nodeContext.CancellationToken).Symbol as ITypeSymbol;

            if (typeSymbol == null)
                return;

            if (!isIntPtrFamily(typeSymbol))
                return;

            // Determine if this is nint/nuint (native int keywords)
            bool isNativeIntKeyword = identifierText == "nint" || identifierText == "nuint";

            if (isNativeIntKeyword)
            {
                // nint/nuint only flagged in P/Invoke declarations
                if (isInsidePInvokeDeclaration(identifierNameSyntax))
                {
                    reportDiagnostic(nodeContext, identifierNameSyntax.GetLocation(),
                        nintInPInvokeRule, identifierText, effectiveSeverity);
                }
            }
            else
            {
                // IntPtr/UIntPtr flagged everywhere
                reportDiagnostic(nodeContext, identifierNameSyntax.GetLocation(),
                    intPtrRule, identifierText, effectiveSeverity);
            }
        }

        private void analyzePredefinedType(
            SyntaxNodeAnalysisContext nodeContext,
            PredefinedTypeSyntax predefinedTypeSyntax,
            DiagnosticSeverity effectiveSeverity)
        {
            // nint/nuint can appear as predefined types in newer C# versions
            var keywordText = predefinedTypeSyntax.Keyword.Text;
            if (keywordText != "nint" && keywordText != "nuint")
                return;

            // nint/nuint only flagged in P/Invoke declarations
            if (isInsidePInvokeDeclaration(predefinedTypeSyntax))
            {
                reportDiagnostic(nodeContext, predefinedTypeSyntax.GetLocation(),
                    nintInPInvokeRule, keywordText, effectiveSeverity);
            }
        }

        private void analyzeCastExpression(
            SyntaxNodeAnalysisContext nodeContext,
            CastExpressionSyntax castExpressionSyntax,
            DiagnosticSeverity effectiveSeverity)
        {
            // Check the target type of the cast
            var castTargetTypeInfo = nodeContext.SemanticModel.GetTypeInfo(castExpressionSyntax.Type, nodeContext.CancellationToken);
            if (castTargetTypeInfo.Type != null && isIntPtrFamily(castTargetTypeInfo.Type))
            {
                var castTargetTypeName = castTargetTypeInfo.Type.Name;
                bool isNativeIntCast = castTargetTypeName == "IntPtr" &&
                    castExpressionSyntax.Type is PredefinedTypeSyntax predefinedCastType &&
                    (predefinedCastType.Keyword.Text == "nint" || predefinedCastType.Keyword.Text == "nuint");

                if (isNativeIntCast)
                {
                    if (isInsidePInvokeDeclaration(castExpressionSyntax))
                    {
                        var castKeywordText = ((PredefinedTypeSyntax)castExpressionSyntax.Type).Keyword.Text;
                        reportDiagnostic(nodeContext, castExpressionSyntax.Type.GetLocation(),
                            nintInPInvokeRule, castKeywordText, effectiveSeverity);
                    }
                }
                else
                {
                    // IntPtr/UIntPtr cast — flagged everywhere
                    // But don't double-report if the type syntax itself will be caught by IdentifierName handler
                    // Only report here if the type is written as a keyword (nint/nuint) that resolves to IntPtr
                    // Actually, the cast type node will be visited separately as IdentifierName or PredefinedType,
                    // so we should NOT report here to avoid double-reporting.
                }
            }
        }

        private static bool isIntPtrFamily(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.SpecialType == SpecialType.System_IntPtr ||
                typeSymbol.SpecialType == SpecialType.System_UIntPtr)
                return true;

            // Fallback for older Roslyn versions where SpecialType might not cover nint/nuint
            var fullName = typeSymbol.ToDisplayString();
            return fullName == "System.IntPtr" || fullName == "System.UIntPtr" ||
                   fullName == "nint" || fullName == "nuint";
        }

        private static bool isInsidePInvokeDeclaration(SyntaxNode syntaxNode)
        {
            // Walk up to find the containing method declaration
            var containingMethod = syntaxNode.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null)
                return false;

            // Check if the method has [DllImport] or [LibraryImport] attribute
            return containingMethod.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .Any(attr =>
                {
                    var attrName = attr.Name.ToString();
                    return attrName == "DllImport" || attrName == "DllImportAttribute" ||
                           attrName == "System.Runtime.InteropServices.DllImport" ||
                           attrName == "System.Runtime.InteropServices.DllImportAttribute" ||
                           attrName == "LibraryImport" || attrName == "LibraryImportAttribute" ||
                           attrName == "System.Runtime.InteropServices.LibraryImport" ||
                           attrName == "System.Runtime.InteropServices.LibraryImportAttribute";
                });
        }

        private static void reportDiagnostic(
            SyntaxNodeAnalysisContext nodeContext,
            Location diagnosticLocation,
            DiagnosticDescriptor diagnosticRule,
            string typeName,
            DiagnosticSeverity effectiveSeverity)
        {
            // Create a descriptor with the effective severity
            var effectiveRule = new DiagnosticDescriptor(
                diagnosticRule.Id,
                diagnosticRule.Title,
                diagnosticRule.MessageFormat,
                diagnosticRule.Category,
                effectiveSeverity,
                diagnosticRule.IsEnabledByDefault,
                helpLinkUri: diagnosticRule.HelpLinkUri);

            var diagnostic = Diagnostic.Create(effectiveRule, diagnosticLocation, typeName);
            nodeContext.ReportDiagnostic(diagnostic);
        }

        private static string getEnforcementLevel(SyntaxNodeAnalysisContext nodeContext)
        {
            var analyzerConfigOptions = nodeContext.Options.AnalyzerConfigOptionsProvider
                .GetOptions(nodeContext.Node.SyntaxTree);

            if (analyzerConfigOptions.TryGetValue("build_property.RequireTypedPointersNotIntPtr", out var configValue)
                && !string.IsNullOrEmpty(configValue))
            {
                return configValue.ToLowerInvariant();
            }

            // Default is "warn"
            return "warn";
        }
    }
}