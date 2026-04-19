# AN0501 Implementation Plan — PureFunctionNoInstanceWrites

Spec: [`An0501_PureFunctionNoInstanceWrites.md`](An0501_PureFunctionNoInstanceWrites.md)

---

## Phase 1: Attribute

- [x] Create `PureFunction/PureFunctionAttribute.cs`
  - Namespace: `AN.CodeAnalyzers.PureFunction`
  - `sealed class`, `AttributeTargets.Method`, `Inherited = true`, `AllowMultiple = false`

## Phase 2: Analyzer Core

- [x] Create `PureFunction/PureFunctionAnalyzer.cs`
  - Diagnostic ID: `AN0501`, category `Correctness`, severity `Error`, always enabled
  - `Initialize()`: `RegisterOperationBlockStartAction`
  - In operation block start callback:
    - [x] Check if method has `[PureFunction]` directly via `GetAttributes()` name match
    - [x] Walk `OverriddenMethod` chain upward to detect inherited `[PureFunction]`
    - [x] Check explicit and implicit interface implementations
    - [x] If found, register `OperationAction` for the relevant operation kinds
  - Operation callbacks:
    - [x] `ISimpleAssignmentOperation` — check if `Target` is instance field/property ref on `this`
    - [x] `ICompoundAssignmentOperation` — same check
    - [x] `OperationKind.Increment` / `OperationKind.Decrement` — same check
    - [x] `IArgumentOperation` where `Parameter.RefKind` is `Ref`/`Out` — check if value is instance field ref
  - Helper: `tryGetInstanceMemberInfo(IOperation target)` — returns true when operation is `IFieldReferenceOperation` or `IPropertyReferenceOperation` with `Instance` being `IInstanceReferenceOperation`
  - Report diagnostic with method name, member kind, and member name in message

## Phase 3: Test Infrastructure

- [x] Create `PureFunction/Tests/AN.CodeAnalyzers.PureFunction.Tests.csproj`
  - Copied structure from `CallersMustNameAllParameters/Tests/*.csproj`
  - `net8.0`, xUnit 2.7, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.2`
  - `ProjectReference` to `../../AN.CodeAnalyzers.csproj`
- [x] Create `PureFunction/Tests/PureFunctionVerifierHelper.cs`
  - Embed `PureFunctionAttribute` source text as const string
  - `CreateNoDiagnosticsTest(string sourceCode)` factory
  - `CreateDiagnosticsTest(string sourceCode, DiagnosticResult[] expected)` factory
  - `ExpectAN0501Error(int markupIndex, string methodName, string memberKind, string memberName)` helper
  - No MSBuild property injection needed (always-on, attribute-driven)

## Phase 4: Test Cases (21 tests, all passing)

- [x] **Flagged (expect AN0501):**
  - [x] Field assignment: `_foo = 5`
  - [x] Field compound assignment: `_count += 1`
  - [x] Field increment: `_counter++`
  - [x] Field decrement: `_counter--`
  - [x] Property setter: `Visible = false`
  - [x] Explicit this: `this._cache = data`
  - [x] `ref` instance field: `SomeMethod(ref _field)`
  - [x] `out` instance field: `SomeMethod(out _field)`
  - [x] Inherited attribute via override chain (base has `[PureFunction]`, derived overrides without attribute)
  - [x] Multi-level inheritance (grandchild override)
  - [x] Multiple mutations in one method — each gets its own diagnostic
  - [x] Lambda inside `[PureFunction]` that writes instance field
  - [x] Interface `[PureFunction]` with implicit implementation
- [x] **Not flagged (expect clean):**
  - [x] Local variable assignment
  - [x] Parameter assignment
  - [x] Static field assignment
  - [x] Instance method call (no transitive check)
  - [x] Method call on field (`_list.Add(x)`)
  - [x] Event raise (`StateChanged?.Invoke()`)
  - [x] Method without `[PureFunction]` doing same mutations
  - [x] Lambda capturing and mutating a local inside `[PureFunction]` method

## Phase 5: Solution Integration

- [x] Add test project to `AN_CodeAnalyzers.sln`
  - Solution folder `PureFunction` with nested `Tests` folder
  - Registered `PureFunction/Tests/AN.CodeAnalyzers.PureFunction.Tests.csproj`
- [x] Verified `AN.CodeAnalyzers.csproj` auto-includes `PureFunction/*.cs` (no change needed)

## Phase 6: Build & Verify

- [x] `dotnet build` — analyzer compiles into main DLL (0 errors)
- [x] `dotnet test` — all 21 new PureFunction tests pass
- [x] `dotnet test` — all 190 existing tests still pass (0 regressions, 211 total)

## Phase 7: Documentation

- [x] Update `README.md` — add AN0501 to analyzer summary table, add section with usage docs
- [x] Update `README-nuget.md` — add AN0501 to summary table and add section