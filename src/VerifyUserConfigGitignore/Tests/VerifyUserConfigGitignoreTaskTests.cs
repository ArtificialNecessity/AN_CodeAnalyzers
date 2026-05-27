using System;
using System.IO;
using Xunit;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

namespace AN.CodeAnalyzers.VerifyUserConfigGitignore.Tests
{
    public class VerifyUserConfigGitignoreTaskTests : IDisposable
    {
        private readonly string testRootDirectory;

        public VerifyUserConfigGitignoreTaskTests()
        {
            testRootDirectory = Path.Combine(Path.GetTempPath(), "VerifyUserConfigGitignore_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRootDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(testRootDirectory))
            {
                Directory.Delete(testRootDirectory, recursive: true);
            }
        }

        [Fact]
        public void AllFilesCoveredByExactEntries_Succeeds()
        {
            // Arrange
            string gitignoreContent = @"
# User config files
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
global.json
nuget.config
.editorconfig
";
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent);

            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = new FakeBuildEngine()
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.True(taskSucceeded);
            Assert.Empty(((FakeBuildEngine)task.BuildEngine).LoggedErrors);
        }

        [Fact]
        public void AllFilesCoveredByGlobPattern_Succeeds()
        {
            // Arrange
            string gitignoreContent = @"
# User Controlled Config Files should be ignored
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
global.json
nuget.config
.editorconfig
";
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent);

            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = new FakeBuildEngine()
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            var fakeBuildEngine = (FakeBuildEngine)task.BuildEngine;
            if (!taskSucceeded || fakeBuildEngine.LoggedErrors.Count > 0)
            {
                string allErrors = string.Join("\n", fakeBuildEngine.LoggedErrors);
                throw new Exception($"Task failed. Errors:\n{allErrors}");
            }
            Assert.True(taskSucceeded);
            Assert.Empty(fakeBuildEngine.LoggedErrors);
        }

        [Fact]
        public void SomeFilesMissing_FailsWithCorrectErrors()
        {
            // Arrange
            string gitignoreContent = @"
Directory.Build.props
# Missing: Directory.Build.targets, Directory.Packages.props, global.json, nuget.config, .editorconfig
";
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent);

            var fakeBuildEngine = new FakeBuildEngine();
            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);

            string allErrors = string.Join("\n", fakeBuildEngine.LoggedErrors);
            Assert.Contains("5 file(s) not covered by .gitignore", allErrors);
            Assert.Contains("NOT IGNORED: Directory.Build.targets", allErrors);
            Assert.Contains("NOT IGNORED: Directory.Packages.props", allErrors);
            Assert.Contains("NOT IGNORED: global.json", allErrors);
            Assert.Contains("NOT IGNORED: nuget.config", allErrors);
            Assert.Contains("NOT IGNORED: .editorconfig", allErrors);
        }

        [Fact]
        public void NoGitignoreFound_FailsWithAppropriateMessage()
        {
            // Arrange - no .gitignore file created
            var fakeBuildEngine = new FakeBuildEngine();
            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);
            Assert.Contains("No .gitignore file found", fakeBuildEngine.LoggedErrors[0]);
        }

        [Fact]
        public void NegationPatternUndoesCoverage_ReportsFileAsNotIgnored()
        {
            // Arrange
            string gitignoreContent = @"
*.props
!Directory.Build.props
";
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent);

            var fakeBuildEngine = new FakeBuildEngine();
            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);

            string allErrors = string.Join("\n", fakeBuildEngine.LoggedErrors);
            Assert.Contains("NOT IGNORED: Directory.Build.props", allErrors);
        }

        [Fact]
        public void EmptyGitignore_FailsListingAllFiles()
        {
            // Arrange
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, "");

            var fakeBuildEngine = new FakeBuildEngine();
            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "error",
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.False(taskSucceeded);
            Assert.NotEmpty(fakeBuildEngine.LoggedErrors);

            string allErrors = string.Join("\n", fakeBuildEngine.LoggedErrors);
            Assert.Contains("6 file(s) not covered by .gitignore", allErrors);
        }

        [Fact]
        public void SeverityWarning_LogsWarningsInsteadOfErrors()
        {
            // Arrange
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, ""); // Empty gitignore

            var fakeBuildEngine = new FakeBuildEngine();
            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = testRootDirectory,
                Severity = "warning",
                BuildEngine = fakeBuildEngine
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.True(taskSucceeded); // Task succeeds when severity is warning
            Assert.Empty(fakeBuildEngine.LoggedErrors);
            Assert.NotEmpty(fakeBuildEngine.LoggedWarnings);

            string allWarnings = string.Join("\n", fakeBuildEngine.LoggedWarnings);
            Assert.Contains("6 file(s) not covered by .gitignore", allWarnings);
        }

        [Fact]
        public void GitignoreInParentDirectory_IsFound()
        {
            // Arrange
            string gitignoreContent = @"
# User controlled config files should be ignored            
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
global.json
nuget.config
.editorconfig
";
            string gitignorePath = Path.Combine(testRootDirectory, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent);

            // Create a subdirectory for the project
            string projectSubdirectory = Path.Combine(testRootDirectory, "src", "MyProject");
            Directory.CreateDirectory(projectSubdirectory);

            var task = new VerifyUserConfigGitignoreTask
            {
                ProjectDirectory = projectSubdirectory,
                Severity = "error",
                BuildEngine = new FakeBuildEngine()
            };

            // Act
            bool taskSucceeded = task.Execute();

            // Assert
            Assert.True(taskSucceeded);
            Assert.Empty(((FakeBuildEngine)task.BuildEngine).LoggedErrors);
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