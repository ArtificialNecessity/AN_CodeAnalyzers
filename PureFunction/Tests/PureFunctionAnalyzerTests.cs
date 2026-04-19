using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AN.CodeAnalyzers.Tests.PureFunction
{
    public class PureFunctionAnalyzerTests
    {
        // ──────────────────────────────────────────────────
        // Flagged: field assignment
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task FieldAssignment_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _foo;

    [PureFunction]
    public void Render()
    {
        {|#0:_foo = 5|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_foo"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: compound assignment
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task CompoundAssignment_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _count;

    [PureFunction]
    public void Render()
    {
        {|#0:_count += 1|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_count"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: increment / decrement
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task FieldIncrement_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _counter;

    [PureFunction]
    public void Render()
    {
        {|#0:_counter++|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_counter"),
                });
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task FieldDecrement_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _counter;

    [PureFunction]
    public void Render()
    {
        {|#0:_counter--|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_counter"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: property setter
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task PropertySetter_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    public bool Visible { get; set; }

    [PureFunction]
    public void Render()
    {
        {|#0:Visible = false|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "property", "Visible"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: explicit this
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task ExplicitThis_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private object _cache;

    [PureFunction]
    public void Render()
    {
        {|#0:this._cache = null|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_cache"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: ref/out instance field
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task RefInstanceField_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _value;

    [PureFunction]
    public void Render()
    {
        Mutate({|#0:ref _value|});
    }

    private static void Mutate(ref int x) { x = 42; }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_value"),
                });
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task OutInstanceField_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _value;

    [PureFunction]
    public void Render()
    {
        TryGet({|#0:out _value|});
    }

    private static bool TryGet(out int x) { x = 0; return true; }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_value"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: inherited attribute via override chain
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task InheritedAttribute_Override_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public abstract class BaseView
{
    [PureFunction]
    public virtual void Draw() { }
}

public class DerivedView : BaseView
{
    private bool _dirty;

    public override void Draw()
    {
        {|#0:_dirty = false|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Draw", "field", "_dirty"),
                });
            await analyzerTest.RunAsync();
        }

        [Fact]
        public async Task InheritedAttribute_MultiLevel_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public abstract class BaseView
{
    [PureFunction]
    public virtual void Draw() { }
}

public class MiddleView : BaseView
{
    public override void Draw() { }
}

public class LeafView : MiddleView
{
    private int _state;

    public override void Draw()
    {
        {|#0:_state = 1|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Draw", "field", "_state"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: multiple mutations in one method
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task MultipleMutations_EachGetsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _a;
    private int _b;
    public bool Visible { get; set; }

    [PureFunction]
    public void Render()
    {
        {|#0:_a = 1|};
        {|#1:_b += 2|};
        {|#2:Visible = true|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_a"),
                    PureFunctionVerifierHelper.ExpectAN0501Error(1, "Render", "field", "_b"),
                    PureFunctionVerifierHelper.ExpectAN0501Error(2, "Render", "property", "Visible"),
                });
            await analyzerTest.RunAsync();
        }

        // ──────────────────────────────────────────────────
        // Flagged: lambda inside [PureFunction] writes field
        // ──────────────────────────────────────────────────

        [Fact]
        public async Task LambdaWritesInstanceField_InPureFunction_ReportsDiagnostic()
        {
            const string testSource = @"
using System;
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private int _value;

    [PureFunction]
    public void Render()
    {
        Action a = () => {|#0:_value = 99|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Render", "field", "_value"),
                });
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: local variable assignment
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task LocalVariable_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    [PureFunction]
    public void Render()
    {
        var x = 5;
        x = 6;
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: parameter assignment
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task ParameterAssignment_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    [PureFunction]
    public void Render(int param)
    {
        param = 3;
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: static field assignment
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task StaticFieldAssignment_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private static int s_globalCount;

    [PureFunction]
    public void Render()
    {
        s_globalCount++;
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: instance method call (no transitive check)
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task InstanceMethodCall_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    [PureFunction]
    public void Render()
    {
        DoLayout();
    }

    private void DoLayout() { }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: method call on field
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task FieldMethodCall_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using System.Collections.Generic;
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    private List<int> _list = new List<int>();

    [PureFunction]
    public void Render()
    {
        _list.Add(42);
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: event raise
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task EventRaise_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using System;
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    public event Action StateChanged;

    [PureFunction]
    public void Render()
    {
        StateChanged?.Invoke();
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: no [PureFunction] attribute
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task NoAttribute_SameMutations_NoDiagnostic()
        {
            const string testSource = @"
public class TestClass
{
    private int _foo;
    public bool Visible { get; set; }

    public void Render()
    {
        _foo = 5;
        _foo += 1;
        _foo++;
        Visible = false;
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // NOT flagged: lambda mutating a local
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task LambdaMutatesLocal_InPureFunction_NoDiagnostic()
        {
            const string testSource = @"
using System;
using AN.CodeAnalyzers.PureFunction;

public class TestClass
{
    [PureFunction]
    public void Render()
    {
        int captured = 0;
        Action a = () => captured = 1;
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateNoDiagnosticsTest(testSource);
            await analyzerTest.RunAsync();
        }

        // ══════════════════════════════════════════════════
        // Interface: implicit implementation inherits constraint
        // ══════════════════════════════════════════════════

        [Fact]
        public async Task InterfaceAttribute_ImplicitImpl_ReportsDiagnostic()
        {
            const string testSource = @"
using AN.CodeAnalyzers.PureFunction;

public interface IRenderer
{
    [PureFunction]
    void Draw();
}

public class MyRenderer : IRenderer
{
    private int _state;

    public void Draw()
    {
        {|#0:_state = 1|};
    }
}";
            var analyzerTest = PureFunctionVerifierHelper.CreateDiagnosticsTest(
                testSource,
                new[]
                {
                    PureFunctionVerifierHelper.ExpectAN0501Error(0, "Draw", "field", "_state"),
                });
            await analyzerTest.RunAsync();
        }
    }
}