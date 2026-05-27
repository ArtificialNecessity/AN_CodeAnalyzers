using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.ProhibitNamespaceAccess
{
    public class ProhibitNamespaceAccessAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Explicit type reference — error mode (qualified name)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ExplicitTypeReference_QualifiedName_ErrorMode()
        {
            // Stub type with no self-references
            const string testSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public class MyClass
{
    Prohibited.Interop.{|#0:NativeHandle|} _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Explicit type reference — warn mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ExplicitTypeReference_WarnMode_ProducesWarning()
        {
            const string testSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public class MyClass
{
    Prohibited.Interop.{|#0:NativeHandle|} _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessWarning(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ warn = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Simple identifier with using directive
        // ──────────────────────────────────────────────

        [Fact]
        public async Task SimpleIdentifier_WithUsing_BothFlagged()
        {
            // Using directive must come before namespace declaration in C#
            // Define type in separate source to keep test file clean
            const string typeDefinitionSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}";
            const string testSource = @"
{|#0:using Prohibited.Interop;|}

public class MyClass
{
    {|#1:NativeHandle|} _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectUsingDirectiveWarning(0, "Prohibited.Interop", "Prohibited.Interop"),
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(1, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            analyzerTest.TestState.Sources.Add(("Types.cs", typeDefinitionSource));
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Glob pattern — matches child namespace
        // ──────────────────────────────────────────────

        [Fact]
        public async Task GlobPattern_MatchesChildNamespace()
        {
            const string testSource = @"
namespace Prohibited.Interop.Advanced
{
    public class AdvancedHandle { }
}

public class MyClass
{
    Prohibited.Interop.Advanced.{|#0:AdvancedHandle|} _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "AdvancedHandle", "Prohibited.Interop.Advanced", "Prohibited.Interop*"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop*"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Glob pattern — matches exact prefix namespace
        // ──────────────────────────────────────────────

        [Fact]
        public async Task GlobPattern_MatchesExactPrefixNamespace()
        {
            const string testSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public class MyClass
{
    Prohibited.Interop.{|#0:NativeHandle|} _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop*"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop*"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Exact pattern does NOT match child namespace
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ExactPattern_DoesNotMatchChildNamespace()
        {
            const string testSource = @"
namespace Prohibited.Interop.Advanced
{
    public class AdvancedHandle { }
}

public class MyClass
{
    Prohibited.Interop.Advanced.AdvancedHandle _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateNoDiagnosticsTest(
                testSource,
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Disabled mode — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task EmptyConfig_NoDiagnostics()
        {
            const string testSource = @"
using System;

public class MyClass
{
    IntPtr _handle;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateNoDiagnosticsTest(
                testSource, configValue: "");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Clean code — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CleanCode_NoDiagnostics()
        {
            const string testSource = @"
namespace Allowed.Namespace
{
    public class SafeType { }
}

public class MyClass
{
    Allowed.Namespace.SafeType _safe;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateNoDiagnosticsTest(
                testSource,
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Var inference — catches prohibited type leaked
        // through a method that returns the prohibited type.
        // Factory is in a separate file so deduplication
        // doesn't merge the var with the return type ref.
        // ──────────────────────────────────────────────

        [Fact]
        public async Task VarInference_CatchesProhibitedType()
        {
            // Factory defined in a separate source file — its NativeHandle return type
            // reference is in a different syntax tree, so deduplication is per-file.
            const string factorySource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public static class Factory
{
    public static Prohibited.Interop.NativeHandle Create() => default;
}";
            // Test file only has the var reference — the inferred type is NativeHandle
            const string testSource = @"
public class MyClass
{
    public void DoWork()
    {
        {|#0:var|} handle = Factory.Create();
    }
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    // The var infers NativeHandle which is in a prohibited namespace
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            // Add the factory as a separate source file
            analyzerTest.TestState.Sources.Add(("Factory.cs", factorySource));

            // Factory.cs also has a NativeHandle reference (return type) — expect that diagnostic too
            analyzerTest.ExpectedDiagnostics.Add(
                new DiagnosticResult("AN0105", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .WithSpan("Factory.cs", 9, 38, 9, 50)
                    .WithArguments("NativeHandle", "Prohibited.Interop", "Prohibited.Interop"));

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Return type — flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ReturnType_Flagged()
        {
            const string testSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public class MyClass
{
    public Prohibited.Interop.{|#0:NativeHandle|} GetHandle() => default;
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Parameter type — flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ParameterType_Flagged()
        {
            const string testSource = @"
namespace Prohibited.Interop
{
    public class NativeHandle { }
}

public class MyClass
{
    public void Process(Prohibited.Interop.{|#0:NativeHandle|} handle) { }
}";
            var analyzerTest = ProhibitNamespaceAccessVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitNamespaceAccessVerifierHelper.ExpectTypeAccessError(0, "NativeHandle", "Prohibited.Interop", "Prohibited.Interop"),
                },
                configValue: @"{ error = [ ""Prohibited.Interop"" ] }");

            await analyzerTest.RunAsync();
        }
    }
}