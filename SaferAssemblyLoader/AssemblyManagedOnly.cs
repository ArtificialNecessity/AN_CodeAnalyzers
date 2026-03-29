using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ArtificialNecessity.SaferAssemblyLoader
{
    /// <summary>
    /// Loads .NET assemblies with a managed-only guarantee.
    /// Inspects the assembly metadata WITHOUT loading it into the runtime.
    /// If the assembly contains any unmanaged code surface, throws before loading.
    /// If it loads, it's clean.
    /// </summary>
    public static class AssemblyManagedOnly
    {
        /// <summary>
        /// Load an assembly from disk. Inspects PE metadata first.
        /// If the assembly is managed-only, loads and returns it.
        /// If not, throws ManagedOnlyViolationException listing every violation.
        /// The assembly is NEVER loaded if it fails inspection.
        /// </summary>
        public static Assembly LoadFrom(string assemblyPath)
        {
            var inspectionResult = ManagedAssemblyInspector.Inspect(assemblyPath);
            if (!inspectionResult.IsManagedOnly)
            {
                string assemblyFileName = Path.GetFileName(assemblyPath);
                throw new ManagedOnlyViolationException(assemblyFileName, inspectionResult.Violations);
            }

            return Assembly.LoadFrom(assemblyPath);
        }

        /// <summary>
        /// Load an assembly from a byte array. Same guarantees.
        /// Inspects PE metadata first, throws if not managed-only.
        /// </summary>
        public static Assembly Load(byte[] rawAssemblyBytes)
        {
            var inspectionResult = ManagedAssemblyInspector.Inspect(rawAssemblyBytes);
            if (!inspectionResult.IsManagedOnly)
            {
                throw new ManagedOnlyViolationException("<byte[]>", inspectionResult.Violations);
            }

            return Assembly.Load(rawAssemblyBytes);
        }

        /// <summary>
        /// Inspect without loading. Returns true if managed-only.
        /// If you need the violation list without loading, use GetViolations instead.
        /// </summary>
        public static bool IsManagedOnly(string assemblyPath)
        {
            var inspectionResult = ManagedAssemblyInspector.Inspect(assemblyPath);
            return inspectionResult.IsManagedOnly;
        }

        /// <summary>
        /// Inspect without loading. Returns the violation list.
        /// Empty list = managed-only.
        /// </summary>
        public static IReadOnlyList<string> GetViolations(string assemblyPath)
        {
            var inspectionResult = ManagedAssemblyInspector.Inspect(assemblyPath);
            return inspectionResult.Violations;
        }
    }
}