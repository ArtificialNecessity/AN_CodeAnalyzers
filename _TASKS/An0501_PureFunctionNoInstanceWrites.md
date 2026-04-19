# AN0501 — PureFunctionNoInstanceWrites Analyzer

## Problem

LLMs keep putting state mutation logic in `Draw()` and other methods that must be side-effect-free. This violates the render architecture (Draw is a pure function of current state) and causes bugs that are hard to trace.

## Solution

A `[PureFunction]` attribute and a Roslyn analyzer that flags any instance state mutation inside methods marked with it. Compile-time enforcement. The LLM sees the error immediately via Roslyn diagnostics.

## Attribute

```csharp
using System;

namespace AN.CodeAnalyzers.PureFunction
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class PureFunctionAttribute : Attribute { }
}
```

`Inherited = true` is critical — `FView.Draw()` is marked `[PureFunction]`, every override inherits the constraint automatically. The LLM doesn't need to remember to annotate overrides.

## What the Analyzer Flags

### AN0501: Instance state mutation in [PureFunction] method

**Error** (not warning — this is a correctness violation, not style):

| Mutation Type | Example | Flagged? |
|---|---|---|
| Field assignment | `_foo = 5` | YES |
| Field compound assignment | `_count += 1` | YES |
| Field increment/decrement | `_counter++` | YES |
| Property setter | `Visible = false` | YES |
| `this.field = x` | `this._cache = data` | YES |
| Local variable assignment | `var x = 5; x = 6;` | NO — locals are fine |
| Parameter assignment | `param = 3` | NO — parameters are fine |
| Static field assignment | `s_globalCount++` | NO — not instance state (debatable, but keep it simple) |
| Method calls on instance | `DoLayout()` | NO — not checked transitively |
| Method calls on fields | `_list.Add(x)` | NO — too deep for v1 |
| `ref`/`out` instance field | `SomeMethod(ref _field)` | YES — mutation by proxy |
| Event raise | `StateChanged?.Invoke()` | NO — raising is not mutating |

### Diagnostic Message

```
AN0501: Method 'Draw' is marked [PureFunction] and must not modify instance state.
        Assignment to instance field '_isMouseDragSelecting' is not allowed here.
```

## Detection Logic

1. Register `SymbolStartAction` on `SymbolKind.Method` to check if the method has `[PureFunction]` (directly or inherited from base via override chain walk)
2. Within that method, register `OperationAction` for assignment/increment operations
3. For each `ISimpleAssignmentOperation`, `ICompoundAssignmentOperation`, `IIncrementOrDecrementOperation`:
   - Check if the target is an `IFieldReferenceOperation` or `IPropertyReferenceOperation` where `Instance` is `IInstanceReferenceOperation` (i.e., `this`)
   - If yes → report AN0501
4. For each `IArgumentOperation` with `ref`/`out` parameter:
   - Check if the value is an instance field reference
   - If yes → report AN0501

**Note:** Roslyn's `GetAttributes()` only returns directly-applied attributes. `Inherited = true` is a runtime reflection concept. The analyzer must manually walk the `OverriddenMethod` chain to detect inherited `[PureFunction]`.

## Usage

```csharp
using AN.CodeAnalyzers.PureFunction;

public abstract class FView
{
    [PureFunction]
    public virtual void Draw(FDrawContext dc)
    {
        // Overrides inherit the constraint
    }
}

public class FCodeView : FView
{
    private bool _needsRecalc;

    public override void Draw(FDrawContext dc)
    {
        _needsRecalc = false;  // AN0501: Assignment to '_needsRecalc' in [PureFunction]

        var localTemp = 5;
        localTemp = 6;         // Fine — local variable

        DrawLines(dc);         // Fine — method call not checked transitively
    }
}
```

## Where to Apply [PureFunction]

| Method | Why |
|---|---|
| `FView.Draw()` | Render pass must be side-effect-free |
| `ITreeCellProvider.DrawCell()` | Rubber stamp rendering, no state |
| `FView.MeasureOverride()` | Layout measurement must not mutate |
| `FView.HitTest()` | Query method, no side effects |

Start with `Draw()` only. Add others incrementally as confidence grows.

## Implementation

Add to existing `AN.CodeAnalyzers` project following the per-analyzer directory convention.

| File | Content |
|---|---|
| `PureFunction/PureFunctionAttribute.cs` | The attribute class (sealed, `Inherited = true`) |
| `PureFunction/PureFunctionAnalyzer.cs` | The DiagnosticAnalyzer (~150-200 lines) |
| `PureFunction/Tests/AN.CodeAnalyzers.PureFunction.Tests.csproj` | Test project (xUnit + Roslyn Analyzer.Testing) |
| `PureFunction/Tests/PureFunctionVerifierHelper.cs` | Test helper (embeds attribute source, creates test instances) |
| `PureFunction/Tests/PureFunctionAnalyzerTests.cs` | Unit tests for each mutation type |

No MSBuild property needed — this analyzer is purely attribute-driven, always enabled.

## Future (v2)

- Optional transitive checking via `[PureFunction(Transitive = true)]` — also flag calls to methods NOT marked `[PureFunction]`
- Flag `event += handler` subscription changes in pure methods
- Flag static field mutation (currently allowed)