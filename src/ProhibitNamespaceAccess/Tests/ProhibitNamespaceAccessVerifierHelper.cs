using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.ProhibitNamespaceAccess;

namespace AN.CodeAnalyzers.Tests.ProhibitNamespaceAccess
{
    /// <summary>
    /// Helper to build and run <see cref="ProhibitNamespaceAccessAnalyzer"/> verification tests
    /// with configurable MSBuild properties.
    /// </summary>
    public static class ProhibitNamespaceAccessVerifierHelper
    {
        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<ProhibitNamespaceAccessAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string configValue = "")
        {
            var analyzerTest = new CSharpAnalyzerTest<ProhibitNamespaceAccessAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            addGlobalConfig(analyzerTest, configValue);
            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<ProhibitNamespaceAccessAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string configValue)
        {
            var analyzerTest = new CSharpAnalyzerTest<ProhibitNamespaceAccessAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, configValue);
            return analyzerTest;
        }

        /// <summary>
        /// Builds a DiagnosticResult for AN0105 type access at the given markup location as an error.
        /// </summary>
        public static DiagnosticResult ExpectTypeAccessError(int markupIndex, string typeName, string namespaceName, string patternString)
        {
            return new DiagnosticResult(ProhibitNamespaceAccessAnalyzer.TypeAccessDiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(typeName, namespaceName, patternString);
        }

        /// <summary>
        /// Builds a DiagnosticResult for AN0105 type access at the given markup location as a warning.
        /// </summary>
        public static DiagnosticResult ExpectTypeAccessWarning(int markupIndex, string typeName, string namespaceName, string patternString)
        {
            return new DiagnosticResult(ProhibitNamespaceAccessAnalyzer.TypeAccessDiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(typeName, namespaceName, patternString);
        }

        /// <summary>
        /// Builds a DiagnosticResult for AN0105 using directive (always warning).
        /// </summary>
        public static DiagnosticResult ExpectUsingDirectiveWarning(int markupIndex, string namespaceName, string patternString)
        {
            return new DiagnosticResult(ProhibitNamespaceAccessAnalyzer.TypeAccessDiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(namespaceName, patternString);
        }

        /// <summary>
        /// Builds a DiagnosticResult for AN0106 config parse error (always warning, no location).
        /// </summary>
        public static DiagnosticResult ExpectConfigParseError(string parseErrorMessage)
        {
            return new DiagnosticResult(ProhibitNamespaceAccessAnalyzer.ConfigErrorDiagnosticId, DiagnosticSeverity.Warning)
                .WithArguments(parseErrorMessage);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<ProhibitNamespaceAccessAnalyzer, DefaultVerifier> analyzerTest,
            string configValue)
        {
            var globalConfigContent = $@"is_global = true
build_property.ProhibitNamespaceAccess = {configValue}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}