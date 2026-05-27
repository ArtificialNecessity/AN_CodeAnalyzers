using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.ProhibitPlatformImports;

namespace AN.CodeAnalyzers.Tests.ProhibitPlatformImports
{
    /// <summary>
    /// Helper to build and run <see cref="ProhibitPlatformImportsAnalyzer"/> verification tests
    /// with configurable MSBuild properties (ProhibitPlatformImports enforcement level).
    /// </summary>
    public static class ProhibitPlatformImportsVerifierHelper
    {
        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<ProhibitPlatformImportsAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string configValue = "disabled")
        {
            var analyzerTest = new CSharpAnalyzerTest<ProhibitPlatformImportsAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            addGlobalConfig(analyzerTest, configValue);
            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:code|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<ProhibitPlatformImportsAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string configValue = "error")
        {
            var analyzerTest = new CSharpAnalyzerTest<ProhibitPlatformImportsAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, configValue);
            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0104 (platform import attribute) as an error.
        /// </summary>
        public static DiagnosticResult ExpectPlatformImportError(int markupIndex, string methodName, string configValue = "error")
        {
            return new DiagnosticResult(ProhibitPlatformImportsAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(methodName, configValue);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0104 (platform import attribute) as a warning.
        /// </summary>
        public static DiagnosticResult ExpectPlatformImportWarning(int markupIndex, string methodName, string configValue = "warn")
        {
            return new DiagnosticResult(ProhibitPlatformImportsAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(methodName, configValue);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0104 (NativeLibrary call) as an error.
        /// </summary>
        public static DiagnosticResult ExpectNativeLibraryCallError(int markupIndex, string calledMethodFullName, string configValue = "error")
        {
            return new DiagnosticResult(ProhibitPlatformImportsAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(calledMethodFullName, configValue);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0104 (NativeLibrary call) as a warning.
        /// </summary>
        public static DiagnosticResult ExpectNativeLibraryCallWarning(int markupIndex, string calledMethodFullName, string configValue = "warn")
        {
            return new DiagnosticResult(ProhibitPlatformImportsAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(calledMethodFullName, configValue);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<ProhibitPlatformImportsAnalyzer, DefaultVerifier> analyzerTest,
            string configValue)
        {
            var globalConfigContent = $@"is_global = true
build_property.ProhibitPlatformImports = {configValue}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}