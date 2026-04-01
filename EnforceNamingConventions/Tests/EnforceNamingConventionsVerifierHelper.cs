using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.EnforceNamingConventions;

namespace AN.CodeAnalyzers.Tests.EnforceNamingConventions
{
    /// <summary>
    /// Helper to build and run <see cref="EnforceNamingConventionsAnalyzer"/> verification tests
    /// with configurable MSBuild properties (EnforceNamingConventions rule string).
    /// </summary>
    public static class EnforceNamingConventionsVerifierHelper
    {
        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<EnforceNamingConventionsAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string namingConventionsConfig = "")
        {
            var analyzerTest = new CSharpAnalyzerTest<EnforceNamingConventionsAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            addGlobalConfig(analyzerTest, namingConventionsConfig);
            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:SymbolName|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<EnforceNamingConventionsAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string namingConventionsConfig = "")
        {
            var analyzerTest = new CSharpAnalyzerTest<EnforceNamingConventionsAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, namingConventionsConfig);
            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0200 (naming violation) at the given markup location index.
        /// </summary>
        public static DiagnosticResult ExpectNamingViolation(
            int markupIndex,
            string symbolCategory,
            string symbolName,
            string expectedPattern)
        {
            return new DiagnosticResult(
                    EnforceNamingConventionsAnalyzer.NamingViolationDiagnosticId,
                    DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(symbolCategory, symbolName, expectedPattern);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0201 (config error) with no specific location.
        /// The <paramref name="errorMessageArgument"/> is the {0} argument in the message format.
        /// </summary>
        public static DiagnosticResult ExpectConfigError(string errorMessageArgument)
        {
            return new DiagnosticResult(
                    EnforceNamingConventionsAnalyzer.ConfigErrorDiagnosticId,
                    DiagnosticSeverity.Warning)
                .WithArguments(errorMessageArgument);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<EnforceNamingConventionsAnalyzer, DefaultVerifier> analyzerTest,
            string namingConventionsConfig)
        {
            var globalConfigContent = $@"is_global = true
build_property.EnforceNamingConventions = {namingConventionsConfig}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}