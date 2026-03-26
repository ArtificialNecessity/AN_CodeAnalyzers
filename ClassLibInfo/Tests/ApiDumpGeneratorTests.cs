using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AN.CodeAnalyzers.ClassLibInfo.Tests
{
    public class ApiDumpGeneratorTests : IDisposable
    {
        private readonly string _testOutputDirectory;
        private readonly string _testAssemblyDllPath;
        private readonly string _testAssemblyXmlPath;

        /// <summary>
        /// Source code for the test fixture assembly. Contains known types, methods,
        /// properties, enums, and doc comments that we verify in the output.
        /// </summary>
        private const string TestFixtureSourceCode = @"
namespace TestLib
{
    /// <summary>A simple calculator for testing.</summary>
    public class Calculator
    {
        /// <summary>Adds two integers and returns the result.</summary>
        public int Add(int leftOperand, int rightOperand) => leftOperand + rightOperand;

        /// <summary>Multiplies two doubles.</summary>
        public double Multiply(double factorA, double factorB) => factorA * factorB;

        /// <summary>Gets or sets the last result.</summary>
        public double LastResult { get; set; }

        /// <summary>The maximum precision supported.</summary>
        public const int MaxPrecision = 15;
    }

    /// <summary>Represents a color channel.</summary>
    public enum ColorChannel
    {
        /// <summary>Red channel.</summary>
        Red = 0,
        /// <summary>Green channel.</summary>
        Green = 1,
        /// <summary>Blue channel.</summary>
        Blue = 2
    }

    /// <summary>A generic container.</summary>
    public class Container<T>
    {
        /// <summary>Gets the contained value.</summary>
        public T Value { get; set; }

        /// <summary>Transforms the value using a function.</summary>
        public TResult Transform<TResult>(System.Func<T, TResult> transformFunc) => transformFunc(Value);
    }

    /// <summary>A static utility class.</summary>
    public static class StringUtils
    {
        /// <summary>Reverses a string.</summary>
        public static string Reverse(string input)
        {
            char[] charArray = input.ToCharArray();
            System.Array.Reverse(charArray);
            return new string(charArray);
        }
    }
}
";

        public ApiDumpGeneratorTests()
        {
            _testOutputDirectory = Path.Combine(Path.GetTempPath(), "ClassLibInfoTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testOutputDirectory);

            _testAssemblyDllPath = Path.Combine(_testOutputDirectory, "TestLib.dll");
            _testAssemblyXmlPath = Path.Combine(_testOutputDirectory, "TestLib.xml");

            compileTestFixtureAssembly();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testOutputDirectory))
            {
                Directory.Delete(_testOutputDirectory, recursive: true);
            }
        }

        private void compileTestFixtureAssembly()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(TestFixtureSourceCode);

            // Reference the core runtime assemblies
            var runtimeAssemblyDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var metadataReferences = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyDirectory, "System.Runtime.dll")),
            };

            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            var compilation = CSharpCompilation.Create(
                assemblyName: "TestLib",
                syntaxTrees: new[] { syntaxTree },
                references: metadataReferences,
                options: compilationOptions);

            // Emit DLL + XML doc
            using var dllStream = new FileStream(_testAssemblyDllPath, FileMode.Create);
            using var xmlStream = new FileStream(_testAssemblyXmlPath, FileMode.Create);

            var emitResult = compilation.Emit(dllStream, xmlDocumentationStream: xmlStream);
            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString());
                throw new InvalidOperationException(
                    $"Test fixture compilation failed:\n{string.Join("\n", errors)}");
            }
        }

        // ──────────────────────────────────────────────
        // HJSON format tests
        // ──────────────────────────────────────────────

        [Fact]
        public void HjsonFormat_ContainsNamespace()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("TestLib:", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsClassWithModifiers()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("Calculator:", hjsonOutput);
            Assert.Contains("kind: class", hjsonOutput);
            Assert.Contains("vis: public", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsStaticClass()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("StringUtils:", hjsonOutput);
            Assert.Contains("static: true", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsMethodWithArgs()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("Add:", hjsonOutput);
            Assert.Contains("rtn: int", hjsonOutput);
            Assert.Contains("leftOperand: int", hjsonOutput);
            Assert.Contains("rightOperand: int", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsEnum()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("ColorChannel:", hjsonOutput);
            Assert.Contains("kind: enum", hjsonOutput);
            Assert.Contains("Red: 0", hjsonOutput);
            Assert.Contains("Green: 1", hjsonOutput);
            Assert.Contains("Blue: 2", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsGenericType()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("Container<T>:", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsGenericMethod()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("Transform:", hjsonOutput);
            Assert.Contains("tparam: TResult", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsProperty()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("LastResult:", hjsonOutput);
            Assert.Contains("type: double", hjsonOutput);
            Assert.Contains("get: true", hjsonOutput);
            Assert.Contains("set: true", hjsonOutput);
        }

        [Fact]
        public void HjsonFormat_ContainsConst()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "hjson" };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("MaxPrecision:", hjsonOutput);
            Assert.Contains("type: int", hjsonOutput);
            Assert.Contains("value: 15", hjsonOutput);
        }

        // ──────────────────────────────────────────────
        // Flat format tests
        // ──────────────────────────────────────────────

        [Fact]
        public void FlatFormat_ContainsNamespace()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("namespace TestLib", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsClassDeclaration()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("class Calculator [public]", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsMethodKeyword()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("method int Add(int leftOperand, int rightOperand)", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsEnumValues()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("enum ColorChannel [public]", flatOutput);
            Assert.Contains("val Red = 0", flatOutput);
            Assert.Contains("val Green = 1", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsPropertyKeyword()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("prop double LastResult { get; set; }", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsConstKeyword()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("const int MaxPrecision = 15", flatOutput);
        }

        [Fact]
        public void FlatFormat_ContainsStaticClassModifier()
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = "public", OutputFormat = "flat" };
            string flatOutput = ApiDumpGenerator.GenerateApiDump(_testAssemblyDllPath, dumpOptions);

            Assert.Contains("class StringUtils [public, static]", flatOutput);
        }

        // ──────────────────────────────────────────────
        // XML doc comment tests
        // ──────────────────────────────────────────────

        [Fact]
        public void XmlDocReader_LoadsSidecarFile()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath);
            Assert.NotNull(docReader);
        }

        [Fact]
        public void XmlDocReader_FindsTypeSummary()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? calculatorSummary = docReader.GetSummary("T:TestLib.Calculator");
            Assert.NotNull(calculatorSummary);
            Assert.Contains("simple calculator", calculatorSummary);
        }

        [Fact]
        public void XmlDocReader_FindsMethodSummary()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? addMethodSummary = docReader.GetSummary("M:TestLib.Calculator.Add(System.Int32,System.Int32)");
            Assert.NotNull(addMethodSummary);
            Assert.Contains("Adds two integers", addMethodSummary);
        }

        [Fact]
        public void XmlDocReader_FindsPropertySummary()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? lastResultSummary = docReader.GetSummary("P:TestLib.Calculator.LastResult");
            Assert.NotNull(lastResultSummary);
            Assert.Contains("last result", lastResultSummary);
        }

        [Fact]
        public void XmlDocReader_FindsEnumSummary()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? colorChannelSummary = docReader.GetSummary("T:TestLib.ColorChannel");
            Assert.NotNull(colorChannelSummary);
            Assert.Contains("color channel", colorChannelSummary);
        }

        [Fact]
        public void XmlDocReader_BriefTruncatesLongSummary()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? briefSummary = docReader.GetBriefSummary("T:TestLib.Calculator", maxBriefLength: 10);
            Assert.NotNull(briefSummary);
            Assert.True(briefSummary.Length <= 15); // 10 + "..." + some word boundary slack
            Assert.EndsWith("...", briefSummary);
        }

        [Fact]
        public void XmlDocReader_ReturnsNullForMissingMember()
        {
            var docReader = XmlDocCommentReader.TryLoadForAssembly(_testAssemblyDllPath)!;
            string? nonexistentSummary = docReader.GetSummary("T:TestLib.NonExistentType");
            Assert.Null(nonexistentSummary);
        }
    }
}