using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.RequireTypedPointersNotIntPtr;

namespace AN.CodeAnalyzers.Tests.RequireTypedPointersNotIntPtr
{
    /// <summary>
    /// Helper to build and run <see cref="RequireTypedPointersNotIntPtrAnalyzer"/> verification tests
    /// with configurable MSBuild properties (RequireTypedPointersNotIntPtr enforcement level).
    /// </summary>
    public static class RequireTypedPointersNotIntPtrVerifierHelper
    {
        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<RequireTypedPointersNotIntPtrAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string enforcementLevel = "warn")
        {
            var analyzerTest = new CSharpAnalyzerTest<RequireTypedPointersNotIntPtrAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            addGlobalConfig(analyzerTest, enforcementLevel);
            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:TypeName|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<RequireTypedPointersNotIntPtrAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string enforcementLevel = "warn")
        {
            var analyzerTest = new CSharpAnalyzerTest<RequireTypedPointersNotIntPtrAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, enforcementLevel);
            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0100 (IntPtr/UIntPtr anywhere) at the given markup location index.
        /// </summary>
        public static DiagnosticResult ExpectIntPtrError(int markupIndex, string typeName)
        {
            return new DiagnosticResult(RequireTypedPointersNotIntPtrAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(typeName);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0100 (IntPtr/UIntPtr anywhere) as a warning.
        /// </summary>
        public static DiagnosticResult ExpectIntPtrWarning(int markupIndex, string typeName)
        {
            return new DiagnosticResult(RequireTypedPointersNotIntPtrAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(typeName);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0100 (nint/nuint in P/Invoke) as an error.
        /// </summary>
        public static DiagnosticResult ExpectNintPInvokeError(int markupIndex, string typeName)
        {
            return new DiagnosticResult(RequireTypedPointersNotIntPtrAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(typeName);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0100 (nint/nuint in P/Invoke) as a warning.
        /// </summary>
        public static DiagnosticResult ExpectNintPInvokeWarning(int markupIndex, string typeName)
        {
            return new DiagnosticResult(RequireTypedPointersNotIntPtrAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(typeName);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<RequireTypedPointersNotIntPtrAnalyzer, DefaultVerifier> analyzerTest,
            string enforcementLevel)
        {
            var globalConfigContent = $@"is_global = true
build_property.RequireTypedPointersNotIntPtr = {enforcementLevel}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}