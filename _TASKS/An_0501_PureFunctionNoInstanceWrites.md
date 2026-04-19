# AN0501 - PureFunctionNoInstanceWrites Analyzer

## Problem

LLMs keep putting state mutation logic in `Draw()` and other methods that must be side-effect-free. This violates the render architecture (Draw is a pure function of current state) and causes bugs that are hard to trace.

## Solution

A `[PureFunction]` attribute and a Roslyn analyzer that flags any instance state mutation inside methods marked with it. Compile-time enforcement. The LLM sees the error immediately via Roslyn diagnostics.

## Attribute

```csharp
namespace AN.CodeAnalyzers;

[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class PureFunctionAttribute : Attribute { }
```

`Inherited = true` is critical â€” `FView.Draw()` is marked `[PureFunction]`, every override inherits the constraint automatically. The LLM doesn't need to remember to annotate overrides.

## What the Analyzer Flags

### AN0002: Instance state mutation in [PureFunction] method

**Error** (not warning â€” this is a correctness violation, not style):

| Mutation Type | Example | Flagged? |
|---|---|---|
| Field assignment | `_foo = 5` | YES |
| Field compound assignment | `_count += 1` | YES |
| Field increment/decrement | `_counter++` | YES |
| Property setter | `Visible = false` | YES |
| `this.field = x` | `this._cache = data` | YES |
| Local variable assignment | `var x = 5; x = 6;` | NO â€” locals are fine |
| Parameter assignment | `param = 3` | NO â€” parameters are fine |
| Static field assignment | `s_globalCount++` | NO â€” not instance state (debatable, but keep it simple) |
| Method calls on instance | `DoLayout()` | NO â€” not checked transitively |
| Method calls on fields | `_list.Add(x)` | NO â€” too deep for v1 |
| `ref`/`out` instance field | `SomeMethod(ref _field)` | YES â€” mutation by proxy |
| Event raise | `StateChanged?.Invoke()` | NO â€” raising is not mutating |

### Diagnostic Message

```
AN0002: Method 'Draw' is marked [PureFunction] and must not modify instance state. 
        Assignment to instance field '_isMouseDragSelecting' is not allowed here.
```

## Detection Logic

1. Register `OperationAction` on the method body of any method that has `[PureFunction]` (directly or inherited from base)
2. Walk all operations in the method body
3. For each `IAssignmentOperation`, `IIncrementOrDecrementOperation`, `ICompoundAssignmentOperation`:
   - Check if the target is an `IFieldReferenceOperation` or `IPropertyReferenceOperation` where `Instance` is `this` (implicit or explicit)
   - If yes â†’ report AN0002
4. For each `IArgumentOperation` with `ref`/`out` parameter:
   - Check if the value is an instance field reference
   - If yes â†’ report AN0002

## Usage

```csharp
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
        _needsRecalc = false;  // AN0002: Assignment to '_needsRecalc' in [PureFunction]
        
        var localTemp = 5;
        localTemp = 6;         // Fine â€” local variable
        
        DrawLines(dc);         // Fine â€” method call not checked transitively
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

Add to existing `AN.CodeAnalyzers` project alongside AN0001 ExplicitEnums.

| File | Content |
|---|---|
| `PureFunctionAttribute.cs` | The attribute class |
| `PureFunctionAnalyzer.cs` | The DiagnosticAnalyzer (~150 lines) |
| `PureFunctionAnalyzerTests.cs` | Unit tests for each mutation type |

## Future (v2)

- Optional transitive checking via `[PureFunction(Transitive = true)]` â€” also flag calls to methods NOT marked `[PureFunction]`
- Flag `event += handler` subscription changes in pure methods
- Flag static field mutation (currently allowed)