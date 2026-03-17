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
    }
}