using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.PureFunction;

namespace AN.CodeAnalyzers.Tests.PureFunction
{
    /// <summary>
    /// Helper to build and run <see cref="PureFunctionAnalyzer"/> verification tests.
    /// Embeds the PureFunctionAttribute source so test compilations can resolve it.
    /// </summary>
    public static class PureFunctionVerifierHelper
    {
        /// <summary>
        /// Source text for the attribute that the analyzer checks for.
        /// Included in every test so the test compilation can resolve it.
        /// </summary>
        private const string attributeSourceText = @"
using System;

namespace AN.CodeAnalyzers.PureFunction
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class PureFunctionAttribute : Attribute
    {
        public bool Transitive { get; set; } = true;
    }
}
";

        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<PureFunctionAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode)
        {
            var analyzerTest = new CSharpAnalyzerTest<PureFunctionAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("PureFunctionAttribute.cs", attributeSourceText));

            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:code|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<PureFunctionAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics)
        {
            var analyzerTest = new CSharpAnalyzerTest<PureFunctionAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("PureFunctionAttribute.cs", attributeSourceText));
            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);

            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0501 at the given markup location index.
        /// For direct mutations (no call chain).
        /// </summary>
        public static DiagnosticResult ExpectAN0501Error(
            int markupIndex,
            string methodName,
            string memberKind,
            string memberName)
        {
            string detail = $"Assignment to instance {memberKind} '{memberName}' is not allowed here.";
            return new DiagnosticResult(PureFunctionAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(methodName, detail);
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN0501 at the given markup location index.
        /// For transitive mutations detected via a call chain.
        /// <paramref name="callChain"/> is the full chain string, e.g. "Draw \u2192 DrawHeader \u2192 ResetState".
        /// </summary>
        public static DiagnosticResult ExpectAN0501TransitiveError(
            int markupIndex,
            string pureFunctionMethodName,
            string memberKind,
            string memberName,
            string callChain)
        {
            string detail = $"Assignment to instance {memberKind} '{memberName}' is not allowed (via call chain: {callChain}).";
            return new DiagnosticResult(PureFunctionAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(pureFunctionMethodName, detail);
        }
    }
}