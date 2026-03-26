using Xunit;

namespace AN.CodeAnalyzers.ClassLibInfo.Tests
{
    public class ApiDumpGeneratorTests
    {
        [Fact]
        public void GenerateApiDump_EmptyAssembly_ReturnsEmptyList()
        {
            // TODO: Phase 6 — compile a minimal test assembly and verify output
            // For now, just verify the placeholder returns empty
            var emptyResult = ApiDumpGenerator.GenerateApiDump("nonexistent.dll", "public");
            Assert.Empty(emptyResult);
        }
    }
}