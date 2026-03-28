using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.RequireTypedPointersNotIntPtr
{
    public class RequireTypedPointersNotIntPtrAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // IntPtr flagged everywhere (warn mode)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IntPtr_Field_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    {|#0:IntPtr|} _handle;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task IntPtr_LocalVariable_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    public void DoWork()
    {
        {|#0:IntPtr|} localHandle = {|#1:IntPtr|}.Zero;
    }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(1, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task IntPtr_Parameter_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    public void DoWork({|#0:IntPtr|} handle) { }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task IntPtr_ReturnType_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    public {|#0:IntPtr|} GetHandle() => default;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task IntPtr_Property_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    public {|#0:IntPtr|} Handle { get; set; }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // UIntPtr flagged everywhere (warn mode)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task UIntPtr_Field_Flagged()
        {
            const string testSource = @"
using System;
public class MyClass
{
    {|#0:UIntPtr|} _size;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "UIntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // IntPtr inside struct — still flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IntPtr_InsideStruct_Flagged()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct HWND
{
    public {|#0:IntPtr|} Value;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // IntPtr in DllImport — flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IntPtr_InDllImport_Flagged()
        {
            const string testSource = @"
using System;
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool CloseHandle({|#0:IntPtr|} hObject);
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrWarning(0, "IntPtr"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // nint/nuint in regular code — NOT flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Nint_InRegularCode_NotFlagged()
        {
            const string testSource = @"
public class MyClass
{
    public void DoWork()
    {
        nint bufferSize = 1024;
        nuint count = 42;
    }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "warn");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Nint_AsField_NotFlagged()
        {
            const string testSource = @"
public class MyClass
{
    nint _size;
    nuint _count;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "warn");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Nint_AsParameter_NotFlagged()
        {
            const string testSource = @"
public class MyClass
{
    public void DoWork(nint size, nuint count) { }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "warn");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // nint/nuint in P/Invoke — flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Nint_InDllImport_Flagged()
        {
            const string testSource = @"
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool SomeCall({|#0:nint|} handle);
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectNintPInvokeWarning(0, "nint"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Nuint_InDllImport_Flagged()
        {
            const string testSource = @"
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern {|#0:nuint|} GetSize();
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectNintPInvokeWarning(0, "nuint"),
                },
                enforcementLevel: "warn");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // disallow mode — produces Error severity
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IntPtr_DisallowMode_ProducesError()
        {
            const string testSource = @"
using System;
public class MyClass
{
    {|#0:IntPtr|} _handle;
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectIntPtrError(0, "IntPtr"),
                },
                enforcementLevel: "disallow");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Nint_InDllImport_DisallowMode_ProducesError()
        {
            const string testSource = @"
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool SomeCall({|#0:nint|} handle);
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    RequireTypedPointersNotIntPtrVerifierHelper.ExpectNintPInvokeError(0, "nint"),
                },
                enforcementLevel: "disallow");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // ignore mode — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IntPtr_IgnoreMode_NoDiagnostic()
        {
            const string testSource = @"
using System;
public class MyClass
{
    IntPtr _handle;
    public IntPtr GetHandle() => _handle;
    public void SetHandle(IntPtr value) { _handle = value; }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "ignore");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Nint_InDllImport_IgnoreMode_NoDiagnostic()
        {
            const string testSource = @"
using System.Runtime.InteropServices;

public class NativeMethods
{
    [DllImport(""kernel32.dll"")]
    public static extern bool SomeCall(nint handle);
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "ignore");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Clean code — no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CleanCode_NoDiagnostic()
        {
            const string testSource = @"
public class MyClass
{
    private int _count;
    private string _name = """";

    public int GetCount() => _count;
    public void SetName(string name) { _name = name; }
}";
            var analyzerTest = RequireTypedPointersNotIntPtrVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementLevel: "warn");
            await analyzerTest.RunAsync();
        }
    }
}