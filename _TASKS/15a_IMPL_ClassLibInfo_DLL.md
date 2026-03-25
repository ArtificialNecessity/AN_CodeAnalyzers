# ClassLibInfo Stage 1: DLL API Extraction (SRM)

**Spec:** `_SPECS/15_Classlibinfo_spec.md`  
**Purpose:** Extract public API surface from compiled DLLs (NuGet packages + project output) into dense, AI-friendly `.api.txt` files using `System.Reflection.Metadata`.

---

## Phase 1 — Project Scaffolding

- [ ] Create `ClassLibInfo/ClassLibInfo.csproj` (netstandard2.0, MSBuild task library)
  - References: `Microsoft.Build.Utilities.Core`, `System.Reflection.Metadata`
  - Pattern: matches `StableABIVerification/StableABIVerification.csproj` and `CoreTools/CoreTools.csproj`
  - `DefaultItemExcludes` for `**/Tests/**` and `**/ClassLibInfoTool/**`
  - `CopyLocalLockFileAssemblies=true` (ensures SRM dll copied for NuGet packing)
- [ ] Create `ClassLibInfo/ClassLibInfoTool/AN.CodeAnalyzers.ClassLibInfo.Tool.csproj` (net8.0 console exe)
  - `ProjectReference` to `ClassLibInfo.csproj`
  - Pattern: matches `CoreTools/JsonPeekTool/AN.CodeAnalyzers.JsonPeek.Tool.csproj`
  - `AssemblyName=ClassLibInfo`, `IsPackable=false`
- [ ] Create `ClassLibInfo/Tests/AN.CodeAnalyzers.ClassLibInfo.Tests.csproj` (net8.0 test project)
- [ ] Add all three projects to `AN_CodeAnalyzers.sln`
- [ ] Add `ClassLibInfo/**` to `DefaultItemExcludes` in `AN.CodeAnalyzers.csproj`

## Phase 2 — Core SRM Extractor (`ApiDumpGenerator.cs`)

- [ ] Create `ClassLibInfo/ApiDumpGenerator.cs` — the core extraction engine
  - Input: DLL file path, options (visibility scope, doc-comments mode)
  - Output: `List<string>` of formatted lines (the `.api.txt` content)
- [ ] Implement `SignatureTypeNameProvider : ISignatureTypeProvider<string, object?>` (self-contained, not shared with StableABI)
  - Primitive type → C# keyword mapping
  - `TypeDefinitionHandle` → fully qualified name (namespace.type)
  - `TypeReferenceHandle` → name from reference tables (cross-assembly)
  - `TypeSpecificationHandle` → generic instantiations, arrays, pointers, byrefs
  - `GenericTypeParameter` / `GenericMethodParameter` → resolve actual names from `GenericParameterHandle` table (not just `!0`, `!!0`)
  - Nested types → `Outer.Inner`
- [ ] Implement `NullableAttribute` / `NullableContextAttribute` decoding (core, not optional)
  - Read the byte-array encoding from custom attributes on types, methods, parameters
  - Walk the type tree in the same order as the signature decoder to apply `?` suffixes
  - Handle `NullableContextAttribute` (type-level default) vs per-member `NullableAttribute` overrides
  - This must be built into the signature decoder from the start — retrofitting is much harder because the nullability byte array indexing must walk the type tree in sync with signature decoding
- [ ] Implement type walking: `TypeDefinitionHandle` table iteration
  - Skip `<Module>`, compiler-generated types (`<>c`, display classes)
  - Scope filtering: public-only for NuGet DLLs, all for `--visibility all`
  - Sort output by namespace → type name for stable diffs
- [ ] Implement namespace grouping with indentation-based output format per spec:
  - Namespaces unindented with `namespace` prefix
  - Types indented 2 spaces: `[visibility] [kind] [name] [: bases]`
  - Members indented 4 spaces: `[visibility?] [signature]`
- [ ] Implement type declaration formatting:
  - Class (abstract, sealed, static), struct, interface, enum, delegate, record
  - Base types and interface implementations
  - Generic type parameters with constraints (`where T : class, IComparable<T>, new()`)
  - `[Flags]` and `[Obsolete]` attribute markers (skip all others)
