using System;
using System.Collections.Generic;
using System.IO;
using Hjson;

namespace AN.CodeAnalyzers
{
    /// <summary>
    /// Parses JSON, JSONC, or HJSON files and extracts values by dot-separated key paths.
    /// Uses the Hjson parser which is a superset of JSON that also handles comments.
    /// </summary>
    public static class JsonPeekParser
    {
        /// <summary>
        /// Reads a JSON/JSONC/HJSON file and returns the string value at the given dot-separated key path.
        /// </summary>
        /// <param name="filePath">Path to the JSON/JSONC/HJSON file to read.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "version" or "dependencies.package.version").</param>
        /// <returns>The string representation of the value at the key path.</returns>
        public static string ReadValueFromFile(string filePath, string keyPath)
        {
            string fileContent = File.ReadAllText(filePath);
            return ReadValueFromString(fileContent, keyPath, filePath);
        }

        /// <summary>
        /// Parses a JSON/JSONC/HJSON string and returns the string value at the given dot-separated key path.
        /// </summary>
        /// <param name="jsonContent">The JSON/JSONC/HJSON content to parse.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "version" or "dependencies.package.version").</param>
        /// <param name="sourceDescription">Description of the source (e.g. file path) for error messages.</param>
        /// <returns>The string representation of the value at the key path.</returns>
        public static string ReadValueFromString(string jsonContent, string keyPath, string sourceDescription = "<string>")
        {
            if (string.IsNullOrWhiteSpace(keyPath)) {
                throw new ArgumentException(
                    $"JsonPeek: keyPath cannot be empty. " +
                    $"Provide a dot-separated key path like 'version' or 'parent.child.key'.");
            }

            JsonValue parsedRoot = HjsonValue.Parse(jsonContent);

            string[] keySegments = keyPath.Split('.');
            JsonValue currentNode = parsedRoot;

            for (int segmentIndex = 0; segmentIndex < keySegments.Length; segmentIndex++) {
                string currentKeySegment = keySegments[segmentIndex];
                string traversedPathSoFar = string.Join(".", keySegments, 0, segmentIndex + 1);

                if (currentNode.JsonType != JsonType.Object) {
                    throw new InvalidOperationException(
                        $"JsonPeek: Cannot navigate into '{traversedPathSoFar}' in {sourceDescription} — " +
                        $"expected an object at '{string.Join(".", keySegments, 0, segmentIndex)}' " +
                        $"but found {currentNode.JsonType}.");
                }

                JsonObject currentObject = (JsonObject)currentNode;

                if (!currentObject.ContainsKey(currentKeySegment)) {
                    throw new KeyNotFoundException(
                        $"JsonPeek: Key '{currentKeySegment}' not found at path '{traversedPathSoFar}' " +
                        $"in {sourceDescription}. " +
                        $"Available keys: {string.Join(", ", currentObject.Keys)}");
                }

                currentNode = currentObject[currentKeySegment];
            }

            // For objects and arrays, return the JSON string representation
            if (currentNode.JsonType == JsonType.Object || currentNode.JsonType == JsonType.Array) {
                return currentNode.ToString(Stringify.Plain);
            }

            // For primitives, return the raw value as string
            return currentNode.Qstr();
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // TODO FIXME: This uses regex-based text replacement which is fragile and limited.
        // We need to eventually switch to an HJson library with proper in-place editing support
        // that will preserve comments, formatting, and handle complex nested structures.
        // Current limitation: Only works for simple top-level keys in flat objects.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes a value to a JSON/JSONC/HJSON file at the given dot-separated key path.
        /// Uses text-based replacement to preserve comments and formatting.
        /// </summary>
        /// <param name="filePath">Path to the JSON/JSONC/HJSON file to modify.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "version" or "parent.child.key").</param>
        /// <param name="newValue">The new value to write (as a string - will be inserted as-is for numbers, or quoted for strings).</param>
        public static void WriteValueToFile(string filePath, string keyPath, string newValue)
        {
            string fileContent = File.ReadAllText(filePath);
            string updatedContent = WriteValueToString(fileContent, keyPath, newValue, filePath);
            File.WriteAllText(filePath, updatedContent);
        }

        /// <summary>
        /// Writes a value to a JSON/JSONC/HJSON string at the given dot-separated key path.
        /// Uses text-based regex replacement to preserve comments and formatting.
        /// Only supports simple key paths (no nested objects for now).
        /// </summary>
        /// <param name="jsonContent">The JSON/JSONC/HJSON content to modify.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "version" or "parent.child.key").</param>
        /// <param name="newValue">The new value to write (as a string - will be inserted as-is).</param>
        /// <param name="sourceDescription">Description of the source (e.g. file path) for error messages.</param>
        /// <returns>The modified JSON/JSONC/HJSON string with comments preserved.</returns>
        public static string WriteValueToString(string jsonContent, string keyPath, string newValue, string sourceDescription = "<string>")
        {
            if (string.IsNullOrWhiteSpace(keyPath)) {
                throw new ArgumentException(
                    $"JsonPeek: keyPath cannot be empty. " +
                    $"Provide a dot-separated key path like 'version' or 'parent.child.key'.");
            }

            // First verify the key exists by parsing
            JsonValue parsedRoot = HjsonValue.Parse(jsonContent);
            string[] keySegments = keyPath.Split('.');
            JsonValue currentNode = parsedRoot;

            for (int segmentIndex = 0; segmentIndex < keySegments.Length; segmentIndex++) {
                string currentKeySegment = keySegments[segmentIndex];
                string traversedPathSoFar = string.Join(".", keySegments, 0, segmentIndex + 1);

                if (currentNode.JsonType != JsonType.Object) {
                    throw new InvalidOperationException(
                        $"JsonPeek: Cannot navigate into '{traversedPathSoFar}' in {sourceDescription} — " +
                        $"expected an object at '{string.Join(".", keySegments, 0, segmentIndex)}' " +
                        $"but found {currentNode.JsonType}.");
                }

                JsonObject currentObject = (JsonObject)currentNode;

                if (!currentObject.ContainsKey(currentKeySegment)) {
                    throw new KeyNotFoundException(
                        $"JsonPeek: Key '{currentKeySegment}' not found at path '{traversedPathSoFar}' " +
                        $"in {sourceDescription}. " +
                        $"Available keys: {string.Join(", ", currentObject.Keys)}");
                }

                currentNode = currentObject[currentKeySegment];
            }

            // Use regex to replace the value while preserving comments, formatting, and quoting style
            // Pattern matches: "key" : value  or  "key": value  or  key : value  or  key: value
            // Captures: (1) everything before the value, (2) the old value
            string finalKey = keySegments[keySegments.Length - 1];
            string escapedKey = System.Text.RegularExpressions.Regex.Escape(finalKey);
            string pattern = $@"((?:""{escapedKey}""|{escapedKey})\s*:\s*)([^,\r\n}}]+)";

            System.Text.RegularExpressions.Match matchResult = System.Text.RegularExpressions.Regex.Match(jsonContent, pattern);
            if (!matchResult.Success) {
                throw new InvalidOperationException(
                    $"JsonPeek: Failed to find key '{keyPath}' in {sourceDescription} for replacement. " +
                    $"The regex pattern may not have matched the file format.");
            }

            string oldValue = matchResult.Groups[2].Value.Trim();

            // Preserve quoting style: if old value was quoted, quote the new value
            string formattedNewValue;
            if (oldValue.StartsWith("\"") && oldValue.EndsWith("\"")) {
                // Old value was quoted - quote the new value if it's not already quoted
                if (!newValue.StartsWith("\"")) {
                    formattedNewValue = $"\"{newValue}\"";
                } else {
                    formattedNewValue = newValue;
                }
            } else {
                // Old value was unquoted - use new value as-is
                formattedNewValue = newValue;
            }

            // Use MatchEvaluator to properly handle the replacement
            string updatedContent = System.Text.RegularExpressions.Regex.Replace(
                jsonContent,
                pattern,
                m => m.Groups[1].Value + formattedNewValue);

            return updatedContent;
        }

        /// <summary>
        /// Increments an integer value in a JSON/JSONC/HJSON file at the given dot-separated key path.
        /// </summary>
        /// <param name="filePath">Path to the JSON/JSONC/HJSON file to modify.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "buildNumberOffset").</param>
        /// <param name="incrementAmount">Amount to increment by (default: 1).</param>
        /// <returns>The new value after incrementing.</returns>
        public static int IncrementIntegerInFile(string filePath, string keyPath, int incrementAmount = 1)
        {
            string currentValueString = ReadValueFromFile(filePath, keyPath);

            if (!int.TryParse(currentValueString, out int currentValue)) {
                throw new InvalidOperationException(
                    $"JsonPeek: Cannot increment '{keyPath}' in {filePath} — " +
                    $"current value '{currentValueString}' is not an integer.");
            }

            int newValue = currentValue + incrementAmount;
            WriteValueToFile(filePath, keyPath, newValue.ToString());
            return newValue;
        }

        /// <summary>
        /// Adds to a floating point value in a JSON/JSONC/HJSON file at the given dot-separated key path.
        /// </summary>
        /// <param name="filePath">Path to the JSON/JSONC/HJSON file to modify.</param>
        /// <param name="keyPath">Dot-separated key path (e.g. "version").</param>
        /// <param name="addAmount">Amount to add (can be negative to subtract).</param>
        /// <returns>The new value after adding.</returns>
        public static double AddToFloatInFile(string filePath, string keyPath, double addAmount)
        {
            string currentValueString = ReadValueFromFile(filePath, keyPath);

            if (!double.TryParse(currentValueString, out double currentValue)) {
                throw new InvalidOperationException(
                    $"JsonPeek: Cannot add to '{keyPath}' in {filePath} — " +
                    $"current value '{currentValueString}' is not a number.");
            }

            double newValue = currentValue + addAmount;
            WriteValueToFile(filePath, keyPath, newValue.ToString("G"));
            return newValue;
        }
    }
}