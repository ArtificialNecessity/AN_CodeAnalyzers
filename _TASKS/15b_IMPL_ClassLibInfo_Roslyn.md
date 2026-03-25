# ClassLibInfo Stage 2: Roslyn Source Analysis

**Spec:** `_SPECS/15_Classlibinfo_spec.md`  
**Depends on:** Stage 1 (15a) for output format conventions and shared output directory  
**Purpose:** Extract full API surface (public + internal + private) with `file:line` info from own-project source code using a Roslyn analyzer that runs during compilation.

---

## Phase 1 — Roslyn Analyzer Scaffolding

- [ ] Create `ClassLibInfo/ClassLibInfoSourceAnalyzer.cs` in the main `AN.CodeAnalyzers.csproj` analyzer project
  - This is a `DiagnosticAnalyzer` that registers a `CompilationAction`
  - Ships inside the analyzer DLL (same as ExplicitEnums, PublicConstAnalyzer)
  - Controlled by MSBuild property `ClassLibInfoSourceEnabled` (default: false)
- [ ] Add `CompilerVisibleProperty` for `ClassLibInfoSourceEnabled` and `ClassLibInfoOutputDir` to `build/ArtificialNecessity.CodeAnalyzers.targets`
- [ ] Read output directory from `build_property.ClassLibInfoOutputDir` via `AnalyzerConfigOptionsProvider.GlobalOptions`

## Phase 2 — Symbol Tree Walker

- [ ] Implement recursive `INamespaceSymbol` walker starting from `compilation.Assembly.GlobalNamespace`
  - `GetNamespaceMembers()` for child namespaces
  - `GetTypeMembers()` for types in each namespace
- [ ] For each `INamedTypeSymbol`, walk members via `GetMembers()`:
  - `IMethodSymbol` — methods, constructors, operators, conversions
  - `IPropertySymbol` — properties, indexers
  - `IFieldSymbol` — fields, constants
  - `IEventSymbol` — events
  - `GetTypeMembers()` — nested types (recurse)
- [ ] Extract file:line for every symbol:
  - `symbol.Locations[0].GetLineSpan().StartLinePosition.Line + 1` (1-based)
  - `symbol.Locations[0].SourceTree?.FilePath` (make relative to project root)
  - Skip symbols with no source location (compiler-generated)
- [ ] Extract visibility via `symbol.DeclaredAccessibility`:
  - `public`, `internal`, `protected`, `protected internal`, `private protected`, `private`

## Phase 3 — Output Formatting (Source Mode)

- [ ] Format output matching spec's source mode with `@ path:line` suffix:
  ```
  namespace MyProject.Core
    public class MergeScheduler                              @ Core/MergeScheduler.cs:15
      private readonly MergePolicy _policy                   @ Core/MergeScheduler.cs:17
      public .ctor(MergePolicy policy, int maxConcurrency)   @ Core/MergeScheduler.cs:21
      public Task RunAsync(CancellationToken ct)              @ Core/MergeScheduler.cs:35
  ```
- [ ] Use `symbol.ToDisplayString(format)` with custom `SymbolDisplayFormat` for compact output:
  - Include type kind, visibility, modifiers
  - Generic parameters with constraints
  - Parameter names, types, default values, `params`
  - Property accessors `{ get; set; }` / `{ get; init; }`
- [ ] Right-align `@ path:line` column with consistent padding
- [ ] Include all visibility levels (public, internal, private) — this is the key difference from DLL mode
- [ ] Apply same skip rules as DLL mode:
  - Compiler-generated members
  - `System.Object` inherited members unless overridden
  - Backing fields for auto-properties

## Phase 4 — Doc Comments (Source Mode)

- [ ] Implement `--doc-comments` support for source mode:
  - Extract from `symbol.GetDocumentationCommentXml()`
  - `none` — skip (recommended default for source projects since AI can read file:line)
  - `brief` — first N chars of `<summary>`
  - `full` — complete `<summary>` text
- [ ] Default to `none` for source mode (spec caveat: doc comments can be wrong/stale, file:line is more reliable)
- [ ] Read `ClassLibInfoSourceDocComments` MSBuild property via `CompilerVisibleProperty`

## Phase 5 — File Writing from Analyzer

- [ ] Write output to `ClassLibInfo/src/{AssemblyName}.api.txt`
- [ ] Handle the non-standard nature of analyzers writing files:
  - Use `AdditionalFiles` or direct file I/O from the `CompilationAction` callback
  - Ensure output directory exists (create if needed)
  - Handle concurrent builds gracefully (file locking)
- [ ] Ensure incremental behavior: analyzer only runs when compiler runs, so naturally incremental with `dotnet build`

## Phase 6 — MSBuild Integration

- [ ] Add properties to `build/ArtificialNecessity.CodeAnalyzers.targets`:
  - `ClassLibInfoSourceEnabled` (default: false)
  - `ClassLibInfoSourceDocComments` (default: none)
  - Both as `CompilerVisibleProperty` items
- [ ] No separate MSBuild target needed — the analyzer fires as part of normal `csc` invocation
- [ ] Ensure `ClassLibInfoOutputDir` is shared between Stage 1 (DLL) and Stage 2 (source) so both write to the same `ClassLibInfo/` directory

## Phase 7 — Tests

- [ ] Unit tests for symbol walker:
  - Create in-memory compilations with `CSharpCompilation.Create()`
  - Verify all symbol types extracted: classes, interfaces, structs, enums, delegates
  - Verify nested types
  - Verify all visibility levels included
  - Verify file:line extraction
- [ ] Unit tests for output formatting:
  - Verify indentation structure (namespace/type/member)
  - Verify `@ path:line` alignment
  - Verify visibility prefixes on all members
  - Verify modifier keywords (static, abstract, virtual, override, sealed, readonly, async)
  - Verify generic constraints
  - Verify property accessor formatting
- [ ] Unit tests for doc comment extraction:
  - Verify none/brief/full modes
  - Verify XML parsing of `<summary>` tags
- [ ] Analyzer integration test using `CSharpAnalyzerTest` (Roslyn test infrastructure):
  - Verify analyzer runs without diagnostics
  - Verify output file is created with expected content

## Phase 8 — Documentation

- [ ] Update `README.md` with ClassLibInfo source analysis section
- [ ] Document the two-stage architecture: DLL extraction for NuGet deps, Roslyn analysis for own source
- [ ] Document MSBuild properties for enabling/configuring both stages