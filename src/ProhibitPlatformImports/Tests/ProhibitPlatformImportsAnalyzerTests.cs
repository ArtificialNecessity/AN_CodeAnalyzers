using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.ProhibitPlatformImports
{
    public class ProhibitPlatformImportsAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // [DllImport] flagged in error mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task DllImport_ErrorMode_ProducesError()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    {|#0:[DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle|}(IntPtr hObject);
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportError(0, "CloseHandle"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [DllImport] flagged in warn mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task DllImport_WarnMode_ProducesWarning()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    {|#0:[DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle|}(IntPtr hObject);
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportWarning(0, "CloseHandle"),
                },
                configValue: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [LibraryImport] flagged in error mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task LibraryImport_ErrorMode_ProducesError()
        {
            // Define stub LibraryImportAttribute since it requires .NET 7+ reference assemblies
            const string testSource = @"
using System;

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LibraryImportAttribute : Attribute
    {
        public LibraryImportAttribute(string libraryName) { }
    }
}

public class NativeMethods
{
    {|#0:[System.Runtime.InteropServices.LibraryImport(""kernel32.dll"")]
    public static bool CloseHandle|}(IntPtr hObject) => false;
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportError(0, "CloseHandle"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [UnmanagedCallersOnly] flagged in error mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task UnmanagedCallersOnly_ErrorMode_ProducesError()
        {
            // Define stub UnmanagedCallersOnlyAttribute since it requires .NET 5+ reference assemblies
            const string testSource = @"
using System;

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute { }
}

public class NativeMethods
{
    {|#0:[System.Runtime.InteropServices.UnmanagedCallersOnly]
    public static int ManagedCallback|}(int value) => value * 2;
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportError(0, "ManagedCallback"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [UnmanagedCallersOnly] flagged in warn mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task UnmanagedCallersOnly_WarnMode_ProducesWarning()
        {
            const string testSource = @"
using System;

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute { }
}

public class NativeMethods
{
    {|#0:[System.Runtime.InteropServices.UnmanagedCallersOnly]
    public static int ManagedCallback|}(int value) => value * 2;
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportWarning(0, "ManagedCallback"),
                },
                configValue: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // NativeLibrary.Load flagged in error mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task NativeLibraryLoad_ErrorMode_ProducesError()
        {
            // Define stub NativeLibrary since it requires .NET Core 3.0+ reference assemblies
            const string testSource = @"
using System;

namespace System.Runtime.InteropServices
{
    public static class NativeLibrary
    {
        public static IntPtr Load(string libraryPath) => default;
    }
}

public class MyClass
{
    public void DoWork()
    {
        var libraryHandle = {|#0:System.Runtime.InteropServices.NativeLibrary.Load(""mylib.dll"")|};
    }
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectNativeLibraryCallError(0, "NativeLibrary.Load"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // NativeLibrary.TryLoad flagged in error mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task NativeLibraryTryLoad_ErrorMode_ProducesError()
        {
            const string testSource = @"
using System;

namespace System.Runtime.InteropServices
{
    public static class NativeLibrary
    {
        public static bool TryLoad(string libraryPath, out IntPtr handle) { handle = default; return false; }
    }
}

public class MyClass
{
    public void DoWork()
    {
        {|#0:System.Runtime.InteropServices.NativeLibrary.TryLoad(""mylib.dll"", out var libraryHandle)|};
    }
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectNativeLibraryCallError(0, "NativeLibrary.TryLoad"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // disabled mode — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task DllImport_DisabledMode_NoDiagnostic()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle(IntPtr hObject);
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateNoDiagnosticsTest(
                testSource, configValue: "disabled");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // absent property — no diagnostics (default disabled)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task DllImport_AbsentProperty_NoDiagnostic()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle(IntPtr hObject);
}";
            // Use empty string to simulate absent property
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateNoDiagnosticsTest(
                testSource, configValue: "");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Clean code — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CleanCode_ErrorMode_NoDiagnostic()
        {
            const string testSource = @"
public class MyClass
{
    private int _count;
    private string _name = """";

    public int GetCount() => _count;
    public void SetName(string name) { _name = name; }
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateNoDiagnosticsTest(
                testSource, configValue: "error");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Multiple DllImports in same class — all flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task MultipleDllImports_AllFlagged()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    {|#0:[DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle|}(IntPtr hObject);

    {|#1:[DllImport(""kernel32.dll"")]
    public static extern uint GetCurrentProcessId|}();
}";
            var analyzerTest = ProhibitPlatformImportsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportError(0, "CloseHandle"),
                    ProhibitPlatformImportsVerifierHelper.ExpectPlatformImportError(1, "GetCurrentProcessId"),
                },
                configValue: "error");

            await analyzerTest.RunAsync();
        }
    }
}