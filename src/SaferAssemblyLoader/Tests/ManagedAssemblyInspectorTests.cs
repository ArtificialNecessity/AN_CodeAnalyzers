using System.Linq;
using Xunit;

namespace ArtificialNecessity.SaferAssemblyLoader.Tests
{
    public class ManagedAssemblyInspectorTests
    {
        // ──────────────────────────────────────────────
        // Clean assembly — should pass with zero violations
        // ──────────────────────────────────────────────

        [Fact]
        public void CleanAssembly_ReturnsNoViolations()
        {
            byte[] cleanDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.CleanManagedSource, assemblyName: "CleanLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(cleanDllBytes);

            Assert.True(inspectionResult.IsManagedOnly,
                $"Expected clean assembly to pass, but got violations:\n  {string.Join("\n  ", inspectionResult.Violations)}");
            Assert.Empty(inspectionResult.Violations);
        }

        // ──────────────────────────────────────────────
        // DllImport detection
        // ──────────────────────────────────────────────

        [Fact]
        public void DllImport_DetectsPInvokeMethods()
        {
            byte[] dllImportDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.DllImportSource, assemblyName: "DllImportLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(dllImportDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);
            Assert.Contains(inspectionResult.Violations,
                violation => violation.StartsWith("[DllImport]") && violation.Contains("GetCurrentProcessId"));
            Assert.Contains(inspectionResult.Violations,
                violation => violation.StartsWith("[DllImport]") && violation.Contains("CloseHandle"));
            Assert.Contains(inspectionResult.Violations,
                violation => violation.Contains("kernel32.dll"));
        }

        // ──────────────────────────────────────────────
        // IntPtr field detection
        // ──────────────────────────────────────────────

        [Fact]
        public void IntPtrFields_DetectsIntPtrAndUIntPtrFields()
        {
            byte[] intPtrFieldDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.IntPtrFieldSource, assemblyName: "IntPtrFieldLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(intPtrFieldDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);

            var intPtrFieldViolations = inspectionResult.Violations
                .Where(violation => violation.StartsWith("[IntPtr field]"))
                .ToList();

            Assert.Contains(intPtrFieldViolations,
                violation => violation.Contains("NativeHandle"));
            Assert.Contains(intPtrFieldViolations,
                violation => violation.Contains("BufferSize"));
            Assert.Contains(intPtrFieldViolations,
                violation => violation.Contains("_internalPointer"));
        }

        // ──────────────────────────────────────────────
        // IntPtr in method signatures
        // ──────────────────────────────────────────────

        [Fact]
        public void IntPtrSignatures_DetectsIntPtrInMethodSignatures()
        {
            byte[] intPtrSigDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.IntPtrSignatureSource, assemblyName: "IntPtrSigLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(intPtrSigDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);

            var signatureViolations = inspectionResult.Violations
                .Where(violation => violation.StartsWith("[IntPtr signature]"))
                .ToList();

            Assert.Contains(signatureViolations,
                violation => violation.Contains("Allocate"));
            Assert.Contains(signatureViolations,
                violation => violation.Contains("Free"));
            Assert.Contains(signatureViolations,
                violation => violation.Contains("GetSize"));
        }

        // ──────────────────────────────────────────────
        // Marshal.* call detection
        // ──────────────────────────────────────────────

        [Fact]
        public void MarshalCalls_DetectsMarshalMethodReferences()
        {
            byte[] marshalDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.MarshalCallsSource, assemblyName: "MarshalLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(marshalDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);

            var marshalViolations = inspectionResult.Violations
                .Where(violation => violation.StartsWith("[Marshal call]"))
                .ToList();

            Assert.Contains(marshalViolations,
                violation => violation.Contains("AllocHGlobal"));
            Assert.Contains(marshalViolations,
                violation => violation.Contains("FreeHGlobal"));
        }

        // ──────────────────────────────────────────────
        // Unsafe IL detection
        // ──────────────────────────────────────────────

        [Fact]
        public void UnsafeCode_DetectsPointerOperations()
        {
            byte[] unsafeDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.UnsafeCodeSource, allowUnsafe: true, assemblyName: "UnsafeLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(unsafeDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);

            var unsafeILViolations = inspectionResult.Violations
                .Where(violation => violation.StartsWith("[unsafe IL]"))
                .ToList();

            // Should detect at least one of the unsafe methods
            Assert.NotEmpty(unsafeILViolations);
        }

        // ──────────────────────────────────────────────
        // Multiple violation types in one assembly
        // ──────────────────────────────────────────────

        [Fact]
        public void MultipleViolationTypes_AllDetected()
        {
            // Assembly with both DllImport and IntPtr fields
            const string multiViolationSource = @"
using System;
using System.Runtime.InteropServices;

namespace TestLib
{
    public class MixedBag
    {
        public IntPtr NativeHandle;

        [DllImport(""user32.dll"")]
        public static extern int GetSystemMetrics(int metricIndex);

        public void UseMarshal()
        {
            IntPtr allocatedBlock = Marshal.AllocHGlobal(64);
            Marshal.FreeHGlobal(allocatedBlock);
        }
    }
}
";
            byte[] multiViolationDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                multiViolationSource, assemblyName: "MultiViolationLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(multiViolationDllBytes);

            Assert.False(inspectionResult.IsManagedOnly);

            // Should have DllImport, IntPtr field, Marshal call, and IntPtr signature violations
            Assert.Contains(inspectionResult.Violations, v => v.StartsWith("[DllImport]"));
            Assert.Contains(inspectionResult.Violations, v => v.StartsWith("[IntPtr field]"));
            Assert.Contains(inspectionResult.Violations, v => v.StartsWith("[Marshal call]"));
        }

        // ──────────────────────────────────────────────
        // Violation message format
        // ──────────────────────────────────────────────

        [Fact]
        public void ViolationMessages_ContainTypeAndMethodNames()
        {
            byte[] dllImportDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.DllImportSource, assemblyName: "DllImportFormatLib");

            var inspectionResult = ManagedAssemblyInspector.Inspect(dllImportDllBytes);

            // Verify the violation message includes the full type name
            Assert.Contains(inspectionResult.Violations,
                violation => violation.Contains("TestLib.NativeMethods"));
        }
    }
}