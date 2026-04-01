# AN0103 — CallersMustNameAllParameters Analyzer

## What

A Roslyn analyzer that enforces named parameters at call sites. Two modes:

1. **Attribute mode** (default): Methods/constructors decorated with `[CallersMustNameAllParameters]` require all arguments to be named at every call site.
2. **Everywhere mode** (joke/extreme): Every call site in the entire project must name every argument — Objective-C style.

No code fix provider. The error message tells the developer exactly what to do.

## Why

LLMs guess parameter order by vibes. A method like `ResolveHeight(float value)` gets called with whatever float is nearby. Named parameters make the intent visible at the call site and catch mismatches at compile time.

Sonnet's actual bug: called `ResolveHeight(availableHeight)` instead of `ResolveHeight(resolvedWidth)`. With this analyzer, it would have been forced to write `ResolveHeight(resolvedWidth: availableHeight)` — and the name mismatch is now a visible, obvious error even before the compiler catches it.

## Attribute

```csharp
namespace AN.CodeAnalyzers.CallersMustNameAllParameters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class CallersMustNameAllParametersAttribute : Attribute { }
}
```

Ships in the `ArtificialNecessity.CodeAnalyzers` NuGet alongside AN0100 and AN0101. The attribute is in the analyzers package so consumers get it automatically.

## MSBuild Property

```xml
<PropertyGroup>
  <RequireNamedArgumentsEverywhereLikeObjectiveC>attribute-error</RequireNamedArgumentsEverywhereLikeObjectiveC>
</PropertyGroup>
```

Supports a **comma-separated list** of values. Each value controls one behavior:

| Value | Behavior |
|-------|----------|
| `attribute-error` | Methods with `[CallersMustNameAllParameters]` require named args. Unnamed = **Error**. **(default)** |
| `attribute-warn` | Same as above but unnamed = **Warning** |
| `everywhere-error` | **Every** call site must name **every** argument. Unnamed = **Error**. |
| `everywhere-warn` | Same as above but unnamed = **Warning** |
| `ignore` | Disabled entirely — no diagnostics |

**Combining values:** `attribute-error, everywhere-warn` means attribute-decorated methods produce errors, and all other call sites produce warnings. If both `attribute-error` and `attribute-warn` appear, `error` wins. Same for `everywhere-*`.

**Default** (property absent or empty): `attribute-error` — only attribute-decorated methods are enforced, as errors.

## Analyzer

**Diagnostic ID:** AN0103  
**Category:** Naming  
**Severity:** Error or Warning (depends on config)  
**Title:** Method requires named parameters at call site  
**Message format:** `Argument {position} to '{methodName}' must be named. Use named arguments for all parameters, e.g. MyMethod(argA: 1, argB: 2)`

### Detection Logic

```
1. Read RequireNamedArgumentsEverywhereLikeObjectiveC MSBuild property
2. Parse comma-separated values into attribute-mode severity + everywhere-mode severity
3. For each InvocationExpressionSyntax and ObjectCreationExpressionSyntax:
   a. Resolve the target method symbol
   b. Determine which mode applies:
      - If method/constructor has [CallersMustNameAllParameters] → use attribute-mode severity
      - Else if everywhere-mode is active → use everywhere-mode severity
      - Else → skip
   c. For each argument in the argument list:
      - If argument.NameColon is null → report AN0103
      - Skip params array arguments (variable-length args are exempt)
      - Include the expected parameter name in the diagnostic message
```

### Error Message

The error message must be clear enough that a developer (or LLM) knows exactly how to fix it without looking up documentation:

```
AN0103: Argument 1 to 'ResolveHeightGivenResolvedWidth' must be named. 
        Use named arguments for all parameters, e.g. MyMethod(argA: 1, argB: 2)
```

For multiple unnamed args, one diagnostic per unnamed argument.

## Examples

```csharp
[CallersMustNameAllParameters]
public float ResolveHeightGivenResolvedWidth(float resolvedWidth) { ... }

[CallersMustNameAllParameters]
public void SetMargin(float vertical, float horizontal, CssUnit unit) { ... }

[CallersMustNameAllParameters]  
public void AssignBounds(RectangleF finalBounds) { ... }
```

