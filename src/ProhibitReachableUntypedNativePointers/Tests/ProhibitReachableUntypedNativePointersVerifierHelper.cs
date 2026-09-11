using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using AN.CodeAnalyzers.ProhibitReachableUntypedNativePointers;

namespace AN.CodeAnalyzers.Tests.ProhibitReachableUntypedNativePointers
{
    /// <summary>
    /// Builds <see cref="ProhibitReachableUntypedNativePointersAnalyzer"/> verification tests.
    /// Uses the .NET 8 reference assemblies so BCL surface such as File.OpenHandle, RandomAccess,
    /// NativeMemory and Span&lt;T&gt;(void*, int) is available to the code under test.
    /// </summary>
    public static class ProhibitReachableUntypedNativePointersVerifierHelper
    {
        public static CSharpAnalyzerTest<ProhibitReachableUntypedNativePointersAnalyzer, DefaultVerifier> CreateNoDiagnosticsTest(
            string sourceCode,
            string enforcementLevel = "warn",
            IEnumerable<MetadataReference>? additionalReferences = null)
        {
            return createTest(sourceCode, enforcementLevel, additionalReferences);
        }

        /// <summary>Use <c>{|#0:...|}</c> markup to mark expected diagnostic locations.</summary>
        public static CSharpAnalyzerTest<ProhibitReachableUntypedNativePointersAnalyzer, DefaultVerifier> CreateDiagnosticsTest(
            string sourceCode,
            DiagnosticResult[] expectedDiagnostics,
            string enforcementLevel = "warn",
            IEnumerable<MetadataReference>? additionalReferences = null)
        {
            var analyzerTest = createTest(sourceCode, enforcementLevel, additionalReferences);
            analyzerTest.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            return analyzerTest;
        }

        /// <summary>AN0102 at the given markup index. Only ID, severity and location are verified; message text is covered by dedicated tests.</summary>
        public static DiagnosticResult ExpectWarning(int markupIndex)
        {
            return new DiagnosticResult(ProhibitReachableUntypedNativePointersAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(markupIndex);
        }

        public static DiagnosticResult ExpectError(int markupIndex)
        {
            return new DiagnosticResult(ProhibitReachableUntypedNativePointersAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(markupIndex);
        }

        /// <summary>
        /// Compiles a small "someone else's assembly" that declares an IntPtr-in-a-costume struct and a
        /// method returning it, so tests can prove the costume rule sees METADATA (matrix row 12).
        /// Compiled against the same .NET 8 reference assemblies the test compilation uses.
        /// </summary>
        public static async System.Threading.Tasks.Task<MetadataReference> CreateCostumeReferenceAssemblyAsync()
        {
            const string costumeSource = @"
using System;
namespace ThirdParty
{
    public struct HWND { public IntPtr Value; }
    public static class Win
    {
        public static HWND GetForegroundWindow() => default;
        public static bool SetForegroundWindow(HWND h) => true;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(costumeSource);
            var references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, System.Threading.CancellationToken.None);

            var compilation = CSharpCompilation.Create(
                "ThirdParty.Costume",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
                throw new System.InvalidOperationException("Costume assembly failed to compile: " + string.Join("\n", emitResult.Diagnostics));

            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        /// <summary>
        /// Runs the analyzer directly against a compilation built on the runtime's trusted platform assemblies
        /// and returns the AN0102 diagnostics, so tests can assert on the RENDERED message text.
        /// </summary>
        public static async System.Threading.Tasks.Task<ImmutableArray<Diagnostic>> RunAndCollectAsync(string sourceCode, string enforcementLevel = "warn")
        {
            var references = new List<MetadataReference>();
            foreach (var path in ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                references.Add(MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create(
                "UnderTest",
                new[] { CSharpSyntaxTree.ParseText(sourceCode) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var options = new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new SingleValueConfigOptionsProvider("build_property." + ProhibitReachableUntypedNativePointersAnalyzer.BuildPropertyName, enforcementLevel));

            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new ProhibitReachableUntypedNativePointersAnalyzer()),
                options);

            return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private sealed class SingleValueConfigOptionsProvider : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
        {
            private readonly SingleValueConfigOptions options;
            public SingleValueConfigOptionsProvider(string key, string value) { options = new SingleValueConfigOptions(key, value); }
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions => options;
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => options;
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => options;
        }

        private sealed class SingleValueConfigOptions : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            private readonly string key;
            private readonly string value;
            public SingleValueConfigOptions(string key, string value) { this.key = key; this.value = value; }
            public override bool TryGetValue(string key, out string value)
            {
                if (key == this.key) { value = this.value; return true; }
                value = null!;
                return false;
            }
        }

        private static CSharpAnalyzerTest<ProhibitReachableUntypedNativePointersAnalyzer, DefaultVerifier> createTest(
            string sourceCode,
            string enforcementLevel,
            IEnumerable<MetadataReference>? additionalReferences)
        {
            var analyzerTest = new CSharpAnalyzerTest<ProhibitReachableUntypedNativePointersAnalyzer, DefaultVerifier>
            {
                TestCode = sourceCode,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };

            // The code under test is FFI code: it needs unsafe.
            analyzerTest.SolutionTransforms.Add((solution, projectId) =>
            {
                var project = solution.GetProject(projectId)!;
                var options = (CSharpCompilationOptions)project.CompilationOptions!;
                return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
            });

            if (additionalReferences != null)
            {
                foreach (var reference in additionalReferences)
                    analyzerTest.TestState.AdditionalReferences.Add(reference);
            }

            analyzerTest.TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
                "is_global = true\nbuild_property." + ProhibitReachableUntypedNativePointersAnalyzer.BuildPropertyName + " = " + enforcementLevel + "\n"));

            return analyzerTest;
        }
    }
}