using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AN.CodeAnalyzers.StableABIVerification
{
    /// <summary>
    /// MSBuild Task that verifies or generates a StableABI.snapshot file.
    /// In verify mode (default): compares current assembly against committed snapshot, errors on mismatch.
    /// In generate mode: writes the snapshot file from the current assembly.
    /// Line endings are detected from: 1) existing snapshot file, 2) platform default (Windows=CRLF, else LF).
    /// </summary>
    public class StableABIVerifyTask : Task
    {
        /// <summary>
        /// Path to the compiled assembly DLL to analyze.
        /// </summary>
        [Required]
        public string AssemblyPath { get; set; } = "";

        /// <summary>
        /// Path to the committed StableABI.snapshot file (next to the .csproj).
        /// </summary>
        [Required]
        public string SnapshotPath { get; set; } = "";

        /// <summary>
        /// Scope: "public" for public types only, "all" for all types.
        /// </summary>
        public string Scope { get; set; } = "public";

        /// <summary>
        /// When true, generates/overwrites the snapshot file instead of verifying.
        /// </summary>
        public bool GenerateMode { get; set; } = false;

        public override bool Execute()
        {
            if (!File.Exists(AssemblyPath)) {
                Log.LogError($"StableABI: Assembly not found at '{AssemblyPath}'. Build must complete before snapshot verification.");
                return false;
            }

            List<string> currentSnapshotLines = StableABISnapshotGenerator.GenerateSnapshotLines(AssemblyPath, Scope);

            if (GenerateMode) {
                return executeGenerateMode(currentSnapshotLines);
            }

            return executeVerifyMode(currentSnapshotLines);
        }

        // ──────────────────────────────────────────────
        // Generate mode
        // ──────────────────────────────────────────────

        private bool executeGenerateMode(List<string> currentSnapshotLines)
        {
            string lineEnding = detectLineEnding(SnapshotPath);
            string snapshotFileContent = joinLinesWithEnding(currentSnapshotLines, lineEnding);
            File.WriteAllText(SnapshotPath, snapshotFileContent);
            Log.LogMessage(MessageImportance.High,
                $"StableABI: Snapshot written to {SnapshotPath} (line ending: {(lineEnding == "\r\n" ? "CRLF" : "LF")})");
            return true;
        }

        // ──────────────────────────────────────────────
        // Verify mode
        // ──────────────────────────────────────────────

        private bool executeVerifyMode(List<string> currentSnapshotLines)
        {
            if (!File.Exists(SnapshotPath)) {
                Log.LogError(
                    "StableABI: No snapshot file found at '{0}'. " +
                    "Run 'dotnet build /p:UpdateStableABI=true' to generate one.",
                    SnapshotPath);
                return false;
            }

            List<string> committedSnapshotLines = readSnapshotFileAsLines(SnapshotPath);

            // Check version compatibility
            int committedVersion = parseSnapshotVersion(committedSnapshotLines);
            if (committedVersion < StableABISnapshotGenerator.CurrentFormatVersion) {
                Log.LogWarning(
                    "StableABI: Snapshot at '{0}' is format version {1}, current is {2}. " +
                    "Type/method/property/event/field changes are NOT being verified. " +
                    "Run 'dotnet build /p:UpdateStableABI=true' to upgrade.",
                    SnapshotPath, committedVersion, StableABISnapshotGenerator.CurrentFormatVersion);
            }

            // Compare as line lists (line-ending agnostic)
            if (linesAreEqual(committedSnapshotLines, currentSnapshotLines)) {
                return true; // Match — silent success
            }

            // Parse both for detailed diff
            var committedEntries = parseSnapshotLinesToDictionary(committedSnapshotLines);
            var currentEntries = parseSnapshotLinesToDictionary(currentSnapshotLines);

            var addedKeys = new List<string>();
            var removedKeys = new List<string>();
            var changedKeys = new List<string>();

            foreach (var committedEntry in committedEntries) {
                if (!currentEntries.TryGetValue(committedEntry.Key, out string? currentValue)) {
                    removedKeys.Add(committedEntry.Key);
                } else if (committedEntry.Value != currentValue) {
                    changedKeys.Add(committedEntry.Key);
                }
            }

            foreach (var currentEntry in currentEntries) {
                if (!committedEntries.ContainsKey(currentEntry.Key)) {
                    addedKeys.Add(currentEntry.Key);
                }
            }

            int totalChanges = addedKeys.Count + removedKeys.Count + changedKeys.Count;

            Log.LogError(
                $"StableABI snapshot mismatch: {totalChanges} change(s) detected. " +
                $"To accept: dotnet build /p:UpdateStableABI=true");

            foreach (string changedKey in changedKeys) {
                Log.LogError($"  CHANGED: {changedKey}: {committedEntries[changedKey]} -> {currentEntries[changedKey]}");
            }

            foreach (string addedKey in addedKeys) {
                Log.LogError($"  ADDED:   {addedKey}: {currentEntries[addedKey]}");
            }

            foreach (string removedKey in removedKeys) {
                Log.LogError($"  REMOVED: {removedKey}: {committedEntries[removedKey]}");
            }

            return false;
        }

        // ──────────────────────────────────────────────
        // Line ending detection
        // ──────────────────────────────────────────────

        /// <summary>
        /// Detects the line ending to use when writing the snapshot file.
        /// Priority: 1) existing file's line endings, 2) platform default (Windows=CRLF, else LF).
        /// </summary>
        private static string detectLineEnding(string snapshotFilePath)
        {
            // If the file already exists, match its existing line endings
            if (File.Exists(snapshotFilePath)) {
                string existingFileContent = File.ReadAllText(snapshotFilePath);
                string? detectedEnding = detectLineEndingFromContent(existingFileContent);
                if (detectedEnding != null) {
                    return detectedEnding;
                }
            }

            // Platform default: Windows = CRLF, everything else = LF
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";
        }

        /// <summary>
        /// Detects line ending from file content by looking for the first newline.
        /// Returns "\r\n" if CRLF found, "\n" if LF found, null if no newlines.
        /// </summary>
        public static string? detectLineEndingFromContent(string fileContent)
        {
            int lfIndex = fileContent.IndexOf('\n');
            if (lfIndex < 0) {
                return null; // no newlines at all
            }
            if (lfIndex > 0 && fileContent[lfIndex - 1] == '\r') {
                return "\r\n";
            }
            return "\n";
        }

        // ──────────────────────────────────────────────
        // Snapshot line helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Joins lines with the specified line ending, with a trailing line ending.
        /// Returns empty string for empty list.
        /// </summary>
        private static string joinLinesWithEnding(List<string> snapshotLines, string lineEnding)
        {
            if (snapshotLines.Count == 0) {
                return "";
            }
            return string.Join(lineEnding, snapshotLines) + lineEnding;
        }

        /// <summary>
        /// Reads a snapshot file and splits into lines, stripping line endings.
        /// </summary>
        private static List<string> readSnapshotFileAsLines(string snapshotFilePath)
        {
            string fileContent = File.ReadAllText(snapshotFilePath);
            var parsedLines = new List<string>();
            foreach (string rawLine in fileContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)) {
                string trimmedLine = rawLine.Trim();
                if (trimmedLine.Length > 0) {
                    parsedLines.Add(trimmedLine);
                }
            }
            return parsedLines;
        }

        private static bool linesAreEqual(List<string> linesA, List<string> linesB)
        {
            if (linesA.Count != linesB.Count) {
                return false;
            }
            for (int lineIndex = 0; lineIndex < linesA.Count; lineIndex++) {
                if (linesA[lineIndex] != linesB[lineIndex]) {
                    return false;
                }
            }
            return true;
        }

        private static int parseSnapshotVersion(List<string> snapshotLines)
        {
            if (snapshotLines.Count == 0) {
                return 1;
            }
            string firstLine = snapshotLines[0];
            if (firstLine.StartsWith("__stableApiVersion: ")) {
                string versionString = firstLine.Substring("__stableApiVersion: ".Length);
                if (int.TryParse(versionString, out int parsedVersion)) {
                    return parsedVersion;
                }
            }
            return 1; // no version header = version 1
        }

        private static Dictionary<string, string> parseSnapshotLinesToDictionary(List<string> snapshotLines)
        {
            var parsedEntries = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string snapshotLine in snapshotLines) {
                int colonSeparatorIndex = snapshotLine.IndexOf(": ", StringComparison.Ordinal);
                if (colonSeparatorIndex < 0) {
                    continue;
                }

                string entryKey = snapshotLine.Substring(0, colonSeparatorIndex);
                string entryValue = snapshotLine.Substring(colonSeparatorIndex + 2);
                parsedEntries[entryKey] = entryValue;
            }

            return parsedEntries;
        }
    }
}