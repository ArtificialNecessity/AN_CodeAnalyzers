# AN0501 Implementation Plan — PureFunctionNoInstanceWrites

Spec: [`An0501_PureFunctionNoInstanceWrites.md`](An0501_PureFunctionNoInstanceWrites.md)

---

## Phase 1: Attribute

- [ ] Create `PureFunction/PureFunctionAttribute.cs`
  - Namespace: `AN.CodeAnalyzers.PureFunction`
  - `sealed class`, `AttributeTargets.Method`, `Inherited = true`, `AllowMultiple = false`

## Phase 2: Analyzer Core

- [ ] Create `PureFunction/PureFunctionAnalyzer.cs`
  - Diagnostic ID: `AN0501`, category `Correctness`, severity `Error`, always enabled
  - `Initialize()`: `RegisterSymbolStartAction(SymbolKind.Method)`
  - In symbol start callback:
    - [ ] Check if method has `[PureFunction]` directly via `GetAttributes()` name match
    - [ ] Walk `OverriddenMethod` chain upward to detect inherited `[PureFunction]` (Roslyn doesn't auto-inherit attributes)
    - [ ] If found, register `OperationAction` for the relevant operation kinds
  - Operation callbacks:
    - [ ] `ISimpleAssignmentOperation` — check if `Target` is instance field/property ref on `this`
    - [ ] `ICompoundAssignmentOperation` — same check
    - [ ] `IIncrementOrDecrementOperation` — same check
    - [ ] `IArgumentOperation` where `Parameter.RefKind` is `Ref`/`Out` — check if value is instance field ref
  - Helper: `IsInstanceMemberWrite(IOperation target)` — returns true when operation is `IFieldReferenceOperation` or `IPropertyReferenceOperation` with `Instance` being `IInstanceReferenceOperation`
  - Report diagnostic with method name and member name in message

## Phase 3: Test Infrastructure

- [ ] Create `PureFunction/Tests/AN.CodeAnalyzers.PureFunction.Tests.csproj`
  - Copy structure from `CallersMustNameAllParameters/Tests/*.csproj`
  - `net8.0`, xUnit 2.7, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.2`
  - `ProjectReference` to `../../AN.CodeAnalyzers.csproj`
- [ ] Create `PureFunction/Tests/PureFunctionVerifierHelper.cs`
  - Embed `PureFunctionAttribute` source text as const string
  - `CreateNoDiagnosticsTest(string sourceCode)` factory
  - `CreateDiagnosticsTest(string sourceCode, DiagnosticResult[] expected)` factory
  - `ExpectAN0501Error(int markupIndex, string methodName, string memberKind, string memberName)` helper
  - No MSBuild property injection needed (always-on, attribute-driven)

## Phase 4: Test Cases

- [ ] **Flagged (expect AN0501):**
  - [ ] Field assignment: `_foo = 5`
  - [ ] Field compound assignment: `_count += 1`
  - [ ] Field increment: `_counter++`
  - [ ] Field decrement: `_counter--`
  - [ ] Property setter: `Visible = false`
  - [ ] Explicit this: `this._cache = data`
  - [ ] `ref` instance field: `SomeMethod(ref _field)`
  - [ ] `out` instance field: `SomeMethod(out _field)`
  - [ ] Inherited attribute via override chain (base has `[PureFunction]`, derived overrides without attribute)
  - [ ] Multi-level inheritance (grandchild override)
- [ ] **Not flagged (expect clean):**
  - [ ] Local variable assignment
  - [ ] Parameter assignment
  - [ ] Static field assignment
  - [ ] Instance method call (no transitive check)
  - [ ] Method call on field (`_list.Add(x)`)
  - [ ] Event raise (`StateChanged?.Invoke()`)
  - [ ] Method without `[PureFunction]` doing same mutations
  - [ ] Lambda capturing and mutating a local inside `[PureFunction]` method
- [ ] **Edge cases:**
  - [ ] Multiple mutations in one method — each gets its own diagnostic
  - [ ] Auto-property backing field via property setter (`this.Prop = x`)
  - [ ] Lambda inside `[PureFunction]` that writes instance field — should flag

## Phase 5: Solution Integration

- [ ] Add test project to `AN_CodeAnalyzers.sln`
  - Solution folder `PureFunction` with nested `Tests` folder
  - Register `PureFunction/Tests/AN.CodeAnalyzers.PureFunction.Tests.csproj`
- [ ] Verify `AN.CodeAnalyzers.csproj` auto-includes `PureFunction/*.cs` (no change needed — `DefaultItemExcludes` already handles `**/Tests/**`)

## Phase 6: Build & Verify

- [ ] `dotnet build` — analyzer compiles into main DLL
- [ ] `dotnet test` — all new tests pass
- [ ] `dotnet test` — all existing tests still pass (no regressions)

## Phase 7: Documentation

- [ ] Update `README.md` — add AN0501 to analyzer summary table, add section with usage docs
- [ ] Update `README-nuget.md` — add AN0501 to summary table and add section