using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.CallersMustNameAllParameters;

namespace AN.CodeAnalyzers.Tests.CallersMustNameAllParameters
{
    /// <summary>
    /// Helper to build and run <see cref="CallersMustNameAllParametersAnalyzer"/> verification tests
    /// with configurable MSBuild properties (RequireNamedArgumentsEverywhereLikeObjectiveC).
    /// </summary>
    public static class CallersMustNameAllParametersVerifierHelper
    {
        /// <summary>
        /// Source text for the attribute that the analyzer checks for.
        /// Included in every test so the test compilation can resolve it.
        /// </summary>
        private const string attributeSourceText = @"
using System;

namespace AN.CodeAnalyzers.CallersMustNameAllParameters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class CallersMustNameAllParametersAttribute : Attribute { }
}
";

        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<CallersMustNameAllParametersAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string enforcementMode = "attribute-error")
        {
            var analyzerTest = new CSharpAnalyzerTest<CallersMustNameAllParametersAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("CallersMustNameAllParametersAttribute.cs", attributeSourceText));
            addGlobalConfig(analyzerTest, enforcementMode);

            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:code|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<CallersMustNameAllParametersAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string enforcementMode = "attribute-error")
        {
            var analyzerTest = new CSharpAnalyzerTest<CallersMustNameAllParametersAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("CallersMustNameAllParametersAttribute.cs", attributeSourceText));
            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, enforcementMode);

            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0103 at the given markup location index.
        /// </summary>
        public static DiagnosticResult ExpectAN0103Error(int markupIndex, int argumentPosition, string methodName)
        {
            return new DiagnosticResult(CallersMustNameAllParametersAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(argumentPosition, methodName);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0103 as a warning.
        /// </summary>
        public static DiagnosticResult ExpectAN0103Warning(int markupIndex, int argumentPosition, string methodName)
        {
            return new DiagnosticResult(CallersMustNameAllParametersAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(argumentPosition, methodName);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<CallersMustNameAllParametersAnalyzer, DefaultVerifier> analyzerTest,
            string enforcementMode)
        {
            var globalConfigContent = $@"is_global = true
build_property.RequireNamedArgumentsEverywhereLikeObjectiveC = {enforcementMode}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}