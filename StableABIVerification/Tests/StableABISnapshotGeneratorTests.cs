using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using AN.CodeAnalyzers.StableABIVerification;

namespace AN.CodeAnalyzers.Tests.StableABIVerification
{
    public class StableABISnapshotGeneratorTests : IDisposable
    {
        private readonly string testOutputDirectory;

        public StableABISnapshotGeneratorTests()
        {
            testOutputDirectory = Path.Combine(Path.GetTempPath(), "AN_StableABI_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testOutputDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(testOutputDirectory)) {
                Directory.Delete(testOutputDirectory, recursive: true);
            }
        }

        // ──────────────────────────────────────────────
        // Enum snapshot generation
        // ──────────────────────────────────────────────

        [Fact]
        public void Enum_PublicWithExplicitValues_GeneratesCorrectSnapshot()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public enum PixelFormat
{
    R8UNorm = 1,
    R16UNorm = 2,
    R8G8B8A8UNorm = 37
}", "public");

            Assert.Contains("enum.PixelFormat._type: int", snapshotContent);
            Assert.Contains("enum.PixelFormat.R8UNorm: 1", snapshotContent);
            Assert.Contains("enum.PixelFormat.R16UNorm: 2", snapshotContent);
            Assert.Contains("enum.PixelFormat.R8G8B8A8UNorm: 37", snapshotContent);
        }

        [Fact]
        public void Enum_WithFlagsAttribute_IncludesFlagsMetadata()
        {
            string snapshotContent = generateSnapshotFromSource(@"
using System;

[Flags]
public enum BufferUsage
{
    VertexBuffer = 1,
    IndexBuffer = 2,
    UniformBuffer = 4
}", "public");

            Assert.Contains("enum.BufferUsage._flags: true", snapshotContent);
            Assert.Contains("enum.BufferUsage._type: int", snapshotContent);
            Assert.Contains("enum.BufferUsage.VertexBuffer: 1", snapshotContent);
        }

        [Fact]
        public void Enum_Internal_ExcludedFromPublicScope()
        {
            string snapshotContent = generateSnapshotFromSource(@"
internal enum InternalFormat
{
    A = 1,
    B = 2
}", "public");

            Assert.DoesNotContain("enum.InternalFormat", snapshotContent);
        }

        [Fact]
        public void Enum_Internal_IncludedInAllScope()
        {
            string snapshotContent = generateSnapshotFromSource(@"
internal enum InternalFormat
{
    A = 1,
    B = 2
}", "all");

            Assert.Contains("enum.InternalFormat._type: int", snapshotContent);
            Assert.Contains("enum.InternalFormat.A: 1", snapshotContent);
        }

        [Fact]
        public void NestedPublicEnumInsideInternalClass_ExcludedFromPublicScope()
        {
            string snapshotContent = generateSnapshotFromSource(@"
internal class InternalContainer
{
    public enum PublicNestedStatus
    {
        Active = 1,
        Inactive = 2
    }

    public const int PublicNestedConst = 42;
    public static readonly int PublicNestedField = 99;
}", "public");

            // Nothing from InternalContainer should appear — it's internal,
            // so its public members are NOT visible to assembly consumers
            Assert.DoesNotContain("InternalContainer", snapshotContent);
            Assert.DoesNotContain("PublicNestedStatus", snapshotContent);
            Assert.DoesNotContain("PublicNestedConst", snapshotContent);
            Assert.DoesNotContain("PublicNestedField", snapshotContent);
        }

        [Fact]
        public void NestedPublicEnumInsidePublicClass_IncludedInPublicScope()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public class PublicContainer
{
    public enum NestedStatus
    {
        Active = 1,
        Inactive = 2
    }
}", "public");

            // Nested public enum inside public class IS visible to consumers
            Assert.Contains("enum.PublicContainer.NestedStatus._type: int", snapshotContent);
            Assert.Contains("enum.PublicContainer.NestedStatus.Active: 1", snapshotContent);
            Assert.Contains("enum.PublicContainer.NestedStatus.Inactive: 2", snapshotContent);
        }

        // ──────────────────────────────────────────────
        // Const snapshot generation
        // ──────────────────────────────────────────────

        [Fact]
        public void Const_PublicIntField_GeneratesCorrectSnapshot()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public class MyClass
{
    public const int MaxRetries = 3;
}", "public");

            Assert.Contains("const.MyClass.MaxRetries: int 3", snapshotContent);
        }

        [Fact]
        public void Const_PublicStringField_GeneratesQuotedValue()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public class MyClass
{
    public const string Version = ""2.0"";
}", "public");

            Assert.Contains("const.MyClass.Version: string \"2.0\"", snapshotContent);
        }

        // ──────────────────────────────────────────────
        // Default parameter snapshot generation
        // ──────────────────────────────────────────────

        [Fact]
        public void DefaultParam_PublicMethod_GeneratesCorrectSnapshot()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public class MyClass
{
    public void Connect(int retries = 3, int timeout = 30) { }
}", "public");

            Assert.Contains("default.MyClass.Connect.retries: int 3", snapshotContent);
            Assert.Contains("default.MyClass.Connect.timeout: int 30", snapshotContent);
        }

