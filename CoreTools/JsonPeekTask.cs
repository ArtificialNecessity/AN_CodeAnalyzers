using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AN.CodeAnalyzers
{
    /// <summary>
    /// MSBuild Task that reads a value from a JSON/JSONC/HJSON file by key path.
    ///
    /// Usage in .targets:
    ///   &lt;JsonPeek File="config.json" KeyPath="version"&gt;
    ///     &lt;Output TaskParameter="Value" PropertyName="ConfigVersion" /&gt;
    ///   &lt;/JsonPeek&gt;
    /// </summary>
    public class JsonPeek : Task
    {
        /// <summary>
        /// Path to the JSON/JSONC/HJSON file to read.
        /// </summary>
        [Required]
        public string File { get; set; } = "";

        /// <summary>
        /// Dot-separated key path to query (e.g. "version" or "dependencies.package.version").
        /// </summary>
        [Required]
        public string KeyPath { get; set; } = "";

        /// <summary>
        /// The extracted value as a string. Use with &lt;Output TaskParameter="Value" .../&gt;.
        /// </summary>
        [Output]
        public string Value { get; set; } = "";

        public override bool Execute()
        {
            if (!System.IO.File.Exists(File)) {
                Log.LogError($"JsonPeek: File not found: '{File}'");
                return false;
            }

            if (string.IsNullOrWhiteSpace(KeyPath)) {
                Log.LogError("JsonPeek: KeyPath cannot be empty. Provide a dot-separated key path like 'version' or 'parent.child.key'.");
                return false;
            }

            try {
                Value = JsonPeekParser.ReadValueFromFile(File, KeyPath);
                Log.LogMessage(MessageImportance.Low, $"JsonPeek: {File}[{KeyPath}] = {Value}");
                return true;
            }
            catch (Exception parseOrQueryException) {
                Log.LogError($"JsonPeek: {parseOrQueryException.Message}");
                return false;
            }
        }
    }
}