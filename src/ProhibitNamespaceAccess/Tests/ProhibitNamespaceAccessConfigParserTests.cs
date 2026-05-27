using System;
using AN.CodeAnalyzers.ProhibitNamespaceAccess;
using Xunit;

namespace AN.CodeAnalyzers.Tests.ProhibitNamespaceAccess
{
    public class ProhibitNamespaceAccessConfigParserTests
    {
        [Fact]
        public void Parse_NullInput_ReturnsEmptyConfig()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(null);
            Assert.False(parsedConfig.HasAnyPatterns);
        }

        [Fact]
        public void Parse_EmptyString_ReturnsEmptyConfig()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse("");
            Assert.False(parsedConfig.HasAnyPatterns);
        }

        [Fact]
        public void Parse_WhitespaceOnly_ReturnsEmptyConfig()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse("   ");
            Assert.False(parsedConfig.HasAnyPatterns);
        }

        [Fact]
        public void Parse_SingleErrorPattern_Parsed()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.Runtime.InteropServices"" ] }");

            Assert.Single(parsedConfig.ErrorPatterns);
            Assert.Equal("System.Runtime.InteropServices", parsedConfig.ErrorPatterns[0].OriginalPattern);
            Assert.False(parsedConfig.ErrorPatterns[0].IsGlob);
            Assert.Empty(parsedConfig.WarnPatterns);
        }

        [Fact]
        public void Parse_SingleWarnPattern_Parsed()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ warn = [ ""OpenTK"" ] }");

            Assert.Empty(parsedConfig.ErrorPatterns);
            Assert.Single(parsedConfig.WarnPatterns);
            Assert.Equal("OpenTK", parsedConfig.WarnPatterns[0].OriginalPattern);
        }

        [Fact]
        public void Parse_MultipleErrorPatterns_AllParsed()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.Runtime.InteropServices"", ""System.IO.MemoryMappedFiles"" ] }");

            Assert.Equal(2, parsedConfig.ErrorPatterns.Count);
            Assert.Equal("System.Runtime.InteropServices", parsedConfig.ErrorPatterns[0].OriginalPattern);
            Assert.Equal("System.IO.MemoryMappedFiles", parsedConfig.ErrorPatterns[1].OriginalPattern);
        }

        [Fact]
        public void Parse_ErrorAndWarnPatterns_BothParsed()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.Runtime.InteropServices"" ], warn = [ ""OpenTK.*"" ] }");

            Assert.Single(parsedConfig.ErrorPatterns);
            Assert.Single(parsedConfig.WarnPatterns);
            Assert.Equal("System.Runtime.InteropServices", parsedConfig.ErrorPatterns[0].OriginalPattern);
            Assert.Equal("OpenTK.*", parsedConfig.WarnPatterns[0].OriginalPattern);
            Assert.True(parsedConfig.WarnPatterns[0].IsGlob);
        }

        [Fact]
        public void Parse_GlobPattern_PrefixExtracted()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.Runtime.Interop*"" ] }");

            var globPattern = parsedConfig.ErrorPatterns[0];
            Assert.True(globPattern.IsGlob);
            Assert.Equal("System.Runtime.Interop*", globPattern.OriginalPattern);
            Assert.Equal("System.Runtime.Interop", globPattern.MatchPrefix);
        }

        [Fact]
        public void Parse_TrailingCommaInArray_Accepted()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.IO"", ] }");

            Assert.Single(parsedConfig.ErrorPatterns);
            Assert.Equal("System.IO", parsedConfig.ErrorPatterns[0].OriginalPattern);
        }

        [Fact]
        public void Parse_TrailingCommaInObject_Accepted()
        {
            var parsedConfig = ProhibitNamespaceAccessConfigParser.Parse(
                @"{ error = [ ""System.IO"" ], }");

            Assert.Single(parsedConfig.ErrorPatterns);
        }

        [Fact]
        public void Parse_UnknownSeverityKey_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                ProhibitNamespaceAccessConfigParser.Parse(@"{ fatal = [ ""System.IO"" ] }"));
        }

        [Fact]
        public void Parse_MissingOpenBrace_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                ProhibitNamespaceAccessConfigParser.Parse(@"error = [ ""System.IO"" ]"));
        }

        [Fact]
        public void Parse_MissingOpenBracket_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                ProhibitNamespaceAccessConfigParser.Parse(@"{ error = ""System.IO"" }"));
        }

        // ──────────────────────────────────────────────
        // NamespacePattern matching tests
        // ──────────────────────────────────────────────

        [Fact]
        public void NamespacePattern_ExactMatch_MatchesExactNamespace()
        {
            var exactPattern = new NamespacePattern("System.Runtime.InteropServices");
            Assert.True(exactPattern.MatchesNamespace("System.Runtime.InteropServices"));
        }

        [Fact]
        public void NamespacePattern_ExactMatch_DoesNotMatchChildNamespace()
        {
            var exactPattern = new NamespacePattern("System.Runtime.InteropServices");
            Assert.False(exactPattern.MatchesNamespace("System.Runtime.InteropServices.Marshalling"));
        }

        [Fact]
        public void NamespacePattern_ExactMatch_DoesNotMatchParentNamespace()
        {
            var exactPattern = new NamespacePattern("System.Runtime.InteropServices");
            Assert.False(exactPattern.MatchesNamespace("System.Runtime"));
        }

        [Fact]
        public void NamespacePattern_GlobWithDotStar_MatchesChildNamespaces()
        {
            var globPattern = new NamespacePattern("System.Runtime.*");
            Assert.True(globPattern.MatchesNamespace("System.Runtime.InteropServices"));
            Assert.True(globPattern.MatchesNamespace("System.Runtime.CompilerServices"));
            Assert.True(globPattern.MatchesNamespace("System.Runtime.InteropServices.Marshalling"));
        }

        [Fact]
        public void NamespacePattern_GlobWithDotStar_DoesNotMatchExactNamespace()
        {
            // "System.Runtime.*" should NOT match "System.Runtime" itself
            // because the prefix is "System.Runtime." and "System.Runtime" doesn't start with "System.Runtime."
            var globPattern = new NamespacePattern("System.Runtime.*");
            Assert.False(globPattern.MatchesNamespace("System.Runtime"));
        }

        [Fact]
        public void NamespacePattern_GlobWithPartialName_MatchesPrefixedNamespaces()
        {
            var globPattern = new NamespacePattern("System.Runtime.Interop*");
            Assert.True(globPattern.MatchesNamespace("System.Runtime.InteropServices"));
            Assert.True(globPattern.MatchesNamespace("System.Runtime.InteropServices.Marshalling"));
            Assert.True(globPattern.MatchesNamespace("System.Runtime.InteropStuffNotInventedYet"));
        }

        [Fact]
        public void NamespacePattern_GlobWithPartialName_DoesNotMatchUnrelatedNamespace()
        {
            var globPattern = new NamespacePattern("System.Runtime.Interop*");
            Assert.False(globPattern.MatchesNamespace("System.Runtime.CompilerServices"));
            Assert.False(globPattern.MatchesNamespace("System.Runtime"));
        }
    }
}