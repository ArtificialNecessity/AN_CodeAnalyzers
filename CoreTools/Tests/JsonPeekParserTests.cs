using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Microsoft.Build.Framework;

namespace AN.CodeAnalyzers.Tests
{
    public class JsonPeekParserTests : IDisposable
    {
        private readonly string testTempDirectory;

        public JsonPeekParserTests()
        {
            testTempDirectory = Path.Combine(Path.GetTempPath(), "JsonPeek_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testTempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(testTempDirectory)) {
                Directory.Delete(testTempDirectory, recursive: true);
            }
        }

        private string writeTestFile(string filename, string content)
        {
            string filePath = Path.Combine(testTempDirectory, filename);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        // ═══════════════════════════════════════════════════════════
        // Core parser tests - ReadValueFromString
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SimpleJsonKey_ReturnsValue()
        {
            string extractedValue = JsonPeekParser.ReadValueFromString(
                @"{ ""version"": ""1.0"" }",
                "version");

            Assert.Equal("1.0", extractedValue);
        }

        [Fact]
        public void HjsonUnquotedKey_ReturnsValue()
        {
            // HJSON allows unquoted keys and values.
            // Unquoted numeric values are parsed as numbers, so 1.0 becomes double 1.0 → "1".
            // To preserve "1.0" as a string, quote it in the source: version: "1.0"
            string extractedNumericValue = JsonPeekParser.ReadValueFromString(
                "{ version: 1.0 }",
                "version");
            Assert.Equal("1", extractedNumericValue);

            // Quoted string values preserve the exact text
            string extractedQuotedValue = JsonPeekParser.ReadValueFromString(
                "{ version: \"1.0\" }",
                "version");
            Assert.Equal("1.0", extractedQuotedValue);
        }

        [Fact]
        public void JsoncWithComments_ReturnsValue()
        {
            string jsoncContent = @"
{
    // This is a comment
    ""version"": ""2.5.0"",
    /* block comment */
    ""name"": ""test""
}";
            string extractedValue = JsonPeekParser.ReadValueFromString(jsoncContent, "version");
            Assert.Equal("2.5.0", extractedValue);
        }

        [Fact]
        public void NestedDotSeparatedKeyPath_ReturnsValue()
        {
            string jsonContent = @"
{
    ""parent"": {
        ""child"": {
            ""value"": ""deep""
        }
    }
}";
            string extractedValue = JsonPeekParser.ReadValueFromString(jsonContent, "parent.child.value");
            Assert.Equal("deep", extractedValue);
        }

        [Fact]
        public void NumericValue_ReturnsStringRepresentation()
        {
            string extractedValue = JsonPeekParser.ReadValueFromString(
                @"{ ""count"": 42 }",
                "count");

            Assert.Equal("42", extractedValue);
        }

        [Fact]
        public void BooleanValue_ReturnsStringRepresentation()
        {
            string extractedValue = JsonPeekParser.ReadValueFromString(
                @"{ ""enabled"": true }",
                "enabled");

            Assert.Equal("true", extractedValue);
        }

        [Fact]
        public void MissingKey_ThrowsKeyNotFoundException()
        {
            var thrownException = Assert.Throws<KeyNotFoundException>(() =>
                JsonPeekParser.ReadValueFromString(
                    @"{ ""version"": ""1.0"" }",
                    "nonexistent"));

            Assert.Contains("nonexistent", thrownException.Message);
            Assert.Contains("Available keys", thrownException.Message);
        }

        [Fact]
        public void NavigateIntoNonObject_ThrowsInvalidOperationException()
        {
            var thrownException = Assert.Throws<InvalidOperationException>(() =>
                JsonPeekParser.ReadValueFromString(
                    @"{ ""version"": ""1.0"" }",
                    "version.sub"));

            Assert.Contains("Cannot navigate", thrownException.Message);
        }

        [Fact]
        public void EmptyKeyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                JsonPeekParser.ReadValueFromString(
                    @"{ ""version"": ""1.0"" }",
                    ""));
        }