        // ──────────────────────────────────────────────
        // Struct snapshot generation
        // ──────────────────────────────────────────────

        [Fact]
        public void Struct_PublicWithFields_GeneratesLayoutAndFields()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public struct VertexPosition
{
    public float X;
    public float Y;
    public float Z;
}", "public");

            Assert.Contains("struct.VertexPosition._layout: Sequential", snapshotContent);
            Assert.Contains("struct.VertexPosition._pack: 0", snapshotContent);
            Assert.Contains("struct.VertexPosition._size: 0", snapshotContent);
            Assert.Contains("struct.VertexPosition.field0.X: float", snapshotContent);
            Assert.Contains("struct.VertexPosition.field1.Y: float", snapshotContent);
            Assert.Contains("struct.VertexPosition.field2.Z: float", snapshotContent);
        }

        // ──────────────────────────────────────────────
        // Sorting verification
        // ──────────────────────────────────────────────

        [Fact]
        public void Snapshot_IsSortedAlphabetically()
        {
            string snapshotContent = generateSnapshotFromSource(@"
public enum Zebra { Z = 1 }
public enum Alpha { A = 1 }
public class Middle { public const int Value = 5; }
", "public");

            string[] snapshotLines = snapshotContent.Split('\n',
                StringSplitOptions.RemoveEmptyEntries);

            for (int lineIndex = 1; lineIndex < snapshotLines.Length; lineIndex++) {
                Assert.True(
                    string.Compare(snapshotLines[lineIndex - 1], snapshotLines[lineIndex], StringComparison.Ordinal) <= 0,
                    $"Lines not sorted: '{snapshotLines[lineIndex - 1]}' should come before '{snapshotLines[lineIndex]}'");
            }
        }

        [Fact]
        public void Snapshot_EmptyCompilation_ReturnsEmptyString()
        {
            string snapshotContent = generateSnapshotFromSource(@"
internal class Hidden { }
", "public");

            Assert.Equal("", snapshotContent);
        }

        // ──────────────────────────────────────────────
        // Helper: compile source to DLL, then generate snapshot
        // ──────────────────────────────────────────────

        private string generateSnapshotFromSource(string sourceCode, string snapshotScope)
        {
            string assemblyFileName = "TestAssembly_" + Guid.NewGuid().ToString("N") + ".dll";
            string assemblyFilePath = Path.Combine(testOutputDirectory, assemblyFileName);

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            // Get the core library reference from the runtime
            string coreLibPath = typeof(object).Assembly.Location;
            string runtimeDirectory = Path.GetDirectoryName(coreLibPath)!;

            var references = new MetadataReference[]
            {
                MetadataReference.CreateFromFile(coreLibPath),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            };

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var emitResult = compilation.Emit(assemblyFilePath);
            Assert.True(emitResult.Success,
                "Test compilation failed: " + string.Join("\n",
                    emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            return StableABISnapshotGenerator.GenerateSnapshot(assemblyFilePath, snapshotScope);
        }
    }
}