- [ ] Implement member extraction:
  - Methods: return type, name, parameters with types and names, default values, `params`, generic method parameters
  - Constructors: `.ctor` convention
  - Properties: `{ get; set; }`, `{ get; }`, `{ set; }`, `{ get; init; }`
  - Fields: `static`, `readonly`, `const` with values
  - Events: `event` prefix with handler type
  - Indexers: `this[KeyType key] { get; set; }`
  - Delegates: `delegate ReturnType Name(params...)`
  - Operators: `static bool operator ==(Foo left, Foo right)`
  - Implicit/explicit conversions: `static implicit operator int(Foo value)`
  - Extension methods: `ext` prefix with `this` parameter type (NOT `extension` — avoids collision with C# 14 `extension` keyword for extension types)
- [ ] Implement modifier keywords: `static`, `abstract`, `virtual`, `override`, `sealed`, `readonly`, `async`
- [ ] Implement skip rules:
  - Compiler-generated members (`<>c`, `<Clone>$`, backing fields)
  - `System.Object` inherited members unless overridden
  - Explicit interface implementations
  - Assembly-level attributes, module initializers
  - Record-specific compiler goo: skip `PrintMembers`, `EqualityContract`, `<Clone>$`
  - Record-specific real API: KEEP positional properties, `Deconstruct`, equality operators

## Phase 3 — Doc Comments Extraction

- [ ] Implement XML doc comment extraction from `.xml` sidecar files
  - Look for `{AssemblyName}.xml` next to the DLL
  - Parse `<member name="M:Namespace.Type.Method">` entries
  - Build doc comment member IDs from SRM metadata to match against XML entries
  - NOTE: XML doc IDs use backtick notation for generics (`Where``1`) and double-backtick for method type arity — this encoding differs from display format and must be carefully constructed from the metadata
- [ ] Implement `--doc-comments` modes:
  - `none` — no doc comments in output
  - `brief` — first N characters of `<summary>` (configurable N, default ~120)
  - `full` — complete `<summary>` text, whitespace-normalized to single line
- [ ] Format doc comments as `// summary text` on the line before the member declaration

## Phase 4 — CLI Tool (`ClassLibInfoTool/`)

- [ ] Create `ClassLibInfo_Program.cs` — CLI entry point (pattern: `CoreTools/JsonPeekTool/JsonPeek_Program.cs`)
- [ ] Implement single-DLL mode: `ClassLibInfo <input.dll> <output.api.txt> [options]`
  - `--visibility public|all` (default: public)
  - `--doc-comments none|brief|full` (default: brief)
- [ ] Implement batch mode: `ClassLibInfo --batch <manifest.txt> --output <dir> [options]`
  - Manifest format: `<dll-path>\t<PackageId>\t<PackageVersion>` (one per line)
  - MSBuild target writes the manifest, invokes CLI once — avoids 40+ process spawns for large projects
  - Skip entries where `<output>/<PackageId>.<PackageVersion>.api.txt` already exists (cache hit)
- [ ] Implement project mode: `ClassLibInfo --project <path.csproj> --output <dir> [options]`
  - Run `dotnet msbuild <path.csproj> -getProperty:TargetPath` to find compiled DLL
  - Run `dotnet msbuild <path.csproj> -getItem:ReferencePath` to find NuGet DLL paths with package metadata
  - Build manifest internally, then process as batch
  - Write to `<output>/nuget/{PackageId}.{PackageVersion}.api.txt`
  - Write project output to `<output>/src/{AssemblyName}.api.txt`
- [ ] Implement `--include-transitive` flag (default: direct deps only)
- [ ] Implement `--framework <tfm>` for multi-targeting projects
- [ ] Generate `_index.txt` manifest with machine-parseable header:
  ```
  # ClassLibInfo index — generated 2026-03-25T14:30:00Z
  # Project: MyProject (net8.0)
  nuget/Newtonsoft.Json.13.0.3.api.txt  PackageId=Newtonsoft.Json  Version=13.0.3
  nuget/System.Collections.Immutable.8.0.0.api.txt  PackageId=System.Collections.Immutable  Version=8.0.0
  src/MyProject.api.txt  AssemblyName=MyProject  Generated=2026-03-25T14:30:00Z
  ```

## Phase 5 — MSBuild Task Integration

- [ ] Create `ClassLibInfo/ClassLibInfoDllTask.cs` — MSBuild Task class
  - Input: `ProjectDirectory`, `OutputDirectory`, `Scope`, `DocComments`, `IncludeTransitive`
  - Uses `TaskHostFactory` for out-of-process execution (matches existing pattern)
  - Builds manifest from `@(ReferencePath)` items with `NuGetPackageId`/`NuGetPackageVersion` metadata
  - Invokes `ApiDumpGenerator` directly (in-process via task DLL, not shelling out to CLI)
- [ ] Add `UsingTask` and targets to `build/ArtificialNecessity.CodeAnalyzers.targets`:
  - `ClassLibInfoEnabled` property (default: false)
  - `ClassLibInfoOutputDir` property (default: `$(SolutionDir)ClassLibInfo` or `$(MSBuildProjectDirectory)\..\ClassLibInfo`)
  - `ClassLibInfoDocComments` property (default: brief)
  - Post-build target `DumpClassLibInfo` that runs `AfterTargets="Build"`
  - Caching: skip NuGet DLLs where `.api.txt` already exists with matching version
- [ ] Add pack items to `AN.CodeAnalyzers.csproj`:
  - ClassLibInfo task DLL → `tasks/netstandard2.0/`
  - ClassLibInfo CLI tool exe + deps → `tools/net8.0/any/`
  - `System.Reflection.Metadata.dll` dependency (if not already packed from StableABI)

## Phase 6 — Tests

- [ ] Unit tests for `ApiDumpGenerator`:
  - Compile small test assemblies in-memory or from embedded resources
  - Verify output format for: classes, interfaces, structs, enums, delegates
  - Verify generic types with constraints
  - Verify nullable annotations (`string` vs `string?`, `T?`, `IList<string?>?`)
  - Verify properties, events, indexers, operators
  - Verify extension methods get `ext` prefix
  - Verify `[Flags]` and `[Obsolete]` markers
  - Verify compiler-generated members are skipped
  - Verify record types: `PrintMembers`/`EqualityContract` skipped, positional props/`Deconstruct` kept
  - Verify `System.Object` members skipped unless overridden
  - Verify namespace grouping and indentation
  - Verify visibility filtering (public vs all)
- [ ] Unit tests for doc comment extraction:
  - Verify XML sidecar parsing
  - Verify doc comment ID construction (especially generics with backtick notation)
  - Verify brief/full/none modes
- [ ] Integration test: run CLI on a known DLL, snapshot-compare output
- [ ] Integration test: run batch mode with manifest file
- [ ] Integration test: run project mode on a test .csproj

## Phase 7 — Documentation

- [ ] Update `README.md` with ClassLibInfo section
- [ ] Update `README-nuget.md` with ClassLibInfo usage
- [ ] Add ClassLibInfo to the analyzer summary table in README