        // ═══════════════════════════════════════════════════════════
        // File-based tests - ReadValueFromFile
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ReadValueFromFile_JsonFile_ReturnsValue()
        {
            string testFilePath = writeTestFile("test.json", @"{ ""version"": ""3.0"" }");

            string extractedValue = JsonPeekParser.ReadValueFromFile(testFilePath, "version");

            Assert.Equal("3.0", extractedValue);
        }

        [Fact]
        public void ReadValueFromFile_HjsonFile_ReturnsValue()
        {
            string hjsonContent = @"
{
    # HJSON comment
    version: 4.2.1
    name: my-package
}";
            string testFilePath = writeTestFile("test.hjson", hjsonContent);

            string extractedValue = JsonPeekParser.ReadValueFromFile(testFilePath, "version");

            Assert.Equal("4.2.1", extractedValue);
        }

        [Fact]
        public void ReadValueFromFile_AnyExtension_ReturnsValue()
        {
            // Extension doesn't matter - parser handles all formats
            string testFilePath = writeTestFile("config.txt", @"{ ""key"": ""value"" }");

            string extractedValue = JsonPeekParser.ReadValueFromFile(testFilePath, "key");

            Assert.Equal("value", extractedValue);
        }

        // ═══════════════════════════════════════════════════════════
        // MSBuild task tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void JsonPeekTask_ValidFile_SetsOutputValue()
        {
            string testFilePath = writeTestFile("task-test.json", @"{ ""version"": ""5.0"" }");

            var fakeBuildEngine = new FakeBuildEngine();
            var msbuildTask = new JsonPeek {
                File = testFilePath,
                KeyPath = "version",
                BuildEngine = fakeBuildEngine
            };

            bool taskSucceeded = msbuildTask.Execute();

            Assert.True(taskSucceeded);
            Assert.Equal("5.0", msbuildTask.Value);
            Assert.Empty(fakeBuildEngine.LoggedErrors);
        }

