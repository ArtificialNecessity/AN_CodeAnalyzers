# AN0103 — CallersMustNameAllParameters Analyzer

## What

A Roslyn analyzer that enforces named parameters at call sites for methods decorated with `[CallersMustNameAllParameters]`. If any argument at the call site lacks an explicit parameter name, it's a compile error.

## Why

LLMs guess parameter order by vibes. A method like `ResolveHeight(float value)` gets called with whatever float is nearby. Named parameters make the intent visible at the call site and catch mismatches at compile time.

Sonnet's actual bug: called `ResolveHeight(availableHeight)` instead of `ResolveHeight(resolvedWidth)`. With this analyzer, it would have been forced to write `ResolveHeight(resolvedWidth: availableHeight)` — and the name mismatch is now a visible, obvious error even before the compiler catches it.

## Attribute

```csharp
namespace ArtificialNecessity.CodeAnalyzers;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public class CallersMustNameAllParametersAttribute : Attribute { }
```

Ship in the `ArtificialNecessity.CodeAnalyzers` NuGet alongside AN0100 and AN0101. The attribute is in the analyzers package so consumers get it automatically.

## Analyzer

**Diagnostic ID:** AN0103  
**Category:** Naming  
**Severity:** Error  
**Title:** Method requires named parameters at call site  
**Message:** `Method '{methodName}' requires named parameters. Use '{parameterName}: value' for argument {position}.`

### Detection Logic

```
1. Find all InvocationExpressionSyntax and ObjectCreationExpressionSyntax nodes
2. Resolve the target method symbol
3. Check if the method (or constructor) has [CallersMustNameAllParameters]
4. For each argument in the argument list:
   a. If argument.NameColon is null → report AN0103
   b. Include the expected parameter name in the diagnostic message
5. Skip params array arguments (variable-length args are exempt)
```

### Code Fix

Auto-fix: insert the parameter name at each unnamed argument.

```csharp
// Before fix:
view.ResolveHeightGivenResolvedWidth(logicalWidth);

// After fix (automatic):
view.ResolveHeightGivenResolvedWidth(resolvedWidth: logicalWidth);
```

The fix reads parameter names from the method symbol and inserts `NameColon` syntax at each argument position.

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
//   → "Method 'ResolveHeightGivenResolvedWidth' requires named parameters. 
//      Use 'resolvedWidth: value' for argument 1."

// ❌ AN0103 — partially named
view.SetMargin(vertical: 4, 8, unit: CssUnit.Px);
//   → "Use 'horizontal: value' for argument 2."

// ❌ AN0103 — all unnamed
view.SetMargin(4, 8, CssUnit.Px);
//   → three diagnostics, one per argument
```

## Scope for Decoration

Methods worth decorating (not exhaustive, add as needed):

- Layout methods: `ResolveHeightGivenResolvedWidth`, `AssignBounds`, `GetResolvedWidth`
- Any method with multiple parameters of the same type (two floats, two strings)
- Fluent API methods where parameter meaning isn't obvious from type alone
- Constructors with more than 2-3 parameters

## Test Cases

- [ ] Method with attribute, all args named → no diagnostic
- [ ] Method with attribute, one arg unnamed → AN0103 on that arg
- [ ] Method with attribute, all args unnamed → AN0103 on each arg
- [ ] Method WITHOUT attribute, unnamed args → no diagnostic (normal C#)
- [ ] Constructor with attribute, unnamed args → AN0103
- [ ] Method with params array → exempt the params portion
- [ ] Code fix inserts correct parameter names
- [ ] Code fix handles multiple unnamed args in one call
- [ ] Attribute on interface method → enforced on calls through the interface

## Files

```
AN_CodeAnalyzers/
  Attributes/
    CallersMustNameAllParametersAttribute.cs    ← the attribute
  Analyzers/
    AN0103_CallersMustNameAllParametersAnalyzer.cs
  CodeFixes/
    AN0103_CallersMustNameAllParametersCodeFix.cs
  Tests/
    AN0103_CallersMustNameAllParametersTests.cs
```