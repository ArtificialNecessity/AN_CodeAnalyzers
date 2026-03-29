using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArtificialNecessity.SaferAssemblyLoader.Tests
{
    /// <summary>
    /// Compiles C# source code into DLL bytes for testing the assembly inspector.
    /// Each method produces a different kind of test fixture assembly.
    /// </summary>
    internal static class TestAssemblyCompiler
    {
        /// <summary>
        /// Compile C# source to a DLL byte array.
        /// </summary>
        public static byte[] CompileToDllBytes(string sourceCode, bool allowUnsafe = false, string assemblyName = "TestAssembly")
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            var runtimeAssemblyDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var metadataReferences = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyDirectory, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyDirectory, "System.Runtime.InteropServices.dll")),
            };

            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe);

            var compilation = CSharpCompilation.Create(
                assemblyName: assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: metadataReferences,
                options: compilationOptions);

            using var dllOutputStream = new MemoryStream();
            var emitResult = compilation.Emit(dllOutputStream);

            if (!emitResult.Success)
            {
                var compilationErrors = emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString());
                throw new InvalidOperationException(
                    $"Test fixture compilation failed for '{assemblyName}':\n{string.Join("\n", compilationErrors)}");
            }

            return dllOutputStream.ToArray();
        }

        /// <summary>
        /// Compile C# source to a DLL file on disk. Returns the file path.
        /// Caller is responsible for cleanup.
        /// </summary>
        public static string CompileToDllFile(string sourceCode, string outputDirectory, bool allowUnsafe = false, string assemblyName = "TestAssembly")
        {
            byte[] dllBytes = CompileToDllBytes(sourceCode, allowUnsafe, assemblyName);
            string dllFilePath = Path.Combine(outputDirectory, assemblyName + ".dll");
            File.WriteAllBytes(dllFilePath, dllBytes);
            return dllFilePath;
        }

        // ──────────────────────────────────────────────
        // Pre-built test fixture source code
        // ──────────────────────────────────────────────

        /// <summary>Clean managed-only assembly — no native code surface at all.</summary>
        public const string CleanManagedSource = @"
namespace TestLib
{
    public class Calculator
    {
        public int Add(int leftOperand, int rightOperand) => leftOperand + rightOperand;
        public string Greet(string name) => ""Hello, "" + name;
        public double LastResult { get; set; }
    }

    public enum Color { Red = 0, Green = 1, Blue = 2 }
}
";

        /// <summary>Assembly with DllImport P/Invoke methods.</summary>
        public const string DllImportSource = @"
using System.Runtime.InteropServices;

namespace TestLib
{
    public static class NativeMethods
    {
        [DllImport(""kernel32.dll"")]
        public static extern int GetCurrentProcessId();

        [DllImport(""kernel32.dll"", SetLastError = true)]
        public static extern bool CloseHandle(System.IntPtr objectHandle);
    }
}
";

        /// <summary>Assembly with IntPtr fields.</summary>
        public const string IntPtrFieldSource = @"
using System;

namespace TestLib
{
    public class ResourceHolder
    {
        public IntPtr NativeHandle;
        public UIntPtr BufferSize;
        private IntPtr _internalPointer;
    }
}
";

        /// <summary>Assembly with IntPtr in method signatures.</summary>
        public const string IntPtrSignatureSource = @"
using System;

namespace TestLib
{
    public class Interop
    {
        public IntPtr Allocate(int byteCount) => IntPtr.Zero;
        public void Free(IntPtr memoryPointer) { }
        public UIntPtr GetSize() => UIntPtr.Zero;
    }
}
";

        /// <summary>Assembly with Marshal.* calls.</summary>
        public const string MarshalCallsSource = @"
using System;
using System.Runtime.InteropServices;

namespace TestLib
{
    public class MarshalUser
    {
        public void DoMarshalStuff()
        {
            IntPtr allocatedMemory = Marshal.AllocHGlobal(100);
            Marshal.FreeHGlobal(allocatedMemory);
        }
    }
}
";

        /// <summary>Assembly with unsafe code (pointer operations).</summary>
        public const string UnsafeCodeSource = @"
namespace TestLib
{
    public class UnsafeOperations
    {
        public unsafe int ReadFromPointer(int* sourcePointer)
        {
            return *sourcePointer;
        }

        public unsafe void WriteToPointer(int* targetPointer, int value)
        {
            *targetPointer = value;
        }
    }
}
";
    }
}