        [Fact]
        public void JsonPeekTask_MissingFile_LogsError()
        {
            var fakeBuildEngine = new FakeBuildEngine();
            var msbuildTask = new JsonPeek {
                File = Path.Combine(testTempDirectory, "nonexistent.json"),
                KeyPath = "version",
                BuildEngine = fakeBuildEngine
            };

            bool taskSucceeded = msbuildTask.Execute();

            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);
            Assert.Contains("File not found", fakeBuildEngine.LoggedErrors[0]);
        }

        [Fact]
        public void JsonPeekTask_MissingKey_LogsError()
        {
            string testFilePath = writeTestFile("task-missing-key.json", @"{ ""version"": ""1.0"" }");

            var fakeBuildEngine = new FakeBuildEngine();
            var msbuildTask = new JsonPeek {
                File = testFilePath,
                KeyPath = "nonexistent",
                BuildEngine = fakeBuildEngine
            };

            bool taskSucceeded = msbuildTask.Execute();

            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);
            Assert.Contains("nonexistent", fakeBuildEngine.LoggedErrors[0]);
        }

        // ═══════════════════════════════════════════════════════════
        // Write value tests - WriteValueToString and WriteValueToFile
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void WriteValueToString_UnquotedInteger_PreservesUnquotedStyle()
        {
            string originalJson = @"{ ""count"": 42 }";
            string modifiedJson = JsonPeekParser.WriteValueToString(originalJson, "count", "99");
            
            Assert.Contains("\"count\": 99", modifiedJson);
            Assert.DoesNotContain("\"99\"", modifiedJson); // Should NOT be quoted
        }

        [Fact]
        public void WriteValueToString_QuotedString_PreservesQuotedStyle()
        {
            string originalJson = @"{ ""version"": ""1.0"" }";
            string modifiedJson = JsonPeekParser.WriteValueToString(originalJson, "version", "2.0");
            
            Assert.Contains("\"version\": \"2.0\"", modifiedJson);
        }

        [Fact]
        public void WriteValueToString_PreservesComments()
        {
            string originalJson = @"
{
  // This is a comment
  ""version"": ""1.0"",
  ""count"": 5
}";
            string modifiedJson = JsonPeekParser.WriteValueToString(originalJson, "version", "2.0");
            
            Assert.Contains("// This is a comment", modifiedJson);
            Assert.Contains("\"version\": \"2.0\"", modifiedJson);
        }

        [Fact]
        public void WriteValueToString_HjsonUnquotedKey_Works()
        {
            string originalHjson = "{ version: 1.0 }";
            string modifiedHjson = JsonPeekParser.WriteValueToString(originalHjson, "version", "2.0");
            
            Assert.Contains("version: 2.0", modifiedHjson);
        }

        [Fact]
        public void WriteValueToFile_ModifiesFileAndPreservesComments()
        {
            string originalContent = @"
{
  // Version comment
  ""version"": ""1.0"",
  ""buildNumberOffset"": 5
}";
            string testFilePath = writeTestFile("write-test.json", originalContent);
            
            JsonPeekParser.WriteValueToFile(testFilePath, "buildNumberOffset", "10");
            
            string modifiedContent = File.ReadAllText(testFilePath);
            Assert.Contains("// Version comment", modifiedContent);
            Assert.Contains("\"buildNumberOffset\": 10", modifiedContent);
            Assert.Contains("\"version\": \"1.0\"", modifiedContent); // Other values unchanged
        }

        // ═══════════════════════════════════════════════════════════
        // Increment integer tests - IncrementIntegerInFile
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IncrementIntegerInFile_IncrementsBy1_ReturnsNewValue()
        {
            string testFilePath = writeTestFile("increment-test.json", @"{ ""count"": 5 }");
            
            int newValue = JsonPeekParser.IncrementIntegerInFile(testFilePath, "count");
            
            Assert.Equal(6, newValue);
            string modifiedContent = File.ReadAllText(testFilePath);
            Assert.Contains("\"count\": 6", modifiedContent);
        }

        [Fact]
        public void IncrementIntegerInFile_CustomAmount_IncrementsCorrectly()
        {
            string testFilePath = writeTestFile("increment-custom.json", @"{ ""buildNumber"": 10 }");
            
            int newValue = JsonPeekParser.IncrementIntegerInFile(testFilePath, "buildNumber", 5);
            
            Assert.Equal(15, newValue);
            string modifiedContent = File.ReadAllText(testFilePath);
            Assert.Contains("\"buildNumber\": 15", modifiedContent);
        }

        [Fact]
        public void IncrementIntegerInFile_PreservesComments()
        {
            string originalContent = @"
{
  // Build number comment
  ""buildNumberOffset"": 3,
  // Other comment
  ""version"": ""0.1""
}";
            string testFilePath = writeTestFile("increment-comments.json", originalContent);
            
            int newValue = JsonPeekParser.IncrementIntegerInFile(testFilePath, "buildNumberOffset");
            
            Assert.Equal(4, newValue);
            string modifiedContent = File.ReadAllText(testFilePath);
            Assert.Contains("// Build number comment", modifiedContent);
            Assert.Contains("// Other comment", modifiedContent);
            Assert.Contains("\"buildNumberOffset\": 4", modifiedContent);
            Assert.Contains("\"version\": \"0.1\"", modifiedContent); // Other values unchanged
        }

        [Fact]
        public void IncrementIntegerInFile_NonIntegerValue_ThrowsInvalidOperationException()
        {
            string testFilePath = writeTestFile("increment-string.json", @"{ ""version"": ""1.0"" }");
            
            var thrownException = Assert.Throws<InvalidOperationException>(() =>
                JsonPeekParser.IncrementIntegerInFile(testFilePath, "version"));
            
            Assert.Contains("not an integer", thrownException.Message);
            Assert.Contains("1.0", thrownException.Message);
        }
    }

    /// <summary>
    /// Fake IBuildEngine for testing MSBuild tasks.
    /// </summary>
    internal class FakeBuildEngine : IBuildEngine
    {
        public List<string> LoggedErrors { get; } = new List<string>();
        public List<string> LoggedWarnings { get; } = new List<string>();
        public List<string> LoggedMessages { get; } = new List<string>();

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