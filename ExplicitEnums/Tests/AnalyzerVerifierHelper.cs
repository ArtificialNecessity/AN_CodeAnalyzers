using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.ExplicitEnums;

namespace AN.CodeAnalyzers.Tests.ExplicitEnums
{
    /// <summary>
    /// Helper to build and run <see cref="ExplicitEnumValuesAnalyzer"/> verification tests
    /// with configurable MSBuild properties (EnforceExplicitEnumValues scope).
    /// </summary>
    public static class AnalyzerVerifierHelper
    {
        /// <summary>
        /// Source text for the attributes that the analyzer checks for.
        /// Included in every test so the test compilation can resolve them.
        /// </summary>
        private const string attributeSourceText = @"
using System;

namespace AN.CodeAnalyzers.ExplicitEnums
{
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class SuppressExplicitEnumValuesAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class RequireExplicitEnumValuesAttribute : Attribute { }
}
";

        /// <summary>
        /// Creates a test that expects NO diagnostics from the given source code.
        /// </summary>
        public static CSharpAnalyzerTest<ExplicitEnumValuesAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string enforcementScope = "none")
        {
            var analyzerTest = new CSharpAnalyzerTest<ExplicitEnumValuesAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("Attributes.cs", attributeSourceText));
            addGlobalConfig(analyzerTest, enforcementScope);

            return analyzerTest;
        }

        /// <summary>
        /// Creates a test that expects specific diagnostics from the given source code.
        /// Use <c>{|#0:MemberName|}</c> markup in source to mark expected diagnostic locations.
        /// </summary>
        public static CSharpAnalyzerTest<ExplicitEnumValuesAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string enforcementScope = "none")
        {
            var analyzerTest = new CSharpAnalyzerTest<ExplicitEnumValuesAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
            };

            analyzerTest.TestState.Sources.Add(("Attributes.cs", attributeSourceText));
            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            addGlobalConfig(analyzerTest, enforcementScope);

            return analyzerTest;
        }

        /// <summary>
        /// Builds a <see cref="DiagnosticResult"/> for AN001 at the given markup location index.
        /// </summary>
        public static DiagnosticResult ExpectAN001(int markupIndex, string memberName)
        {
            return new DiagnosticResult(ExplicitEnumValuesAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex)
                .WithArguments(memberName);
        }

        private static void addGlobalConfig(
            CSharpAnalyzerTest<ExplicitEnumValuesAnalyzer, DefaultVerifier> analyzerTest,
            string enforcementScope)
        {
            var globalConfigContent = $@"is_global = true
build_property.EnforceExplicitEnumValues = {enforcementScope}
";
            analyzerTest.TestState.AnalyzerConfigFiles.Add(
                ("/.globalconfig", globalConfigContent));
        }
    }
}