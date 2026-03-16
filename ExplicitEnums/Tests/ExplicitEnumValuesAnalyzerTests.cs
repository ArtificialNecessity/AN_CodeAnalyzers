using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.ExplicitEnums
{
    public class ExplicitEnumValuesAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Default scope ("public") — public enums flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicEnum_WithoutExplicitValues_ReportsDiagnostic()
        {
            const string testSource = @"
public enum {|#0:Color|}
{
    {|#1:Red|},
    {|#2:Green|},
    {|#3:Blue|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(1, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(3, "Blue"),
                });

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicEnum_WithExplicitValues_NoDiagnostic()
        {
            const string testSource = @"
public enum Color
{
    Red = 0,
    Green = 1,
    Blue = 2
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicEnum_MixedValues_OnlyFlagsMissing()
        {
            const string testSource = @"
public enum Color
{
    Red = 0,
    {|#0:Green|},
    Blue = 2
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Green"),
                });

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Default scope ("public") — internal enums NOT flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task InternalEnum_WithoutExplicitValues_NoDiagnostic_DefaultScope()
        {
            const string testSource = @"
internal enum InternalColor
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PrivateEnum_WithoutExplicitValues_NoDiagnostic_DefaultScope()
        {
            const string testSource = @"
public class Outer
{
    private enum PrivateColor
    {
        Red,
        Green,
        Blue
    }
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Scope "all" — all enums flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task InternalEnum_WithoutExplicitValues_ReportsDiagnostic_ScopeAll()
        {
            const string testSource = @"
internal enum InternalColor
{
    {|#0:Red|},
    {|#1:Green|},
    {|#2:Blue|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(1, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Blue"),
                },
                enforcementScope: "all");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PrivateEnum_WithoutExplicitValues_ReportsDiagnostic_ScopeAll()
        {
            const string testSource = @"
public class Outer
{
    private enum PrivateColor
    {
        {|#0:Red|},
        {|#1:Green|},
        {|#2:Blue|}
    }
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(1, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Blue"),
                },
                enforcementScope: "all");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Scope "none" — nothing flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicEnum_WithoutExplicitValues_NoDiagnostic_ScopeNone()
        {
            const string testSource = @"
public enum Color
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementScope: "none");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Scope "explicit" — only [RequireExplicitEnumValues] flagged
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicEnum_WithoutAttribute_NoDiagnostic_ScopeExplicit()
        {
            const string testSource = @"
public enum Color
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementScope: "explicit");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicEnum_WithRequireAttribute_ReportsDiagnostic_ScopeExplicit()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[RequireExplicitEnumValues]
public enum Color
{
    {|#0:Red|},
    {|#1:Green|},
    {|#2:Blue|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(1, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Blue"),
                },
                enforcementScope: "explicit");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [SuppressExplicitEnumValues] — suppresses regardless of scope
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicEnum_WithSuppressAttribute_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[SuppressExplicitEnumValues]
public enum Color
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicEnum_WithSuppressAttribute_NoDiagnostic_ScopeAll()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[SuppressExplicitEnumValues]
public enum Color
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementScope: "all");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [RequireExplicitEnumValues] — forces enforcement regardless of scope
        // ──────────────────────────────────────────────

        [Fact]
        public async Task InternalEnum_WithRequireAttribute_ReportsDiagnostic_ScopePublic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[RequireExplicitEnumValues]
internal enum InternalColor
{
    {|#0:Red|},
    {|#1:Green|},
    {|#2:Blue|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(1, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Blue"),
                },
                enforcementScope: "public");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task InternalEnum_WithRequireAttribute_ReportsDiagnostic_ScopeNone()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[RequireExplicitEnumValues]
internal enum InternalColor
{
    {|#0:Red|},
    {|#1:Green|},
    {|#2:Blue|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Red"),
                    AnalyzerVerifierHelper.ExpectAN001(1, "Green"),
                    AnalyzerVerifierHelper.ExpectAN001(2, "Blue"),
                },
                enforcementScope: "none");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [SuppressExplicitEnumValues] takes precedence over [RequireExplicitEnumValues]
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicEnum_WithBothAttributes_SuppressWins()
        {
            const string testSource = @"
using AN.CodeAnalyzers.ExplicitEnums;

[SuppressExplicitEnumValues]
[RequireExplicitEnumValues]
public enum Color
{
    Red,
    Green,
    Blue
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Edge cases
        // ──────────────────────────────────────────────

        [Fact]
        public async Task EmptyEnum_NoDiagnostic()
        {
            const string testSource = @"
public enum Empty
{
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task SingleMember_WithExplicitValue_NoDiagnostic()
        {
            const string testSource = @"
public enum Single
{
    Only = 42
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task SingleMember_WithoutExplicitValue_ReportsDiagnostic()
        {
            const string testSource = @"
public enum Single
{
    {|#0:Only|}
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    AnalyzerVerifierHelper.ExpectAN001(0, "Only"),
                });

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task FlagsEnum_WithExplicitValues_NoDiagnostic()
        {
            const string testSource = @"
using System;

[Flags]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    All = Read | Write | Execute
}";
            var analyzerTest = AnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }
    }
}