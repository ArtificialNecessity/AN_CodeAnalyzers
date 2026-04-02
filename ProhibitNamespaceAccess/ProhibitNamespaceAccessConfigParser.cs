using System;
using System.Collections.Generic;

namespace AN.CodeAnalyzers.ProhibitNamespaceAccess
{
    /// <summary>
    /// A namespace pattern with optional glob support.
    /// Without '*': exact namespace match. With '*': prefix match on everything before the '*'.
    /// </summary>
    public readonly struct NamespacePattern
    {
        /// <summary>The original pattern string as written in config (e.g. "System.Runtime.Interop*").</summary>
        public string OriginalPattern { get; }

        /// <summary>For glob patterns, the prefix before '*'. For exact patterns, same as OriginalPattern.</summary>
        public string MatchPrefix { get; }

        /// <summary>True if the pattern contains '*' (prefix glob match).</summary>
        public bool IsGlob { get; }

        public NamespacePattern(string originalPattern)
        {
            OriginalPattern = originalPattern;
            int starIndex = originalPattern.IndexOf('*');
            if (starIndex >= 0)
            {
                IsGlob = true;
                MatchPrefix = originalPattern.Substring(0, starIndex);
            }
            else
            {
                IsGlob = false;
                MatchPrefix = originalPattern;
            }
        }

        /// <summary>
        /// Returns true if the given fully-qualified namespace matches this pattern.
        /// </summary>
        public bool MatchesNamespace(string fullyQualifiedNamespace)
        {
            if (IsGlob)
                return fullyQualifiedNamespace.StartsWith(MatchPrefix, StringComparison.Ordinal);
            return string.Equals(fullyQualifiedNamespace, OriginalPattern, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Parsed configuration for ProhibitNamespaceAccess.
    /// Contains separate lists of patterns for error and warn severities.
    /// </summary>
    public sealed class ProhibitNamespaceAccessConfig
    {
        public List<NamespacePattern> ErrorPatterns { get; } = new List<NamespacePattern>();
        public List<NamespacePattern> WarnPatterns { get; } = new List<NamespacePattern>();

        public bool HasAnyPatterns => ErrorPatterns.Count > 0 || WarnPatterns.Count > 0;
    }

    /// <summary>
    /// Parses the ProhibitNamespaceAccess MSBuild property value.
    /// Format: <c>{ error = [ "pattern1", "pattern2" ], warn = [ "pattern3" ] }</c>
    /// </summary>
    public static class ProhibitNamespaceAccessConfigParser
    {
        /// <summary>
        /// Parses the config string into a <see cref="ProhibitNamespaceAccessConfig"/>.
        /// Returns an empty config if <paramref name="configString"/> is null or whitespace.
        /// Throws <see cref="FormatException"/> if the string is non-empty but malformed.
        /// </summary>
        public static ProhibitNamespaceAccessConfig Parse(string? configString)
        {
            var parsedConfig = new ProhibitNamespaceAccessConfig();

            if (configString == null || string.IsNullOrWhiteSpace(configString))
                return parsedConfig;

            int position = 0;
            skipWhitespace(configString, ref position);
            expectCharacter(configString, ref position, '{');

            bool hasReadFirstEntry = false;
            while (position < configString.Length)
            {
                skipWhitespace(configString, ref position);

                if (position < configString.Length && configString[position] == '}')
                {
                    position++;
                    break;
                }

                if (hasReadFirstEntry)
                {
                    expectCharacter(configString, ref position, ',');
                    skipWhitespace(configString, ref position);

                    // Handle trailing comma: { error = [ "x" ], }
                    if (position < configString.Length && configString[position] == '}')
                    {
                        position++;
                        break;
                    }
                }

                string severityKey = readIdentifier(configString, ref position);
                skipWhitespace(configString, ref position);
                expectCharacter(configString, ref position, '=');
                skipWhitespace(configString, ref position);

                List<string> patternStrings = readStringArray(configString, ref position);

                List<NamespacePattern> targetPatternList;
                if (string.Equals(severityKey, "error", StringComparison.OrdinalIgnoreCase))
                    targetPatternList = parsedConfig.ErrorPatterns;
                else if (string.Equals(severityKey, "warn", StringComparison.OrdinalIgnoreCase))
                    targetPatternList = parsedConfig.WarnPatterns;
                else
                    throw new FormatException(
                        $"ProhibitNamespaceAccess: unknown severity key '{severityKey}' at position {position}. " +
                        $"Expected 'error' or 'warn'. Input: \"{configString}\"");

                foreach (var patternString in patternStrings)
                    targetPatternList.Add(new NamespacePattern(patternString));

                hasReadFirstEntry = true;
            }

            return parsedConfig;
        }

        private static List<string> readStringArray(string configString, ref int position)
        {
            var arrayElements = new List<string>();
            expectCharacter(configString, ref position, '[');

            bool hasReadFirstElement = false;
            while (position < configString.Length)
            {
                skipWhitespace(configString, ref position);

                if (position < configString.Length && configString[position] == ']')
                {
                    position++;
                    break;
                }

                if (hasReadFirstElement)
                {
                    expectCharacter(configString, ref position, ',');
                    skipWhitespace(configString, ref position);

                    // Handle trailing comma: [ "x", ]
                    if (position < configString.Length && configString[position] == ']')
                    {
                        position++;
                        break;
                    }
                }

                string quotedValue = readQuotedString(configString, ref position);
                arrayElements.Add(quotedValue);
                hasReadFirstElement = true;
            }

            return arrayElements;
        }

        private static void skipWhitespace(string configString, ref int position)
        {
            while (position < configString.Length && char.IsWhiteSpace(configString[position]))
                position++;
        }

        private static void expectCharacter(string configString, ref int position, char expectedCharacter)
        {
            if (position >= configString.Length)
                throw new FormatException(
                    $"ProhibitNamespaceAccess: unexpected end of input at position {position}, expected '{expectedCharacter}'. " +
                    $"Input: \"{configString}\"");

            if (configString[position] != expectedCharacter)
                throw new FormatException(
                    $"ProhibitNamespaceAccess: expected '{expectedCharacter}' at position {position}, " +
                    $"found '{configString[position]}'. Input: \"{configString}\"");

            position++;
        }

        private static string readIdentifier(string configString, ref int position)
        {
            int identifierStart = position;

            while (position < configString.Length &&
                   (char.IsLetterOrDigit(configString[position]) || configString[position] == '_'))
            {
                position++;
            }

            if (position == identifierStart)
            {
                string contextFragment = position < configString.Length
                    ? $"found '{configString[position]}'"
                    : "found end of input";
                throw new FormatException(
                    $"ProhibitNamespaceAccess: expected identifier at position {position}, {contextFragment}. " +
                    $"Input: \"{configString}\"");
            }

            return configString.Substring(identifierStart, position - identifierStart);
        }

        private static string readQuotedString(string configString, ref int position)
        {
            expectCharacter(configString, ref position, '"');

            int valueStart = position;

            while (position < configString.Length && configString[position] != '"')
            {
                if (configString[position] == '\\' && position + 1 < configString.Length)
                {
                    position += 2;
                    continue;
                }
                position++;
            }

            if (position >= configString.Length)
                throw new FormatException(
                    $"ProhibitNamespaceAccess: unterminated string starting at position {valueStart - 1}. " +
                    $"Input: \"{configString}\"");

            string quotedValue = configString.Substring(valueStart, position - valueStart);
            position++; // skip closing quote

            return quotedValue;
        }
    }
}