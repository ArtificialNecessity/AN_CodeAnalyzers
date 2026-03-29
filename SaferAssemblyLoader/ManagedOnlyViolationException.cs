using System;
using System.Collections.Generic;

namespace ArtificialNecessity.SaferAssemblyLoader
{
    /// <summary>
    /// Thrown when an assembly fails the managed-only inspection.
    /// Contains the full list of violations found in the assembly metadata.
    /// The assembly was NEVER loaded into the runtime when this exception is thrown.
    /// </summary>
    public sealed class ManagedOnlyViolationException : Exception
    {
        /// <summary>Every violation found, one string per violation.</summary>
        public IReadOnlyList<string> Violations { get; }

        /// <summary>The path or name of the assembly that failed inspection.</summary>
        public string AssemblyName { get; }

        public ManagedOnlyViolationException(string assemblyName, IReadOnlyList<string> violations)
            : base(FormatMessage(assemblyName, violations))
        {
            AssemblyName = assemblyName;
            Violations = violations;
        }

        private static string FormatMessage(string assemblyName, IReadOnlyList<string> violations)
        {
            var messageBuilder = new System.Text.StringBuilder();
            messageBuilder.Append($"Assembly '{assemblyName}' is not managed-only ({violations.Count} violation{(violations.Count == 1 ? "" : "s")}):");
            foreach (string violation in violations)
            {
                messageBuilder.Append($"\n  {violation}");
            }
            return messageBuilder.ToString();
        }
    }
}