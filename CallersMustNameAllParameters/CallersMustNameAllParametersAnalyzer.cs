using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.CallersMustNameAllParameters
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CallersMustNameAllParametersAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0103";
        private const string category = "Naming";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Method requires named parameters at call site",
            "Argument {0} to '{1}' must be named. Use named arguments for all parameters, e.g. MyMethod(argA: 1, argB: 2).",
            category,
            DiagnosticSeverity.Error, // default; overridden by config
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterCompilationStartAction(compilationStartContext =>
            {
                var enforcementConfig = parseEnforcementConfig(compilationStartContext.Options, compilationStartContext.Compilation.SyntaxTrees.FirstOrDefault());

                if (enforcementConfig.IsIgnored)
                {
                    return; // Analyzer disabled
                }

                compilationStartContext.RegisterSyntaxNodeAction(
                    syntaxNodeContext => analyzeInvocation(syntaxNodeContext, enforcementConfig),
                    SyntaxKind.InvocationExpression);

                compilationStartContext.RegisterSyntaxNodeAction(
                    syntaxNodeContext => analyzeObjectCreation(syntaxNodeContext, enforcementConfig),
                    SyntaxKind.ObjectCreationExpression);
            });
        }

        private void analyzeInvocation(SyntaxNodeAnalysisContext syntaxNodeContext, EnforcementConfig enforcementConfig)
        {
            var invocationExpressionSyntax = (InvocationExpressionSyntax)syntaxNodeContext.Node;
            var methodSymbol = syntaxNodeContext.SemanticModel.GetSymbolInfo(invocationExpressionSyntax, syntaxNodeContext.CancellationToken).Symbol as IMethodSymbol;

            if (methodSymbol == null)
            {
                return;
            }

            analyzeArgumentList(syntaxNodeContext, invocationExpressionSyntax.ArgumentList, methodSymbol, enforcementConfig);
        }

        private void analyzeObjectCreation(SyntaxNodeAnalysisContext syntaxNodeContext, EnforcementConfig enforcementConfig)
        {
            var objectCreationSyntax = (ObjectCreationExpressionSyntax)syntaxNodeContext.Node;
            var constructorSymbol = syntaxNodeContext.SemanticModel.GetSymbolInfo(objectCreationSyntax, syntaxNodeContext.CancellationToken).Symbol as IMethodSymbol;

            if (constructorSymbol == null || objectCreationSyntax.ArgumentList == null)
            {
                return;
            }

            analyzeArgumentList(syntaxNodeContext, objectCreationSyntax.ArgumentList, constructorSymbol, enforcementConfig);
        }

        private void analyzeArgumentList(
            SyntaxNodeAnalysisContext syntaxNodeContext,
            ArgumentListSyntax argumentListSyntax,
            IMethodSymbol methodSymbol,
            EnforcementConfig enforcementConfig)
        {
            // Single-parameter methods are always exempt
            if (methodSymbol.Parameters.Length <= 1)
            {
                return;
            }

            // Determine which mode applies
            bool hasAttribute = hasCallersMustNameAllParametersAttribute(methodSymbol);
            DiagnosticSeverity? effectiveSeverity = null;

            if (hasAttribute && enforcementConfig.AttributeModeSeverity.HasValue)
            {
                effectiveSeverity = enforcementConfig.AttributeModeSeverity.Value;
            }
            else if (!hasAttribute && enforcementConfig.EverywhereModeSeverity.HasValue)
            {
                effectiveSeverity = enforcementConfig.EverywhereModeSeverity.Value;
            }

            if (!effectiveSeverity.HasValue)
            {
                return; // No enforcement for this call site
            }

            // Check each argument
            var argumentsArray = argumentListSyntax.Arguments.ToArray();
            for (int argumentIndex = 0; argumentIndex < argumentsArray.Length; argumentIndex++)
            {
                var argumentSyntax = argumentsArray[argumentIndex];

                // Skip if already named
                if (argumentSyntax.NameColon != null)
                {
                    continue;
                }

                // Skip params array arguments
                if (argumentIndex >= methodSymbol.Parameters.Length - 1 &&
                    methodSymbol.Parameters.Length > 0 &&
                    methodSymbol.Parameters[methodSymbol.Parameters.Length - 1].IsParams)
                {
                    continue;
                }

                // Report diagnostic for unnamed argument
                var effectiveRule = new DiagnosticDescriptor(
                    rule.Id,
                    rule.Title,
                    rule.MessageFormat,
                    rule.Category,
                    effectiveSeverity.Value,
                    rule.IsEnabledByDefault);

                var diagnostic = Diagnostic.Create(
                    effectiveRule,
                    argumentSyntax.GetLocation(),
                    argumentIndex + 1, // 1-based for user-facing message
                    methodSymbol.Name);

                syntaxNodeContext.ReportDiagnostic(diagnostic);
            }
        }

        private bool hasCallersMustNameAllParametersAttribute(IMethodSymbol methodSymbol)
        {
            return methodSymbol.GetAttributes().Any(attributeData =>
                attributeData.AttributeClass?.Name == nameof(CallersMustNameAllParametersAttribute) ||
                attributeData.AttributeClass?.Name == "CallersMustNameAllParameters");
        }

        private EnforcementConfig parseEnforcementConfig(AnalyzerOptions analyzerOptions, SyntaxTree? syntaxTree)
        {
            if (syntaxTree == null)
            {
                return new EnforcementConfig(DiagnosticSeverity.Error, null); // default: attribute-error
            }

            var analyzerConfigOptions = analyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);

            if (!analyzerConfigOptions.TryGetValue("build_property.RequireNamedArgumentsEverywhereLikeObjectiveC", out var configValue) ||
                string.IsNullOrWhiteSpace(configValue))
            {
                return new EnforcementConfig(DiagnosticSeverity.Error, null); // default: attribute-error
            }

            // Parse comma-separated values
            var configParts = configValue.Split(',').Select(part => part.Trim().ToLowerInvariant()).ToArray();

            if (configParts.Contains("ignore"))
            {
                return EnforcementConfig.Ignored;
            }

            DiagnosticSeverity? attributeModeSeverity = null;
            DiagnosticSeverity? everywhereModeSeverity = null;

            foreach (var configPart in configParts)
            {
                switch (configPart)
                {
                    case "attribute-error":
                        attributeModeSeverity = DiagnosticSeverity.Error;
                        break;
                    case "attribute-warn":
                        if (!attributeModeSeverity.HasValue) // error wins over warn
                        {
                            attributeModeSeverity = DiagnosticSeverity.Warning;
                        }
                        break;
                    case "everywhere-error":
                        everywhereModeSeverity = DiagnosticSeverity.Error;
                        break;
                    case "everywhere-warn":
                        if (!everywhereModeSeverity.HasValue) // error wins over warn
                        {
                            everywhereModeSeverity = DiagnosticSeverity.Warning;
                        }
                        break;
                }
            }

            // If nothing was set, default to attribute-error
            if (!attributeModeSeverity.HasValue && !everywhereModeSeverity.HasValue)
            {
                attributeModeSeverity = DiagnosticSeverity.Error;
            }

            return new EnforcementConfig(attributeModeSeverity, everywhereModeSeverity);
        }

        private readonly struct EnforcementConfig
        {
            public readonly DiagnosticSeverity? AttributeModeSeverity;
            public readonly DiagnosticSeverity? EverywhereModeSeverity;
            public readonly bool IsIgnored;

            public EnforcementConfig(DiagnosticSeverity? attributeModeSeverity, DiagnosticSeverity? everywhereModeSeverity)
            {
                AttributeModeSeverity = attributeModeSeverity;
                EverywhereModeSeverity = everywhereModeSeverity;
                IsIgnored = false;
            }

            private EnforcementConfig(bool isIgnored)
            {
                AttributeModeSeverity = null;
                EverywhereModeSeverity = null;
                IsIgnored = isIgnored;
            }

            public static readonly EnforcementConfig Ignored = new EnforcementConfig(true);
        }
    }
}