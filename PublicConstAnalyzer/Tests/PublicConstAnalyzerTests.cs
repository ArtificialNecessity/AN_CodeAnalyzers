using System.Threading.Tasks;
using Xunit;

namespace AN.CodeAnalyzers.Tests.StableABIVerification
{
    public class PublicConstAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Public const fields — should warn
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicConst_Int_ReportsWarning()
        {
            const string testSource = @"
public class Config
{
    public const int {|#0:MaxRetries|} = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(0, "Config.MaxRetries"),
                });

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicConst_String_ReportsWarning()
        {
            const string testSource = @"
public class Config
{
    public const string {|#0:Version|} = ""2.0"";
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(0, "Config.Version"),
                });

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicConst_MultipleFields_ReportsAllWarnings()
        {
            const string testSource = @"
public class Config
{
    public const int {|#0:MaxRetries|} = 3;
    public const string {|#1:ApiUrl|} = ""https://api.example.com"";
    public const double {|#2:Timeout|} = 30.0;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(0, "Config.MaxRetries"),
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(1, "Config.ApiUrl"),
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(2, "Config.Timeout"),
                });

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Non-public const fields — should NOT warn
        // ──────────────────────────────────────────────

        [Fact]
        public async Task InternalConst_NoDiagnostic()
        {
            const string testSource = @"
public class Config
{
    internal const int MaxRetries = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PrivateConst_NoDiagnostic()
        {
            const string testSource = @"
public class Config
{
    private const int MaxRetries = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task ProtectedConst_NoDiagnostic()
        {
            const string testSource = @"
public class Config
{
    protected const int MaxRetries = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Public const in non-public type — should NOT warn
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicConst_InInternalClass_NoDiagnostic()
        {
            const string testSource = @"
internal class Config
{
    public const int MaxRetries = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicConst_InPrivateNestedClass_NoDiagnostic()
        {
            const string testSource = @"
public class Outer
{
    private class Inner
    {
        public const int MaxRetries = 3;
    }
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Public static readonly — should NOT warn (this is the fix)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicStaticReadonly_NoDiagnostic()
        {
            const string testSource = @"
public class Config
{
    public static readonly int MaxRetries = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Public const in nested public class — should warn
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicConst_InNestedPublicClass_ReportsWarning()
        {
            const string testSource = @"
public class Outer
{
    public class Inner
    {
        public const int {|#0:MaxRetries|} = 3;
    }
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(0, "Inner.MaxRetries"),
                });

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Enum const fields — should NOT warn (enum members are const but different)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task EnumMembers_NoDiagnostic()
        {
            const string testSource = @"
public enum Color
{
    Red = 0,
    Green = 1,
    Blue = 2
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // [PermanentConst] — suppresses AN0002
        // ──────────────────────────────────────────────

        [Fact]
        public async Task PublicConst_WithPermanentConstAttribute_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.StableABIVerification;

public class MathConstants
{
    [PermanentConst]
    public const double Pi = 3.14159265358979;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task PublicConst_MixedWithAndWithoutPermanentConst_OnlyWarnsUnsuppressed()
        {
            const string testSource = @"
using AN.CodeAnalyzers.StableABIVerification;

public class Config
{
    [PermanentConst]
    public const double Pi = 3.14159265358979;

    public const int {|#0:MaxRetries|} = 3;
}";
            var analyzerTest = PublicConstAnalyzerVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PublicConstAnalyzerVerifierHelper.ExpectAN0002(0, "Config.MaxRetries"),
                });

            await analyzerTest.RunAsync();
        }
    }
}