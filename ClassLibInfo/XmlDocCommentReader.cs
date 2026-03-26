using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace AN.CodeAnalyzers.ClassLibInfo
{
    /// <summary>
    /// Reads XML documentation sidecar files ({AssemblyName}.xml) and provides
    /// lookup of summary text by member documentation ID.
    ///
    /// Member ID format (from C# spec):
    ///   T:Namespace.TypeName                              — type
    ///   M:Namespace.TypeName.MethodName(ParamType1,...)   — method
    ///   P:Namespace.TypeName.PropertyName                 — property
    ///   F:Namespace.TypeName.FieldName                    — field
    ///   E:Namespace.TypeName.EventName                    — event
    /// </summary>
    public sealed class XmlDocCommentReader
    {
        private readonly Dictionary<string, string> _summaryByMemberId;

        private XmlDocCommentReader(Dictionary<string, string> summaryByMemberId)
        {
            _summaryByMemberId = summaryByMemberId;
        }

        /// <summary>
        /// Attempts to load the XML doc sidecar file for the given assembly DLL path.
        /// Looks for {AssemblyName}.xml next to the DLL.
        /// Returns null if no XML file exists.
        /// </summary>
        public static XmlDocCommentReader? TryLoadForAssembly(string assemblyDllPath)
        {
            string xmlDocPath = Path.ChangeExtension(assemblyDllPath, ".xml");
            if (!File.Exists(xmlDocPath)) {
                return null;
            }

            var summaryByMemberId = new Dictionary<string, string>(StringComparer.Ordinal);

            var xmlDoc = new XmlDocument();
            xmlDoc.Load(xmlDocPath);

            var memberNodes = xmlDoc.SelectNodes("/doc/members/member");
            if (memberNodes == null) {
                return new XmlDocCommentReader(summaryByMemberId);
            }

            foreach (XmlNode memberNode in memberNodes) {
                string? memberId = memberNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(memberId)) continue;

                var summaryNode = memberNode.SelectSingleNode("summary");
                if (summaryNode == null) continue;

                // Extract and normalize the summary text
                string rawSummaryText = summaryNode.InnerText;
                string normalizedSummary = normalizeSummaryText(rawSummaryText);

                if (!string.IsNullOrEmpty(normalizedSummary)) {
                    summaryByMemberId[memberId] = normalizedSummary;
                }
            }

            return new XmlDocCommentReader(summaryByMemberId);
        }

        /// <summary>
        /// Gets the summary text for a member by its documentation ID.
        /// Returns null if no summary found.
        /// </summary>
        public string? GetSummary(string memberId)
        {
            _summaryByMemberId.TryGetValue(memberId, out string? summaryText);
            return summaryText;
        }

        /// <summary>
        /// Gets the summary text, truncated to maxLength characters for "brief" mode.
        /// Appends "..." if truncated.
        /// </summary>
        public string? GetBriefSummary(string memberId, int maxBriefLength = 120)
        {
            string? fullSummary = GetSummary(memberId);
            if (fullSummary == null) return null;

            if (fullSummary.Length <= maxBriefLength) return fullSummary;

            // Truncate at word boundary if possible
            int truncateAt = fullSummary.LastIndexOf(' ', maxBriefLength);
            if (truncateAt < maxBriefLength / 2) {
                truncateAt = maxBriefLength; // no good word boundary, hard truncate
            }
            return fullSummary.Substring(0, truncateAt) + "...";
        }

        /// <summary>
        /// Normalizes XML doc comment text: collapses whitespace, trims, single line.
        /// </summary>
        private static string normalizeSummaryText(string rawText)
        {
            // Replace newlines and multiple spaces with single space
            var normalizedChars = new List<char>(rawText.Length);
            bool previousWasWhitespace = true; // start true to trim leading whitespace

            foreach (char currentChar in rawText) {
                if (char.IsWhiteSpace(currentChar)) {
                    if (!previousWasWhitespace) {
                        normalizedChars.Add(' ');
                        previousWasWhitespace = true;
                    }
                } else {
                    normalizedChars.Add(currentChar);
                    previousWasWhitespace = false;
                }
            }

            // Trim trailing whitespace
            while (normalizedChars.Count > 0 && normalizedChars[normalizedChars.Count - 1] == ' ') {
                normalizedChars.RemoveAt(normalizedChars.Count - 1);
            }

            return new string(normalizedChars.ToArray());
        }

        // ──────────────────────────────────────────────
        // Member ID construction helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Builds a type documentation ID: "T:Namespace.TypeName"
        /// </summary>
        public static string BuildTypeDocId(string namespaceName, string typeName)
        {
            if (string.IsNullOrEmpty(namespaceName)) {
                return $"T:{typeName}";
            }
            return $"T:{namespaceName}.{typeName}";
        }

        /// <summary>
        /// Builds a method documentation ID: "M:Namespace.TypeName.MethodName(ParamType1,ParamType2)"
        /// For constructors, methodName should be "#ctor".
        /// </summary>
        public static string BuildMethodDocId(string namespaceName, string typeName, string methodName, string[] parameterTypeNames)
        {
            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            string docMethodName = methodName == ".ctor" ? "#ctor" : methodName;

            if (parameterTypeNames.Length == 0) {
                return $"M:{fullTypeName}.{docMethodName}";
            }

            return $"M:{fullTypeName}.{docMethodName}({string.Join(",", parameterTypeNames)})";
        }

        /// <summary>
        /// Builds a property documentation ID: "P:Namespace.TypeName.PropertyName"
        /// </summary>
        public static string BuildPropertyDocId(string namespaceName, string typeName, string propertyName)
        {
            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            return $"P:{fullTypeName}.{propertyName}";
        }

        /// <summary>
        /// Builds a field documentation ID: "F:Namespace.TypeName.FieldName"
        /// </summary>
        public static string BuildFieldDocId(string namespaceName, string typeName, string fieldName)
        {
            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            return $"F:{fullTypeName}.{fieldName}";
        }

        /// <summary>
        /// Builds an event documentation ID: "E:Namespace.TypeName.EventName"
        /// </summary>
        public static string BuildEventDocId(string namespaceName, string typeName, string eventName)
        {
            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            return $"E:{fullTypeName}.{eventName}";
        }
    }
}