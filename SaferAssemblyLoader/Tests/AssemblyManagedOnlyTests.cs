using System;
using System.IO;
using Xunit;

namespace ArtificialNecessity.SaferAssemblyLoader.Tests
{
    public class AssemblyManagedOnlyTests : IDisposable
    {
        private readonly string _testOutputDirectory;

        public AssemblyManagedOnlyTests()
        {
            _testOutputDirectory = Path.Combine(Path.GetTempPath(), "SaferAssemblyLoaderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testOutputDirectory);
        }

        public void Dispose()
        {
            // Best-effort cleanup — Assembly.LoadFrom locks DLL files,
            // so some files may not be deletable until process exit.
            try { Directory.Delete(_testOutputDirectory, recursive: true); }
            catch (UnauthorizedAccessException) { /* locked by loaded assemblies */ }
            catch (IOException) { /* locked by loaded assemblies */ }
        }

        // ──────────────────────────────────────────────
        // IsManagedOnly
        // ──────────────────────────────────────────────

        [Fact]
        public void IsManagedOnly_ReturnsTrueForCleanAssembly()
        {
            string cleanDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.CleanManagedSource, _testOutputDirectory, assemblyName: "CleanForCheck");

            bool isManagedOnly = AssemblyManagedOnly.IsManagedOnly(cleanDllPath);

            Assert.True(isManagedOnly);
        }

        [Fact]
        public void IsManagedOnly_ReturnsFalseForDllImportAssembly()
        {
            string dirtyDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.DllImportSource, _testOutputDirectory, assemblyName: "DirtyForCheck");

            bool isManagedOnly = AssemblyManagedOnly.IsManagedOnly(dirtyDllPath);

            Assert.False(isManagedOnly);
        }

        // ──────────────────────────────────────────────
        // GetViolations
        // ──────────────────────────────────────────────

        [Fact]
        public void GetViolations_ReturnsEmptyForCleanAssembly()
        {
            string cleanDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.CleanManagedSource, _testOutputDirectory, assemblyName: "CleanForViolations");

            var violations = AssemblyManagedOnly.GetViolations(cleanDllPath);

            Assert.Empty(violations);
        }

        [Fact]
        public void GetViolations_ReturnsViolationsForDirtyAssembly()
        {
            string dirtyDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.DllImportSource, _testOutputDirectory, assemblyName: "DirtyForViolations");

            var violations = AssemblyManagedOnly.GetViolations(dirtyDllPath);

            Assert.NotEmpty(violations);
            Assert.Contains(violations, violation => violation.Contains("DllImport"));
        }

        // ──────────────────────────────────────────────
        // LoadFrom — clean assembly
        // ──────────────────────────────────────────────

        [Fact]
        public void LoadFrom_SucceedsForCleanAssembly()
        {
            string cleanDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.CleanManagedSource, _testOutputDirectory, assemblyName: "CleanForLoad");

            var loadedAssembly = AssemblyManagedOnly.LoadFrom(cleanDllPath);

            Assert.NotNull(loadedAssembly);
            Assert.Equal("CleanForLoad", loadedAssembly.GetName().Name);
        }

        // ──────────────────────────────────────────────
        // LoadFrom — dirty assembly throws
        // ──────────────────────────────────────────────

        [Fact]
        public void LoadFrom_ThrowsForDllImportAssembly()
        {
            string dirtyDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.DllImportSource, _testOutputDirectory, assemblyName: "DirtyForLoad");

            var thrownException = Assert.Throws<ManagedOnlyViolationException>(
                () => AssemblyManagedOnly.LoadFrom(dirtyDllPath));

            Assert.NotEmpty(thrownException.Violations);
            Assert.Contains("DirtyForLoad.dll", thrownException.AssemblyName);
            Assert.Contains("violation", thrownException.Message.ToLowerInvariant());
        }

        // ──────────────────────────────────────────────
        // Load(byte[]) — clean assembly
        // ──────────────────────────────────────────────

        [Fact]
        public void LoadBytes_SucceedsForCleanAssembly()
        {
            byte[] cleanDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.CleanManagedSource, assemblyName: "CleanForByteLoad");

            var loadedAssembly = AssemblyManagedOnly.Load(cleanDllBytes);

            Assert.NotNull(loadedAssembly);
            Assert.Equal("CleanForByteLoad", loadedAssembly.GetName().Name);
        }

        // ──────────────────────────────────────────────
        // Load(byte[]) — dirty assembly throws
        // ──────────────────────────────────────────────

        [Fact]
        public void LoadBytes_ThrowsForDllImportAssembly()
        {
            byte[] dirtyDllBytes = TestAssemblyCompiler.CompileToDllBytes(
                TestAssemblyCompiler.DllImportSource, assemblyName: "DirtyForByteLoad");

            var thrownException = Assert.Throws<ManagedOnlyViolationException>(
                () => AssemblyManagedOnly.Load(dirtyDllBytes));

            Assert.NotEmpty(thrownException.Violations);
            Assert.Contains("violation", thrownException.Message.ToLowerInvariant());
        }

        // ──────────────────────────────────────────────
        // Exception message format
        // ──────────────────────────────────────────────

        [Fact]
        public void Exception_MessageContainsViolationCount()
        {
            string dirtyDllPath = TestAssemblyCompiler.CompileToDllFile(
                TestAssemblyCompiler.DllImportSource, _testOutputDirectory, assemblyName: "DirtyForMessage");

            var thrownException = Assert.Throws<ManagedOnlyViolationException>(
                () => AssemblyManagedOnly.LoadFrom(dirtyDllPath));

            // Message should contain the violation count
            Assert.Contains($"{thrownException.Violations.Count} violation", thrownException.Message);
        }
    }
}