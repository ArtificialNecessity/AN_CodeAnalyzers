using System.Threading.Tasks;
using Xunit;

namespace AN.CodeAnalyzers.Tests.EnforceNamingConventions
{
    public class EnforceNamingConventionsAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Event matches pattern → no diagnostic
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Event_MatchesOnPattern_NoDiagnostic()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler OnClick;
    public event EventHandler OnLoad;
    public event EventHandler OnClose;
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateNoDiagnosticsTest(
                testSource,
                namingConventionsConfig: @"{ event = ""On.*"" }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Event does NOT match pattern → AN0200
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Event_DoesNotMatchOnPattern_ReportsDiagnostic()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler {|#0:ButtonClick|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "ButtonClick", "On.*"),
                },
                namingConventionsConfig: @"{ event = ""On.*"" }");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Event_MultipleViolations_ReportsAll()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler OnClick;
    public event EventHandler {|#0:ButtonClick|};
    public event EventHandler OnLoad;
    public event EventHandler {|#1:WindowClosed|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "ButtonClick", "On.*"),
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        1, "Event", "WindowClosed", "On.*"),
                },
                namingConventionsConfig: @"{ event = ""On.*"" }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // No config → disabled, no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task NoConfig_NoDiagnostic()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler ButtonClick;
    public event EventHandler WindowClosed;
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateNoDiagnosticsTest(
                testSource,
                namingConventionsConfig: "");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Multiple rules — only event rule applies to events
        // ──────────────────────────────────────────────

        [Fact]
        public async Task MultipleRules_OnlyEventRuleApplied()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler OnClick;
    public event EventHandler {|#0:BadName|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "BadName", "On.*"),
                },
                namingConventionsConfig: @"{ event = ""On.*"", interface = ""I.*"" }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Different regex patterns
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Event_SuffixPattern_MatchesCorrectly()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler ClickChanged;
    public event EventHandler {|#0:OnClick|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "OnClick", ".*Changed"),
                },
                namingConventionsConfig: @"{ event = "".*Changed"" }");

            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task Event_ExactNamePattern_MatchesCorrectly()
        {
            const string testSource = @"
using System;
#pragma warning disable CS0067
public class MyClass
{
    public event EventHandler Clicked;
    public event EventHandler {|#0:OnClick|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "OnClick", "Clicked"),
                },
                namingConventionsConfig: @"{ event = ""Clicked"" }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Event in interface — still checked
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Event_InInterface_StillChecked()
        {
            const string testSource = @"
using System;
public interface IMyInterface
{
    event EventHandler {|#0:ButtonClick|};
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    EnforceNamingConventionsVerifierHelper.ExpectNamingViolation(
                        0, "Event", "ButtonClick", "On.*"),
                },
                namingConventionsConfig: @"{ event = ""On.*"" }");

            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Clean code — no events, no diagnostics
        // ──────────────────────────────────────────────

        [Fact]
        public async Task NoEvents_NoDiagnostic()
        {
            const string testSource = @"
public class MyClass
{
    public int Count { get; set; }
    public void DoWork() { }
}";
            var analyzerTest = EnforceNamingConventionsVerifierHelper.CreateNoDiagnosticsTest(
                testSource,
                namingConventionsConfig: @"{ event = ""On.*"" }");

            await analyzerTest.RunAsync();
        }
    }
}