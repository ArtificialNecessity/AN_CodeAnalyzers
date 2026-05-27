using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.EnforceNamingConventions
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EnforceNamingConventionsAnalyzer : DiagnosticAnalyzer
    {
        public const string NamingViolationDiagnosticId = "AN0200";
        public const string ConfigErrorDiagnosticId = "AN0201";
        private const string category = "NamingConventions";

        private static readonly DiagnosticDescriptor namingViolationRule = new DiagnosticDescriptor(
            NamingViolationDiagnosticId,
            "Symbol name does not match required naming convention",
            "{0} '{1}' does not match required naming pattern '{2}'. Rename to match the convention.",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor configErrorRule = new DiagnosticDescriptor(
            ConfigErrorDiagnosticId,
            "EnforceNamingConventions configuration error",
            "EnforceNamingConventions configuration error: {0}",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(namingViolationRule, configErrorRule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterCompilationStartAction(compilationStartContext =>
            {
                var configPropertyValue = getConfigPropertyValue(compilationStartContext);
                if (string.IsNullOrWhiteSpace(configPropertyValue))
                    return;

                Dictionary<string, string> rulesByCategory;
                try
                {
                    rulesByCategory = NamingConventionRuleParser.Parse(configPropertyValue);
                }
                catch (FormatException parseException)
                {
                    reportConfigError(compilationStartContext, parseException.Message);
                    return;
                }

                if (rulesByCategory.Count == 0)
                    return;

                // Pre-compile regex patterns with auto-anchoring
                var compiledRegexByCategory = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
                foreach (var ruleEntry in rulesByCategory)
                {
                    string anchoredPattern = "^(?:" + ruleEntry.Value + ")$";
                    try
                    {
                        compiledRegexByCategory[ruleEntry.Key] = new Regex(anchoredPattern, RegexOptions.Compiled);
                    }
                    catch (ArgumentException regexCompileException)
                    {
                        reportConfigError(compilationStartContext,
                            $"Invalid regex pattern for '{ruleEntry.Key}': {regexCompileException.Message}");
                        return;
                    }
                }

                // Register symbol actions for each supported category
                if (compiledRegexByCategory.TryGetValue("event", out var eventNameRegex))
                {
                    compilationStartContext.RegisterSymbolAction(
                        symbolContext => analyzeEventNaming(symbolContext, eventNameRegex),
                        SymbolKind.Event);
                }
            });
        }

        private static void analyzeEventNaming(SymbolAnalysisContext symbolContext, Regex eventNameRegex)
        {
            var eventSymbol = (IEventSymbol)symbolContext.Symbol;

            if (eventNameRegex.IsMatch(eventSymbol.Name))
                return;

            foreach (var declarationLocation in eventSymbol.Locations)
            {
                symbolContext.ReportDiagnostic(
                    Diagnostic.Create(
                        namingViolationRule,
                        declarationLocation,
                        "Event",
                        eventSymbol.Name,
                        eventNameRegex.ToString().Substring(4, eventNameRegex.ToString().Length - 6))); // strip ^(?:...)$
            }
        }

        private static string? getConfigPropertyValue(CompilationStartAnalysisContext compilationStartContext)
        {
            // We need to get the config from any syntax tree — all trees share the same build properties.
            // Use the first available syntax tree.
            var syntaxTrees = compilationStartContext.Compilation.SyntaxTrees;
            foreach (var syntaxTree in syntaxTrees)
            {
                var analyzerConfigOptions = compilationStartContext.Options.AnalyzerConfigOptionsProvider
                    .GetOptions(syntaxTree);

                if (analyzerConfigOptions.TryGetValue("build_property.EnforceNamingConventions", out var configValue)
                    && !string.IsNullOrEmpty(configValue))
                {
                    return configValue;
                }
            }

            return null;
        }

        /// <summary>
        /// Reports a configuration error as a compilation-end diagnostic.
        /// Also registers a no-op symbol action to satisfy RS1012 (CompilationStartAction must register actions).
        /// </summary>
        private static void reportConfigError(
            CompilationStartAnalysisContext compilationStartContext,
            string configErrorMessage)
        {
            compilationStartContext.RegisterCompilationEndAction(compilationEndContext =>
            {
                compilationEndContext.ReportDiagnostic(
                    Diagnostic.Create(configErrorRule, Location.None, configErrorMessage));
            });
        }
    }
}