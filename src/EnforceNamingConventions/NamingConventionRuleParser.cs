using System;
using System.Collections.Generic;

namespace AN.CodeAnalyzers.EnforceNamingConventions
{
    /// <summary>
    /// Parses a naming convention rule string from the MSBuild property value.
    /// Format: <c>{ event = "On.*", interface = "I.*" }</c>
    /// Keys are unquoted identifiers, values are quoted regex patterns.
    /// </summary>
    public static class NamingConventionRuleParser
    {
        /// <summary>
        /// Parses the rule string into a dictionary of category → regex pattern.
        /// Returns an empty dictionary if <paramref name="ruleString"/> is null or whitespace.
        /// Throws <see cref="FormatException"/> if the string is non-empty but malformed.
        /// </summary>
        public static Dictionary<string, string> Parse(string? ruleString)
        {
            var rulesByCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (ruleString == null || string.IsNullOrWhiteSpace(ruleString))
                return rulesByCategory;

            int position = 0;
            skipWhitespace(ruleString, ref position);

            expectCharacter(ruleString, ref position, '{');

            while (position < ruleString.Length)
            {
                skipWhitespace(ruleString, ref position);

                if (position < ruleString.Length && ruleString[position] == '}')
                {
                    position++;
                    break;
                }

                // Allow trailing comma before closing brace
                if (rulesByCategory.Count > 0)
                {
                    expectCharacter(ruleString, ref position, ',');
                    skipWhitespace(ruleString, ref position);

                    // Handle trailing comma: { event = "On.*", }
                    if (position < ruleString.Length && ruleString[position] == '}')
                    {
                        position++;
                        break;
                    }
                }

                string categoryKey = readIdentifier(ruleString, ref position);
                skipWhitespace(ruleString, ref position);
                expectCharacter(ruleString, ref position, '=');
                skipWhitespace(ruleString, ref position);
                string regexPatternValue = readQuotedString(ruleString, ref position);

                rulesByCategory[categoryKey] = regexPatternValue;
            }

            return rulesByCategory;
        }

        private static void skipWhitespace(string ruleString, ref int position)
        {
            while (position < ruleString.Length && char.IsWhiteSpace(ruleString[position]))
                position++;
        }

        private static void expectCharacter(string ruleString, ref int position, char expectedCharacter)
        {
            if (position >= ruleString.Length)
                throw new FormatException(
                    $"EnforceNamingConventions: unexpected end of input at position {position}, expected '{expectedCharacter}'. " +
                    $"Input: \"{ruleString}\"");

            if (ruleString[position] != expectedCharacter)
                throw new FormatException(
                    $"EnforceNamingConventions: expected '{expectedCharacter}' at position {position}, " +
                    $"found '{ruleString[position]}'. Input: \"{ruleString}\"");

            position++;
        }

        private static string readIdentifier(string ruleString, ref int position)
        {
            int identifierStart = position;

            while (position < ruleString.Length &&
                   (char.IsLetterOrDigit(ruleString[position]) || ruleString[position] == '_'))
            {
                position++;
            }

            if (position == identifierStart)
            {
                string contextFragment = position < ruleString.Length
                    ? $"found '{ruleString[position]}'"
                    : "found end of input";
                throw new FormatException(
                    $"EnforceNamingConventions: expected identifier at position {position}, {contextFragment}. " +
                    $"Input: \"{ruleString}\"");
            }

            return ruleString.Substring(identifierStart, position - identifierStart);
        }

        private static string readQuotedString(string ruleString, ref int position)
        {
            expectCharacter(ruleString, ref position, '"');

            int valueStart = position;

            while (position < ruleString.Length && ruleString[position] != '"')
            {
                // Support backslash escaping inside quoted strings
                if (ruleString[position] == '\\' && position + 1 < ruleString.Length)
                {
                    position += 2;
                    continue;
                }
                position++;
            }

            if (position >= ruleString.Length)
                throw new FormatException(
                    $"EnforceNamingConventions: unterminated string starting at position {valueStart - 1}. " +
                    $"Input: \"{ruleString}\"");

            string quotedValue = ruleString.Substring(valueStart, position - valueStart);
            position++; // skip closing quote

            return quotedValue;
        }
    }
}