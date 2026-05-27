using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
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

            var snapshotLines = StableABISnapshotGenerator.GenerateSnapshotLines(assemblyFilePath, snapshotScope);
            if (snapshotLines.Count == 0) {
                return "";
            }
            return string.Join("\n", snapshotLines) + "\n";
        }
    }

    // ──────────────────────────────────────────────
    // Line ending detection and generation tests
    // ──────────────────────────────────────────────

    public class StableABILineEndingTests : IDisposable
    {
        private readonly string testOutputDirectory;

        public StableABILineEndingTests()
        {
            testOutputDirectory = Path.Combine(Path.GetTempPath(), "AN_StableABI_LineEnding_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testOutputDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(testOutputDirectory)) {
                Directory.Delete(testOutputDirectory, recursive: true);
            }
        }

        [Fact]
        public void DetectLineEnding_CRLFContent_ReturnsCRLF()
        {
            string crlfContent = "line1\r\nline2\r\nline3\r\n";
            var detectedEnding = StableABIVerifyTask.detectLineEndingFromContent(crlfContent);
            Assert.Equal(StableABIVerifyTask.LineEndingMode.CRLF, detectedEnding);
        }

        [Fact]
        public void DetectLineEnding_LFContent_ReturnsLF()
        {
            string lfContent = "line1\nline2\nline3\n";
            var detectedEnding = StableABIVerifyTask.detectLineEndingFromContent(lfContent);
            Assert.Equal(StableABIVerifyTask.LineEndingMode.LF, detectedEnding);
        }

        [Fact]
        public void DetectLineEnding_NoNewlines_ReturnsNull()
        {
            string noNewlineContent = "single line no ending";
            var detectedEnding = StableABIVerifyTask.detectLineEndingFromContent(noNewlineContent);
            Assert.Equal(StableABIVerifyTask.LineEndingMode.Unknown, detectedEnding);
        }

        [Fact]
        public void GenerateMode_ExistingLFFile_PreservesLF()
        {
            // Arrange: write seed file with exact LF bytes (File.WriteAllBytes avoids any conversion)
            string snapshotFilePath = Path.Combine(testOutputDirectory, "test_lf.stableapi");
            byte[] lfSeedBytes = System.Text.Encoding.UTF8.GetBytes("__stableApiVersion: 2\nenum.Foo.A: 1\n");
            File.WriteAllBytes(snapshotFilePath, lfSeedBytes);

            // Verify the seed file actually has LF on disk (defensive)
            byte[] seedBytesOnDisk = File.ReadAllBytes(snapshotFilePath);
            string seedContentOnDisk = System.Text.Encoding.UTF8.GetString(seedBytesOnDisk);
            Assert.Contains("\n", seedContentOnDisk);
            Assert.DoesNotContain("\r", seedContentOnDisk);

            // Compile a test assembly
            string assemblyFilePath = compileTestAssembly(@"
public enum Foo { A = 1, B = 2 }
");

            var fakeBuildEngine = new FakeBuildEngine();
            var generateTask = new StableABIVerifyTask {
                AssemblyPath = assemblyFilePath,
                SnapshotPath = snapshotFilePath,
                Scope = "public",
                GenerateMode = true,
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = generateTask.Execute();

            // Assert: read back with ReadAllBytes to see exact bytes
            Assert.True(taskSucceeded);
            byte[] outputBytes = File.ReadAllBytes(snapshotFilePath);
            string outputContent = System.Text.Encoding.UTF8.GetString(outputBytes);
            // Must contain LF but NOT CRLF
            Assert.Contains("\n", outputContent);
            Assert.DoesNotContain("\r", outputContent);
        }

        [Fact]
        public void GenerateMode_ExistingCRLFFile_PreservesCRLF()
        {
            // Arrange: write seed file with exact CRLF bytes (File.WriteAllBytes avoids any conversion)
            string snapshotFilePath = Path.Combine(testOutputDirectory, "test_crlf.stableapi");
            byte[] crlfSeedBytes = System.Text.Encoding.UTF8.GetBytes("__stableApiVersion: 2\r\nenum.Foo.A: 1\r\n");
            File.WriteAllBytes(snapshotFilePath, crlfSeedBytes);

            // Verify the seed file actually has CRLF on disk (defensive)
            byte[] seedBytesOnDisk = File.ReadAllBytes(snapshotFilePath);
            string seedContentOnDisk = System.Text.Encoding.UTF8.GetString(seedBytesOnDisk);
            Assert.Contains("\r\n", seedContentOnDisk);

            // Compile a test assembly
            string assemblyFilePath = compileTestAssembly(@"
public enum Foo { A = 1, B = 2 }
");

            var fakeBuildEngine = new FakeBuildEngine();
            var generateTask = new StableABIVerifyTask {
                AssemblyPath = assemblyFilePath,
                SnapshotPath = snapshotFilePath,
                Scope = "public",
                GenerateMode = true,
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = generateTask.Execute();

            // Assert: read back with ReadAllBytes to see exact bytes
            Assert.True(taskSucceeded);
            byte[] outputBytes = File.ReadAllBytes(snapshotFilePath);
            string outputContent = System.Text.Encoding.UTF8.GetString(outputBytes);
            // Must contain CRLF
            Assert.Contains("\r\n", outputContent);
            // Every \n must be preceded by \r (no bare LF)
            for (int charIndex = 0; charIndex < outputContent.Length; charIndex++) {
                if (outputContent[charIndex] == '\n') {
                    Assert.True(charIndex > 0 && outputContent[charIndex - 1] == '\r',
                        $"Found bare LF at position {charIndex} — expected CRLF everywhere");
                }
            }
        }

        // ──────────────────────────────────────────────
        // Helper: compile source to DLL
        // ──────────────────────────────────────────────

        private string compileTestAssembly(string sourceCode)
        {
            string assemblyFileName = "TestAssembly_" + Guid.NewGuid().ToString("N") + ".dll";
            string assemblyFilePath = Path.Combine(testOutputDirectory, assemblyFileName);

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
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
            if (!emitResult.Success) {
                throw new Exception("Test compilation failed: " + string.Join("\n",
                    emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }

            return assemblyFilePath;
        }
    }

    /// <summary>
    /// Fake IBuildEngine for testing MSBuild tasks.
    /// </summary>
    internal class FakeBuildEngine : IBuildEngine
    {
        public System.Collections.Generic.List<string> LoggedErrors { get; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> LoggedWarnings { get; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> LoggedMessages { get; } = new System.Collections.Generic.List<string>();

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
        {
            throw new NotImplementedException();
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
            LoggedMessages.Add(e.Message ?? "");
        }

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            LoggedErrors.Add(e.Message ?? "");
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
            LoggedMessages.Add(e.Message ?? "");
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            LoggedWarnings.Add(e.Message ?? "");
        }
    }
}