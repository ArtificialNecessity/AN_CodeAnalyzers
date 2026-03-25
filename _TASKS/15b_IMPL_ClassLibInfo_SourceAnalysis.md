# ClassLibInfo Stage 2: Source Code Analysis (MSBuild Task)

**Spec:** `_SPECS/15_Classlibinfo_spec.md`  
**Depends on:** Stage 1 (15a) for output format conventions and shared output directory  
**Purpose:** Extract full API surface (public + internal + private) with `file:line` info from own-project source code using an MSBuild Task that creates a `CSharpCompilation` from the project's source files and references.

---

## Design Decision: MSBuild Task, Not Analyzer

Instead of a Roslyn `DiagnosticAnalyzer` (which has design-time build complications and is non-standard for file-writing), this is implemented as an **MSBuild Task** that:
- Runs `AfterTargets="CoreCompile"` — source files and references are already resolved
- Uses `Microsoft.CodeAnalysis.CSharp.CSharpCompilation` directly to parse source and build a symbol model
- Guards against design-time builds: `Condition="'$(DesignTimeBuild)' != 'true'"`
- Does a second parse pass (syntax + symbols only, no codegen) — fast for symbol walking
- Uses `TaskHostFactory` for out-of-process execution (matches existing project patterns)

---

## Phase 1 — Project Scaffolding

- [ ] Add source analysis capability to `ClassLibInfo/ClassLibInfo.csproj`
  - Add reference: `Microsoft.CodeAnalysis.CSharp` (needed for `CSharpCompilation`)
  - The task DLL already targets netstandard2.0 from Stage 1
  - Both DLL extraction and source analysis live in the same project/assembly
- [ ] Alternatively, if the `Microsoft.CodeAnalysis.CSharp` dependency is too heavy for the netstandard2.0 task DLL, create a separate `ClassLibInfo/ClassLibInfoSource.csproj` (net8.0) that the CLI tool and a separate MSBuild target invoke
  - Decision: evaluate dependency weight during implementation

## Phase 2 — Source Compilation Builder

- [ ] Create `ClassLibInfo/SourceApiDumpGenerator.cs` — the source analysis engine
  - Input: list of source file paths, list of reference assembly paths, project root path, options
  - Output: `List<string>` of formatted lines (the `.api.txt` content with `@ file:line`)
- [ ] Build a `CSharpCompilation` from MSBuild-provided inputs:
  - `@(Compile)` item group → source file paths → `SyntaxTree` via `CSharpSyntaxTree.ParseText()`
  - `@(ReferencePath)` item group → reference DLL paths → `MetadataReference.CreateFromFile()`
  - Assembly name from `$(AssemblyName)`
  - No need for full compilation/emit — just need the semantic model for symbol resolution

## Phase 3 — Symbol Tree Walker

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

## Phase 4 — Output Formatting (Source Mode)

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
  - Nullable annotations (built into the symbol model — no manual decoding needed unlike SRM)
- [ ] Right-align `@ path:line` column with consistent padding
- [ ] Include all visibility levels (public, internal, private) — key difference from DLL mode
- [ ] Extension methods: use `ext` prefix (same as Stage 1, avoids C# 14 `extension` keyword collision)
- [ ] Apply same skip rules as DLL mode:
  - Compiler-generated members
  - `System.Object` inherited members unless overridden
  - Backing fields for auto-properties
  - Record goo: skip `PrintMembers`, `EqualityContract`; keep positional props, `Deconstruct`

## Phase 5 — Doc Comments (Source Mode)

- [ ] Implement `--doc-comments` support for source mode:
  - Extract from `symbol.GetDocumentationCommentXml()` (much easier than SRM — no manual ID construction)
  - `none` — skip (recommended default for source projects since AI can read file:line)
  - `brief` — first N chars of `<summary>`
  - `full` — complete `<summary>` text
- [ ] Default to `none` for source mode (spec caveat: doc comments can be wrong/stale, file:line is more reliable for own-project code)

## Phase 6 — MSBuild Task Integration

- [ ] Create `ClassLibInfo/ClassLibInfoSourceTask.cs` — MSBuild Task class
  - Input properties:
    - `SourceFiles` (ITaskItem[]) — from `@(Compile)`
    - `ReferencePaths` (ITaskItem[]) — from `@(ReferencePath)`
    - `AssemblyName` (string)
    - `ProjectDirectory` (string) — for making paths relative
    - `OutputDirectory` (string) — ClassLibInfo output dir
    - `DocComments` (string) — none/brief/full
  - Uses `TaskHostFactory` for out-of-process execution
  - Writes to `{OutputDirectory}/src/{AssemblyName}.api.txt`
- [ ] Add target to `build/ArtificialNecessity.CodeAnalyzers.targets`:
  ```xml
  <Target Name="DumpClassLibInfoSource"
          AfterTargets="CoreCompile"
          Condition="'$(ClassLibInfoSourceEnabled)' == 'true' AND '$(DesignTimeBuild)' != 'true'">
    <ClassLibInfoSourceTask
      SourceFiles="@(Compile)"
      ReferencePaths="@(ReferencePath)"
      AssemblyName="$(AssemblyName)"
      ProjectDirectory="$(MSBuildProjectDirectory)"
      OutputDirectory="$(ClassLibInfoOutputDir)\src"
      DocComments="$(ClassLibInfoSourceDocComments)" />
  </Target>
  ```
  - `ClassLibInfoSourceEnabled` property (default: false)
  - `ClassLibInfoSourceDocComments` property (default: none)
  - Shared `ClassLibInfoOutputDir` with Stage 1 so both write to the same `ClassLibInfo/` directory
- [ ] Add pack items to `AN.CodeAnalyzers.csproj` (if separate assembly from Stage 1):
  - Source analysis task DLL → `tasks/netstandard2.0/` (or `tasks/net8.0/` if it needs newer runtime)
  - `Microsoft.CodeAnalysis.CSharp.dll` and dependencies

## Phase 7 — Tests

- [ ] Unit tests for `SourceApiDumpGenerator`:
  - Create in-memory compilations with `CSharpCompilation.Create()`
  - Verify all symbol types extracted: classes, interfaces, structs, enums, delegates
  - Verify nested types
  - Verify all visibility levels included (public, internal, private)
  - Verify file:line extraction
  - Verify nullable annotations
  - Verify record type handling (skip goo, keep real API)
- [ ] Unit tests for output formatting:
  - Verify indentation structure (namespace/type/member)
  - Verify `@ path:line` alignment
  - Verify visibility prefixes on all members
  - Verify modifier keywords (static, abstract, virtual, override, sealed, readonly, async)
  - Verify generic constraints
  - Verify property accessor formatting
  - Verify `ext` prefix on extension methods
- [ ] Unit tests for doc comment extraction:
  - Verify none/brief/full modes
  - Verify XML parsing of `<summary>` tags via `GetDocumentationCommentXml()`
- [ ] MSBuild task integration test:
  - Verify task runs with mock inputs
  - Verify output file is created with expected content
  - Verify `DesignTimeBuild` condition prevents execution

## Phase 8 — Documentation

- [ ] Update `README.md` with ClassLibInfo source analysis section
- [ ] Document the two-stage architecture: DLL extraction for NuGet deps, source analysis for own code
- [ ] Document MSBuild properties for enabling/configuring both stages