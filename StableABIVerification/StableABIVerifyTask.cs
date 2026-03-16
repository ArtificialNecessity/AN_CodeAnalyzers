using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AN.CodeAnalyzers.StableABIVerification
{
    /// <summary>
    /// MSBuild Task that verifies or generates a StableABI.snapshot file.
    /// In verify mode (default): compares current assembly against committed snapshot, errors on mismatch.
    /// In generate mode: writes the snapshot file from the current assembly.
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
            if (!File.Exists(AssemblyPath))
            {
                Log.LogError($"StableABI: Assembly not found at '{AssemblyPath}'. Build must complete before snapshot verification.");
                return false;
            }

            string currentSnapshotContent = StableABISnapshotGenerator.GenerateSnapshot(AssemblyPath, Scope);

            if (GenerateMode)
            {
                return executeGenerateMode(currentSnapshotContent);
            }

            return executeVerifyMode(currentSnapshotContent);
        }

        private bool executeGenerateMode(string currentSnapshotContent)
        {
            File.WriteAllText(SnapshotPath, currentSnapshotContent);
            Log.LogMessage(MessageImportance.High,
                $"StableABI: Snapshot written to {SnapshotPath}");
            return true;
        }

        private bool executeVerifyMode(string currentSnapshotContent)
        {
            if (!File.Exists(SnapshotPath))
            {
                Log.LogError(
                    "StableABI: No snapshot file found at '{0}'. " +
                    "Run 'dotnet build /p:UpdateStableABI=true' to generate one.",
                    SnapshotPath);
                return false;
            }

            string committedSnapshotContent = File.ReadAllText(SnapshotPath);

            if (committedSnapshotContent == currentSnapshotContent)
            {
                return true; // Match — silent success
            }

            // Parse both snapshots for detailed diff
            var committedEntries = parseSnapshotLines(committedSnapshotContent);
            var currentEntries = parseSnapshotLines(currentSnapshotContent);

            var addedKeys = new List<string>();
            var removedKeys = new List<string>();
            var changedKeys = new List<string>();

            foreach (var committedEntry in committedEntries)
            {
                if (!currentEntries.TryGetValue(committedEntry.Key, out string? currentValue))
                {
                    removedKeys.Add(committedEntry.Key);
                }
                else if (committedEntry.Value != currentValue)
                {
                    changedKeys.Add(committedEntry.Key);
                }
            }

            foreach (var currentEntry in currentEntries)
            {
                if (!committedEntries.ContainsKey(currentEntry.Key))
                {
                    addedKeys.Add(currentEntry.Key);
                }
            }

            int totalChanges = addedKeys.Count + removedKeys.Count + changedKeys.Count;

            Log.LogError(
                $"StableABI snapshot mismatch: {totalChanges} change(s) detected. " +
                $"To accept: dotnet build /p:UpdateStableABI=true");

            foreach (string changedKey in changedKeys)
            {
                Log.LogError($"  CHANGED: {changedKey}: {committedEntries[changedKey]} -> {currentEntries[changedKey]}");
            }

            foreach (string addedKey in addedKeys)
            {
                Log.LogError($"  ADDED:   {addedKey}: {currentEntries[addedKey]}");
            }

            foreach (string removedKey in removedKeys)
            {
                Log.LogError($"  REMOVED: {removedKey}: {committedEntries[removedKey]}");
            }

            return false;
        }

        private static Dictionary<string, string> parseSnapshotLines(string snapshotContent)
        {
            var parsedEntries = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string rawLine in snapshotContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmedLine = rawLine.Trim();
                if (trimmedLine.Length == 0)
                {
                    continue;
                }

                int colonSeparatorIndex = trimmedLine.IndexOf(": ", StringComparison.Ordinal);
                if (colonSeparatorIndex < 0)
                {
                    continue;
                }

                string entryKey = trimmedLine.Substring(0, colonSeparatorIndex);
                string entryValue = trimmedLine.Substring(colonSeparatorIndex + 2);
                parsedEntries[entryKey] = entryValue;
            }

            return parsedEntries;
        }
    }
}