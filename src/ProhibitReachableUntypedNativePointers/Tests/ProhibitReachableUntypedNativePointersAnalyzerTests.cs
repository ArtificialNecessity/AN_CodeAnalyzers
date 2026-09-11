using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Helper = AN.CodeAnalyzers.Tests.ProhibitReachableUntypedNativePointers.ProhibitReachableUntypedNativePointersVerifierHelper;

namespace AN.CodeAnalyzers.Tests.ProhibitReachableUntypedNativePointers
{
    /// <summary>Test matrix from _TASKS/An0102_ProhibitReachableUntypedNativePointers.md. Row numbers in method names.</summary>
    public class ProhibitReachableUntypedNativePointersAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Sources — a member returns an untyped native pointer
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Row01_FileOpenHandle_SourceAndVar()
        {
            const string source = @"
using System.IO;
public class C
{
    public void M(string p)
    {
        {|#0:var|} h = {|#1:File.OpenHandle(p, FileMode.Open)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row05_FileStreamSafeFileHandle_PropertyRead()
        {
            const string source = @"
using System.IO;
public class C
{
    public void M(FileStream fs)
    {
        {|#0:var|} h = {|#1:fs.SafeFileHandle|};
    }
}";
            // `fs` and `FileStream` are clean (a managed object); touching .SafeFileHandle is the source.
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row06_MarshalAllocHGlobal_ReturnsIntPtr()
        {
            const string source = @"
using System.Runtime.InteropServices;
public class C
{
    public void M()
    {
        var p = {|#0:Marshal.AllocHGlobal(16)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row07_GCHandleAlloc_CostumeStruct()
        {
            const string source = @"
using System.Runtime.InteropServices;
public class C
{
    public void M(object o)
    {
        {|#0:var|} g = {|#1:GCHandle.Alloc(o)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row08_DangerousGetHandle_OneDiagnostic()
        {
            const string source = @"
using Microsoft.Win32.SafeHandles;
public class C
{
    public void M({|#0:SafeFileHandle|} h)
    {
        var p = {|#1:h.DangerousGetHandle()|};
    }
}";
            // Parameter type + ONE diagnostic on the call (source wins over receiver; no double report).
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row20_NativeMemoryAlloc_ReturnsVoidPointer()
        {
            const string source = @"
using System.Runtime.InteropServices;
public unsafe class C
{
    public void M()
    {
        byte* p = (byte*){|#0:NativeMemory.Alloc(16)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row23_TypeHandle_ValueIsIntPtr()
        {
            // RuntimeTypeHandle is a costume: its public Value property is IntPtr. typeof(Foo) itself is clean (row 22).
            const string source = @"
public class Foo { }
public class C
{
    public void M()
    {
        var t = typeof(Foo);
        {|#0:var|} h = {|#1:typeof(Foo).TypeHandle|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row24_ProcessHandle_PropertyRead()
        {
            const string source = @"
using System.Diagnostics;
public class C
{
    public void M(Process proc)
    {
        var h = {|#0:proc.Handle|};
    }
}";
            // `var` of an IntPtr-family type is B's blind spot by design (AN0100 owns the spelled token; nint arithmetic is noise).
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        // ──────────────────────────────────────────────
        // Sinks — a member accepts an untyped native pointer, regardless of what we pass
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Row02_RandomAccessRead_SinkParameter()
        {
            const string source = @"
using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
public class C
{
    public void M({|#0:SafeFileHandle|} h, Span<byte> span)
    {
        {|#1:RandomAccess.Read(h, span, 0)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Canonical_NewSafeFileHandleFromNint_Sink()
        {
            // The thing this rule exists to prevent. No IntPtr token anywhere; (nint) is an integer.
            const string source = @"
using Microsoft.Win32.SafeHandles;
public class C
{
    public void M()
    {
        {|#0:var|} h = {|#1:new {|#2:SafeFileHandle|}((nint)0x1234, ownsHandle: false)|};
    }
}";
            // Type reference, sink (ctor takes IntPtr), and the inferred `var` — three diagnostics for one line.
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1), Helper.ExpectWarning(2) }).RunAsync();
        }

        [Fact]
        public async Task Sink_FileStreamCtorTakingSafeFileHandle()
        {
            const string source = @"
using System.IO;
using Microsoft.Win32.SafeHandles;
public class C
{
    public void M({|#0:SafeFileHandle|} h)
    {
        var fs = {|#1:new FileStream(h, FileAccess.Read)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row18_SpanCtorTakingVoidPointer_Sink()
        {
            const string source = @"
using System;
public unsafe class C
{
    public void M(byte* p)
    {
        var s = {|#0:new Span<byte>(p, 16)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row19_BufferMemoryCopy_Sink()
        {
            const string source = @"
using System;
public unsafe class C
{
    public void M(byte* src, byte* dst)
    {
        {|#0:Buffer.MemoryCopy(src, dst, 16, 16)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row09_DelegateCreation_MethodReferenceWithIntPtrParameter()
        {
            // Action<IntPtr> and Foo(IntPtr) spell the token — AN0100's job. AN0102 fires on binding Foo (sink parameter).
            const string source = @"
using System;
public class C
{
    static void Foo(IntPtr p) { }
    public void M()
    {
        Action<IntPtr> a = {|#0:Foo|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Sink_PropertySetterOfUntypedType()
        {
            const string source = @"
using Microsoft.Win32.SafeHandles;
public class Holder { public {|#0:SafeFileHandle|} Handle { get; set; } = null!; }
public class C
{
    public void M(Holder x, {|#1:SafeFileHandle|} h)
    {
        {|#2:x.Handle|} = h;
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1), Helper.ExpectWarning(2) }).RunAsync();
        }

        // ──────────────────────────────────────────────
        // Declarations — naming an untyped type
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Row03_SafeFileHandleField_ExactlyOne()
        {
            const string source = @"
using Microsoft.Win32.SafeHandles;
public class C
{
    {|#0:SafeFileHandle|} f = null!;
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row04_DerivesFromSafeHandle_BaseType()
        {
            const string source = @"
using Microsoft.Win32.SafeHandles;
public class MyHandle : {|#0:SafeHandleZeroOrMinusOneIsInvalid|}
{
    public MyHandle() : base(true) { }
    protected override bool ReleaseHandle() => true;
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0) }).RunAsync();
        }

        [Fact]
        public async Task Row12_CostumeStructFromReferenceAssembly()
        {
            // struct HWND { public IntPtr Value; } compiled in ANOTHER assembly. No IntPtr token in our code.
            const string source = @"
using ThirdParty;
public class C
{
    public void M()
    {
        {|#0:HWND|} w = {|#1:Win.GetForegroundWindow()|};
        {|#2:Win.SetForegroundWindow(w)|};
    }
}";
            var costume = await Helper.CreateCostumeReferenceAssemblyAsync();
            await Helper.CreateDiagnosticsTest(source,
                new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1), Helper.ExpectWarning(2) },
                additionalReferences: new[] { costume }).RunAsync();
        }

        // ──────────────────────────────────────────────
        // Clean — the idiom, managed objects, integers
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Row13_14_25_26_TheIdiom_Clean()
        {
            const string source = @"
using System.Collections.Generic;
using System.Runtime.InteropServices;

public unsafe struct HWND  { }
public unsafe struct HFILE { }
public unsafe struct HPCON { }
public struct OVERLAPPED { public uint Internal; }
public readonly unsafe struct HFILE_HANDLE { public readonly HFILE* Value; public HFILE_HANDLE(HFILE* v) { Value = v; } }

public unsafe class NativeMethods
{
    [DllImport(""user32.dll"")]  public static extern bool SetForegroundWindow(HWND* hWnd);
    [DllImport(""kernel32.dll"")] public static extern bool ReadFile(HFILE* hFile, byte* buffer, uint n, uint* read, OVERLAPPED* ov);
    [DllImport(""kernel32.dll"")] public static extern int  CreatePseudoConsole(uint size, HFILE* hIn, HFILE* hOut, uint flags, HPCON** phPC);

    static readonly Dictionary<int, HFILE_HANDLE> _open = new();
    static delegate* unmanaged<HWND*, int, bool> _callback;

    public static void Use()
    {
        HWND* nullHandle = (HWND*)null;
        HFILE* invalid = (HFILE*)(-1);
        SetForegroundWindow(nullHandle);
        _open[1] = new HFILE_HANDLE(invalid);
    }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "disallow").RunAsync();
        }

        [Fact]
        public async Task Row15_MemoryMarshal_Clean()
        {
            const string source = @"
using System;
using System.Runtime.InteropServices;
public unsafe class C
{
    public void M(Span<byte> span, byte* p, int n)
    {
        var u = MemoryMarshal.Cast<byte, ushort>(span);
        var s = MemoryMarshal.CreateSpan(ref *p, n);
        var r = MemoryMarshal.CreateReadOnlySpan(ref *p, n);
    }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "disallow").RunAsync();
        }

        [Fact]
        public async Task Row16_22_ManagedObjects_Clean()
        {
            const string source = @"
using System.Diagnostics;
using System.IO;
public class Foo { }
public class C
{
    public void M(string path, Process proc)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
        var bytes = File.ReadAllBytes(path);
        var t = typeof(Foo);
        var cur = Process.GetCurrentProcess();
        proc.Start();
        var id = proc.Id;
    }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "disallow").RunAsync();
        }

        [Fact]
        public async Task Row17_NativeIntegers_Clean()
        {
            // nint arithmetic, nint locals, and OUR OWN members spelled with nint are integers.
            const string source = @"
using System;
using System.Runtime.CompilerServices;
public class C
{
    static nint Twice(nint x) => x * 2;
    static nuint Size { get; set; }
    public void M(ref int x)
    {
        nint n = 5; n += 2;
        nuint u = 3;
        var t = Twice(n);
        Size = u;
        ref int y = ref Unsafe.Add(ref x, 1);
    }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "disallow").RunAsync();
        }

        [Fact]
        public async Task Row17b_BclNintParameter_IndistinguishableFromIntPtr_Fires()
        {
            // DECISION (2026-09-11): on .NET 7+ the compiler unifies nint with System.IntPtr, so in METADATA
            // Unsafe.Add(ref T, nint) and Marshal.AllocHGlobal(): IntPtr carry the very same type. A native-int-sized
            // value crossing into code we cannot see is treated as a pointer. Use the int/long overloads.
            const string source = @"
using System;
using System.Runtime.CompilerServices;
public class C
{
    public void M(ref int x, nint n)
    {
        ref int y = ref {|#0:Unsafe.Add(ref x, (nint)1)|};
        var m = {|#1:Math.Max(n, (nint)2)|};
    }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectWarning(0), Helper.ExpectWarning(1) }).RunAsync();
        }

        [Fact]
        public async Task Row21_MarshalGetLastWin32Error_Clean()
        {
            const string source = @"
using System.Runtime.InteropServices;
public class C
{
    public int M()
    {
        Marshal.SetLastPInvokeError(0);
        return Marshal.GetLastWin32Error() + Marshal.GetLastPInvokeError() + Marshal.SizeOf<int>();
    }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "disallow").RunAsync();
        }

        // ──────────────────────────────────────────────
        // Configuration
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Row27_Ignore_NoDiagnostic()
        {
            const string source = @"
using System.IO;
public class C
{
    public void M(string p) { var h = File.OpenHandle(p, FileMode.Open); }
}";
            await Helper.CreateNoDiagnosticsTest(source, enforcementLevel: "ignore").RunAsync();
        }

        [Fact]
        public async Task Row28_Disallow_Error()
        {
            const string source = @"
using System.IO;
public class C
{
    public void M(string p) { {|#0:var|} h = {|#1:File.OpenHandle(p, FileMode.Open)|}; }
}";
            await Helper.CreateDiagnosticsTest(source, new[] { Helper.ExpectError(0), Helper.ExpectError(1) }, enforcementLevel: "disallow").RunAsync();
        }

        // ──────────────────────────────────────────────
        // Row 29 — every message shows the idiom
        // ──────────────────────────────────────────────

        [Fact]
        public void Row29_MessagesShowTheIdiom()
        {
            var analyzer = new AN.CodeAnalyzers.ProhibitReachableUntypedNativePointers.ProhibitReachableUntypedNativePointersAnalyzer();
            Assert.Equal(5, analyzer.SupportedDiagnostics.Length);
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                var format = descriptor.MessageFormat.ToString();
                Assert.Equal("AN0102", descriptor.Id);
                Assert.Contains("Never IntPtr, never void*, never SafeHandle", format);
                Assert.Contains("TypeSafePInvoke.md", format);
                Assert.DoesNotContain("erase", format);
                Assert.Contains("*", format);
            }
            // Handle findings (source-handle, sink-handle, type) show the empty marker struct; pointer findings name the pointee.
            var withIdiomStruct = analyzer.SupportedDiagnostics.Count(d => d.MessageFormat.ToString().Contains("public unsafe struct"));
            Assert.Equal(3, withIdiomStruct);
        }

        [Fact]
        public async Task Row29_RenderedMessage_SpecializesMarker()
        {
            var diagnostics = await Helper.RunAndCollectAsync(@"
using System.IO;
public class C { public void M(string p) { var h = File.OpenHandle(p, FileMode.Open); } }");

            var messages = diagnostics.Select(d => d.GetMessage()).ToList();
            Assert.Contains(messages, m => m.Contains("'File.OpenHandle' returns 'SafeFileHandle'") && m.Contains("public unsafe struct HFILE { }") && m.Contains("HFILE* h;"));
            Assert.All(messages, m => Assert.Contains("Never IntPtr, never void*, never SafeHandle", m));
        }
    }
}