using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.CallersMustNameAllParameters
{
    public class CallersMustNameAllParametersAnalyzerTests
    {
        // ──────────────────────────────────────────────
        // Attribute mode (default) — 2+ parameter methods
        // ──────────────────────────────────────────────

        [Fact]
        public async Task AttributeMode_TwoParams_AllNamed_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues(width: 10, height: 20);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task AttributeMode_TwoParams_OneUnnamed_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues(width: 10, {|#0:20|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 2, "SetValues"),
                },
                enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task AttributeMode_ThreeParams_AllUnnamed_ReportsThreeDiagnostics()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetMargin(float vertical, float horizontal, int unit) { }

    public void Caller()
    {
        SetMargin({|#0:4|}, {|#1:8|}, {|#2:1|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 1, "SetMargin"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(1, 2, "SetMargin"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(2, 3, "SetMargin"),
                },
                enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task AttributeMode_MethodWithoutAttribute_Unnamed_NoDiagnostic()
        {
            const string testSource = @"
public class TestClass
{
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues(10, 20);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task AttributeMode_Constructor_Unnamed_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public TestClass(int width, int height) { }

    public static void Caller()
    {
        var obj = new TestClass({|#0:10|}, {|#1:20|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 1, ".ctor"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(1, 2, ".ctor"),
                },
                enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Single-parameter methods — always exempt
        // ──────────────────────────────────────────────

        [Fact]
        public async Task AttributeMode_SingleParam_WithAttribute_Unnamed_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetValue(int value) { }

    public void Caller()
    {
        SetValue(42);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task EverywhereMode_SingleParam_Unnamed_NoDiagnostic()
        {
            const string testSource = @"
public class TestClass
{
    public void SetValue(int value) { }

    public void Caller()
    {
        SetValue(42);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "everywhere-error");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task ZeroParams_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void DoWork() { }

    public void Caller()
    {
        DoWork();
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Params array — exempt
        // ──────────────────────────────────────────────

        [Fact]
        public async Task AttributeMode_ParamsArray_OnlyParamsUnnamed_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void Log(string format, params object[] args) { }

    public void Caller()
    {
        Log(format: ""test"", 1, 2, 3);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "attribute-error");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Everywhere mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task EverywhereWarn_TwoParams_Unnamed_ReportsWarnings()
        {
            const string testSource = @"
public class TestClass
{
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues({|#0:10|}, {|#1:20|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Warning(0, 1, "SetValues"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Warning(1, 2, "SetValues"),
                },
                enforcementMode: "everywhere-warn");
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task EverywhereError_TwoParams_Unnamed_ReportsErrors()
        {
            const string testSource = @"
public class TestClass
{
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues({|#0:10|}, {|#1:20|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 1, "SetValues"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(1, 2, "SetValues"),
                },
                enforcementMode: "everywhere-error");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Combined modes
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CombinedMode_AttributeError_EverywhereWarn()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void WithAttribute(int a, int b) { }

    public void WithoutAttribute(int x, int y) { }

    public void Caller()
    {
        WithAttribute({|#0:1|}, {|#1:2|});
        WithoutAttribute({|#2:3|}, {|#3:4|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 1, "WithAttribute"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(1, 2, "WithAttribute"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Warning(2, 1, "WithoutAttribute"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Warning(3, 2, "WithoutAttribute"),
                },
                enforcementMode: "attribute-error, everywhere-warn");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Ignore mode
        // ──────────────────────────────────────────────

        [Fact]
        public async Task IgnoreMode_NoDiagnostics()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues(10, 20);
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateNoDiagnosticsTest(
                testSource, enforcementMode: "ignore");
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────
        // Default mode (no MSBuild property)
        // ──────────────────────────────────────────────

        [Fact]
        public async Task DefaultMode_BehavesAsAttributeError()
        {
            const string testSource = @"
using AN.CodeAnalyzers.CallersMustNameAllParameters;

public class TestClass
{
    [CallersMustNameAllParameters]
    public void SetValues(int width, int height) { }

    public void Caller()
    {
        SetValues({|#0:10|}, {|#1:20|});
    }
}";
            var analyzerTest = CallersMustNameAllParametersVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(0, 1, "SetValues"),
                    CallersMustNameAllParametersVerifierHelper.ExpectAN0103Error(1, 2, "SetValues"),
                },
                enforcementMode: ""); // empty = default
            await analyzerTest.RunAsync();
        }
    }
}