```csharp
// ✅ All named — compiles
view.ResolveHeightGivenResolvedWidth(resolvedWidth: logicalWidth);
view.SetMargin(vertical: 4, horizontal: 8, unit: CssUnit.Px);
view.AssignBounds(finalBounds: new RectangleF(0, 0, w, h));

// ❌ AN0103 — unnamed parameter
view.ResolveHeightGivenResolvedWidth(logicalWidth);
//   → "Argument 1 to 'ResolveHeightGivenResolvedWidth' must be named.
//      Use named arguments for all parameters, e.g. MyMethod(argA: 1, argB: 2)"

// ❌ AN0103 — partially named
view.SetMargin(vertical: 4, 8, unit: CssUnit.Px);
//   → diagnostic on argument 2

// ❌ AN0103 — all unnamed
view.SetMargin(4, 8, CssUnit.Px);
//   → three diagnostics, one per argument
```

## Test Cases

- [ ] Method with attribute, all args named → no diagnostic
- [ ] Method with attribute, one arg unnamed → AN0103 on that arg
- [ ] Method with attribute, all args unnamed → AN0103 on each arg
- [ ] Method WITHOUT attribute, unnamed args → no diagnostic (default attribute-error mode)
- [ ] Constructor with attribute, unnamed args → AN0103
- [ ] Method with params array → exempt the params portion
- [ ] Attribute on interface method → enforced on calls through the interface
- [ ] `everywhere-warn` mode: method without attribute, unnamed args → AN0103 warning
- [ ] `everywhere-error` mode: method without attribute, unnamed args → AN0103 error
- [ ] `attribute-error, everywhere-warn` combined: attribute method unnamed → error, non-attribute method unnamed → warning
- [ ] `ignore` mode → no diagnostics anywhere
- [ ] Method with zero parameters → no diagnostic (nothing to name)
- [ ] Method with single parameter, named → no diagnostic
- [ ] Default (no MSBuild property) → attribute-error behavior

## Files to Create/Modify

| Action | File |
|--------|------|
| CREATE | `CallersMustNameAllParameters/CallersMustNameAllParametersAttribute.cs` |
| CREATE | `CallersMustNameAllParameters/CallersMustNameAllParametersAnalyzer.cs` |
| CREATE | `CallersMustNameAllParameters/Tests/AN.CodeAnalyzers.CallersMustNameAllParameters.Tests.csproj` |
| CREATE | `CallersMustNameAllParameters/Tests/CallersMustNameAllParametersVerifierHelper.cs` |
| CREATE | `CallersMustNameAllParameters/Tests/CallersMustNameAllParametersAnalyzerTests.cs` |
| MODIFY | `build/ArtificialNecessity.CodeAnalyzers.targets` — add `<CompilerVisibleProperty Include="RequireNamedArgumentsEverywhereLikeObjectiveC" />` |
| MODIFY | `AN_CodeAnalyzers.sln` — add test project + solution folder |
| MODIFY | `README.md` — add AN0103 documentation |
| MODIFY | `README-nuget.md` — add AN0103 to analyzer summary table |

No changes needed to `AN.CodeAnalyzers.csproj` — the existing `**/Tests/**` exclude already covers the new test directory, and the analyzer source files will be auto-included by the SDK glob.

---

## Architecture

```mermaid
flowchart TD
    A[MSBuild Property: RequireNamedArgumentsEverywhereLikeObjectiveC] --> B[.targets: CompilerVisibleProperty]
    B --> C[Roslyn: build_property.RequireNamedArgumentsEverywhereLikeObjectiveC]
    C --> D[Parse comma-separated config values]
    D --> E[attribute-mode severity + everywhere-mode severity]
    E --> F{Invocation or ObjectCreation node}
    F --> G{Method has CallersMustNameAllParameters attr?}
    G -->|Yes| H[Use attribute-mode severity]
    G -->|No| I{everywhere-mode active?}
    I -->|Yes| J[Use everywhere-mode severity]
    I -->|No| K[Skip - no diagnostic]
    H --> L{Any unnamed args?}
    J --> L
    L -->|Yes| M[Report AN0103 per unnamed arg]
    L -->|No| N[No diagnostic]