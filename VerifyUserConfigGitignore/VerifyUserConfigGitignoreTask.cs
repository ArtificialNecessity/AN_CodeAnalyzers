using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MAB.DotIgnore;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AN.CodeAnalyzers.VerifyUserConfigGitignore
{
    /// <summary>
    /// MSBuild Task that verifies user-config files are properly gitignored.
    /// Prevents accidental commits of per-developer configuration files.
    /// </summary>
    public class VerifyUserConfigGitignoreTask : Task
    {
        /// <summary>
        /// The project directory to start searching for .gitignore from.
        /// Typically $(MSBuildProjectDirectory).
        /// </summary>
        [Required]
        public string ProjectDirectory { get; set; } = "";

        /// <summary>
        /// Severity level: "error" (default) or "warning".
        /// </summary>
        public string Severity { get; set; } = "error";

        /// <summary>
        /// Hardcoded list of user-config files that must be gitignored.
        /// </summary>
        private static readonly string[] requiredIgnoredFiles = new[]
        {
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "nuget.config",
            ".editorconfig"
        };

        public override bool Execute()
        {
            if (!Directory.Exists(ProjectDirectory)) {
                Log.LogError($"VerifyUserConfigGitignore: ProjectDirectory does not exist: '{ProjectDirectory}'");
                return false;
            }

            string? gitignoreFilePath = findGitignoreFile(ProjectDirectory);
            
            if (gitignoreFilePath == null) {
                logMessage(
                    "VerifyUserConfigGitignore: No .gitignore file found. " +
                    "Create one and add entries for user-config files: " +
                    string.Join(", ", requiredIgnoredFiles));
                return Severity.Equals("warning", StringComparison.OrdinalIgnoreCase);
            }

            IgnoreList gitignoreRules;
            try {
                gitignoreRules = new IgnoreList(gitignoreFilePath);
            }
            catch (Exception ex) {
                Log.LogError($"VerifyUserConfigGitignore: Failed to parse .gitignore at '{gitignoreFilePath}': {ex.Message}");
                return false;
            }

            var unignoredFiles = new List<string>();
            string gitignoreDirectory = Path.GetDirectoryName(gitignoreFilePath)!;

            foreach (string requiredIgnoredFilename in requiredIgnoredFiles) {
                // Check if the file would be ignored at the root of the git repo
                string testFilePath = Path.Combine(gitignoreDirectory, requiredIgnoredFilename);
                var testFileInfo = new FileInfo(testFilePath);
                
                if (!gitignoreRules.IsIgnored(testFileInfo)) {
                    unignoredFiles.Add(requiredIgnoredFilename);
                }
            }

            if (unignoredFiles.Count == 0) {
                return true; // All files properly ignored
            }

            // Report unignored files
            logMessage(
                $"VerifyUserConfigGitignore: {unignoredFiles.Count} file(s) not covered by .gitignore at '{gitignoreFilePath}'.");
            
            foreach (string unignoredFilename in unignoredFiles) {
                logMessage($"  NOT IGNORED: {unignoredFilename}");
            }
            
            logMessage("Add these entries to your .gitignore to prevent accidental commits of local configuration.");

            return Severity.Equals("warning", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Walk up from projectDirectory looking for .gitignore.
        /// Stops at first .gitignore found, or at a .git/ directory, or at filesystem root.
        /// </summary>
        private static string? findGitignoreFile(string projectDirectory)
        {
            DirectoryInfo? currentDirectory = new DirectoryInfo(projectDirectory);

            while (currentDirectory != null) {
                string gitignorePath = Path.Combine(currentDirectory.FullName, ".gitignore");
                if (File.Exists(gitignorePath)) {
                    return gitignorePath;
                }

                // Stop if we hit a .git directory (repo root)
                string gitDirPath = Path.Combine(currentDirectory.FullName, ".git");
                if (Directory.Exists(gitDirPath)) {
                    return null; // .git exists but no .gitignore
                }

                currentDirectory = currentDirectory.Parent;
            }

            return null; // Reached filesystem root without finding .gitignore
        }

        /// <summary>
        /// Log a message as error or warning based on Severity property.
        /// </summary>
        private void logMessage(string message)
        {
            if (Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
                Log.LogWarning(message);
            } else {
                Log.LogError(message);
            }
        }
    }
}