using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.StableABIVerification;

namespace AN.CodeAnalyzers.Tests.StableABIVerification
{
    /// <summary>
    /// Helper to build and run <see cref="PublicConstAnalyzer"/> verification tests.
    /// </summary>
    public static class PublicConstAnalyzerVerifierHelper
    {
        /// <summary>
        /// Source text for the PermanentConst attribute that the analyzer checks for.
        /// Included in tests that use the attribute so the test compilation can resolve it.
        /// </summary>
        private const string permanentConstAttributeSourceText = @"
using System;

namespace AN.CodeAnalyzers.StableABIVerification
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class PermanentConstAttribute : Attribute { }
}
";

        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<PublicConstAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode)
        {
            var analyzerTest = new CSharpAnalyzerTest<PublicConstAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("PermanentConstAttribute.cs", permanentConstAttributeSourceText));
            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:MemberName|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<PublicConstAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics)
        {
            var analyzerTest = new CSharpAnalyzerTest<PublicConstAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("PermanentConstAttribute.cs", permanentConstAttributeSourceText));
            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0002 at the given markup location index.
        /// </summary>
        public static DiagnosticResult ExpectAN0002(int markupIndex, string qualifiedFieldName)
        {
            return new DiagnosticResult(PublicConstAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex)
                .WithArguments(qualifiedFieldName);
        }
    }
}