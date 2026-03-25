# ClassLibInfo — .NET API Symbol Dump Tool

**Status:** Draft Spec v0.1  
**Package:** ArtificialNecessity.CodeAnalyzers  
**Author:** David Jeske  

---

## Problem

AI assistants working on .NET projects can't see the API surface of referenced libraries or even the project's own types without reading every source file. We need a standard, always-up-to-date, dense symbol reference that lives in a known location in the workspace.

## Solution Overview

Two extraction stages, unified under one output directory:

| Stage | Input | Tool | Visibility | File:Line Info |
|-------|-------|------|------------|----------------|
| **Post-Build DLL Extraction** | Compiled assemblies + NuGet packages | `System.Reflection.Metadata` (SRM) | Public only | No (not in metadata) |
| **Roslyn Source Analysis** | Live source code | Roslyn `ISymbol` tree via Analyzer/Source Generator | Public + Internal + Private | Yes — `path:line` for every symbol |

These are fundamentally different stages because file:line data only exists in the compiler's symbol model, not in the emitted PE metadata. Trying to reverse-engineer it from PDBs is fragile and incomplete (no info for interfaces, enums, properties-as-concept, etc.).

---

## Output Directory

```
${WorkspaceRoot}/
  ClassLibInfo/
    _index.txt                          # manifest of all dumped libs
    nuget/
      Newtonsoft.Json.13.0.3.api.txt
      Microsoft.Extensions.Logging.Abstractions.8.0.0.api.txt
      System.Collections.Immutable.8.0.0.api.txt
    src/
      MyProject.api.txt                 # own project — full visibility + file:line
      MyProject.Shared.api.txt
```

**Why a flat text format?** AI context windows are token-limited. Dense, grep-friendly, indentation-structured plain text is the most efficient encoding. JSON/XML would ~3x the token cost for zero benefit.

---

## Output Format

### NuGet / External DLLs (Public Only)

```
# Newtonsoft.Json 13.0.3
# Extracted from DLL via SRM — public API only

namespace Newtonsoft.Json
  public enum Formatting
    None = 0
    Indented = 1
  public static class JsonConvert
    static string SerializeObject(object? value)
    static string SerializeObject(object? value, Formatting formatting)
    static string SerializeObject(object? value, JsonSerializerSettings? settings)
    static T? DeserializeObject<T>(string value)
    static T? DeserializeObject<T>(string value, JsonSerializerSettings? settings)
    static object? DeserializeObject(string value, Type type)
  public class JsonSerializerSettings
    .ctor()
    IContractResolver? ContractResolver { get; set; }
    NullValueHandling NullValueHandling { get; set; }
    ReferenceLoopHandling ReferenceLoopHandling { get; set; }
    IList<JsonConverter> Converters { get; set; }
  public abstract class JsonConverter
    abstract void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    abstract object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    abstract bool CanConvert(Type objectType)
  public abstract class JsonConverter<T> : JsonConverter
    ...
```

### Own Project Source (Full Visibility + File:Line)

```
# MyProject (source analysis)
# Full visibility — public, internal, private
# Format: [visibility] [kind] [name] [signature]  @ path:line

namespace MyProject.Core
  public class MergeScheduler                              @ Core/MergeScheduler.cs:15
    private readonly MergePolicy _policy                   @ Core/MergeScheduler.cs:17
    private readonly SemaphoreSlim _semaphore               @ Core/MergeScheduler.cs:18
    internal int _pendingCount                              @ Core/MergeScheduler.cs:19
    public .ctor(MergePolicy policy, int maxConcurrency)   @ Core/MergeScheduler.cs:21
    public Task RunAsync(CancellationToken ct)              @ Core/MergeScheduler.cs:35
    private async Task MergeLoop()                          @ Core/MergeScheduler.cs:52
    public int PendingCount { get; }                       @ Core/MergeScheduler.cs:70
    public event EventHandler<MergeCompletedArgs> MergeCompleted  @ Core/MergeScheduler.cs:72
  internal struct RecordKeyEncoded : IComparable<RecordKeyEncoded>  @ Core/RecordKeyEncoded.cs:8
    public static RecordKeyEncoded FromSpan(ReadOnlySpan<byte> data)  @ Core/RecordKeyEncoded.cs:14
    public int CompareTo(RecordKeyEncoded other)            @ Core/RecordKeyEncoded.cs:28
    public int Length { get; }                              @ Core/RecordKeyEncoded.cs:10
```

Paths are relative to the project root. Line numbers are 1-based, pointing to the declaration.

---

## Format Rules

1. **Namespaces** are unindented, prefixed with `namespace`
2. **Types** are indented 2 spaces: `[visibility] [kind] [name] [: bases]`
3. **Members** are indented 4 spaces: `[visibility?] [signature]`
4. **Constructors** use `.ctor` (matching IL convention, compact)
5. **Generic parameters** use angle brackets: `Dictionary<TKey, TValue>`
6. **Nullable** types use `?` suffix
7. **`static`, `abstract`, `virtual`, `override`, `sealed`, `readonly`, `async`** included when present
8. **Properties** show `{ get; set; }`, `{ get; }`, or `{ set; }` — `init` shown as `{ get; init; }`
9. **Events** prefixed with `event`
10. **Enum members** show `Name = Value`
11. **Indexers** shown as `this[KeyType key] { get; set; }`
12. **Delegates** shown as `public delegate ReturnType Name(params...)`
13. **Extension methods** prefixed with `extension` keyword (non-standard but useful for AI)
14. **Default parameter values** shown: `int count = 10`
15. **`params` arrays** shown: `params string[] args`
16. **Attribute markers** — only `[Flags]`, `[Obsolete("msg")]` — skip the rest (noise)
17. **File:line** column right-aligned with `@` separator (source mode only)

### What to Skip

- Private members in DLL mode (not accessible anyway)
- Compiler-generated members (`<>c`, `<Clone>$`, backing fields)
- `System.Object` inherited members (`ToString`, `Equals`, `GetHashCode`, `GetType`) — unless overridden
- Explicit interface implementations (noisy, rarely needed for AI context)
- Assembly-level attributes
- Module initializers

---

## Extraction Stage 1: Post-Build DLL Extraction (SRM)

### Input

Resolved assembly paths. Two sources:

1. **NuGet packages** — resolve from `obj/project.assets.json` or use `dotnet list package --include-transitive` then find DLLs under the NuGet cache
2. **Project output** — `$(TargetPath)` from MSBuild (fallback if Roslyn stage isn't available)

### Implementation

Console app or `dotnet tool` using `System.Reflection.Metadata`:

```
ApiDump.exe <input.dll> <output.api.txt> [--visibility public|all]
```

Core approach:
- Open PE with `PEReader`, get `MetadataReader`
- Walk `TypeDefinitionHandle` table
- Filter by visibility flags
- For each type: walk methods, properties, fields, events
- Decode signatures via `SignatureDecoder` — this is the non-trivial part
- Resolve `TypeReference` handles for parameter/return types (cross-assembly names)
- Handle generics: `GenericParameterHandle` on types and methods
- Sort output by namespace → type name for stable diffs

### Signature Decoding Notes

SRM's `SignatureDecoder<TType, TGenericContext>` requires you to implement a provider that maps type handles to display strings. Key things to handle:

- `TypeDefinitionHandle` → fully qualified name from same assembly
- `TypeReferenceHandle` → name from reference tables (may be in another assembly)
- `TypeSpecificationHandle` → generic instantiations, arrays, pointers, byrefs
- Primitive types (`int`, `string`, `bool`, `void`, etc.) — map from `SignatureTypeCode`
- `GenericTypeParameter` / `GenericMethodParameter` → `T`, `TKey`, etc. from the generic param table
- Nested types → `Outer.Inner`
- Nullable annotations — available in custom attributes (`NullableAttribute`) if we want them

---

## Extraction Stage 2: Roslyn Source Analysis

### Why a Separate Stage

The DLL contains the compiled public surface, but:
- No file:line information in metadata (PDB has sequence points for IL offsets, not declarations)
- No `internal` or `private` visibility from external DLLs
- For our own code, we want everything — the AI needs to know about private fields and internal helpers

### Implementation Options

**Option A: Roslyn Analyzer (Recommended)**

Register a `CompilationAction` that runs at end of compilation:

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ApiDumpAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.RegisterCompilationAction(ctx =>
        {
            var compilation = ctx.Compilation;
            // Walk compilation.Assembly.GlobalNamespace recursively
            // For each ISymbol, grab Location.GetLineSpan()
            // Write to ClassLibInfo/src/{AssemblyName}.api.txt
        });
    }
}
```

Pro: Runs automatically on every build via the CodeAnalyzers package. Con: Analyzers writing files is slightly non-standard (they normally only report diagnostics). May need `AdditionalFiles` or a custom build property to specify output path.

**Option B: Source Generator (Alternative)**

Source generators can emit files, but they're meant to emit *source*, not metadata dumps. Technically works but feels like abuse of the mechanism.

**Option C: MSBuild Task wrapping Roslyn workspace API**

A custom MSBuild `Task` that opens a `MSBuildWorkspace`, compiles, and walks the symbol tree. Most flexible but adds a full compilation pass (slow on large solutions).

**Recommendation: Option A** with the Analyzer writing to a well-known path. The path can be fed via MSBuild property:

```xml
<CompilerVisibleProperty Include="ClassLibInfoOutputDir" />
```

```csharp
// In the analyzer:
var outputDir = ctx.Options.AnalyzerConfigOptionsProvider
    .GlobalOptions.TryGetValue("build_property.ClassLibInfoOutputDir", out var dir)
    ? dir : null;
```

### Symbol Walking

```
INamespaceSymbol (global) 
  → recurse GetNamespaceMembers() + GetTypeMembers()
    → INamedTypeSymbol
      → GetMembers() → IMethodSymbol, IPropertySymbol, IFieldSymbol, IEventSymbol
      → GetTypeMembers() for nested types
```

For each symbol:
- `symbol.ToDisplayString(format)` with a custom `SymbolDisplayFormat` for compact output
- `symbol.Locations[0].GetLineSpan().StartLinePosition.Line + 1` for 1-based line
- `symbol.Locations[0].SourceTree?.FilePath` for path (make relative to project root)
- `symbol.DeclaredAccessibility` for visibility prefix

---

## MSBuild Integration

### Via CodeAnalyzers NuGet Package

The analyzer DLL ships inside the package:

```
ArtificialNecessity.CodeAnalyzers.nupkg
  analyzers/
    dotnet/
      cs/
        ArtificialNecessity.CodeAnalyzers.dll    # includes ApiDumpAnalyzer
  build/
    ArtificialNecessity.CodeAnalyzers.props       # sets defaults
    ArtificialNecessity.CodeAnalyzers.targets      # NuGet DLL dump post-build target
  tools/
    ApiDump.exe (or ApiDump.dll for dotnet tool)   # standalone SRM extractor
```

### .props (defaults)

```xml
<Project>
  <PropertyGroup>
    <ClassLibInfoOutputDir>$(MSBuildProjectDirectory)\..\ClassLibInfo</ClassLibInfoOutputDir>
    <ClassLibInfoEnabled>true</ClassLibInfoEnabled>
    <ClassLibInfoDumpNuGet>true</ClassLibInfoDumpNuGet>
  </PropertyGroup>
</Project>
```

### .targets (NuGet DLL dump)

```xml
<Project>
  <Target Name="DumpNuGetApis" 
          AfterTargets="Build" 
          Condition="'$(ClassLibInfoEnabled)' == 'true' AND '$(ClassLibInfoDumpNuGet)' == 'true'">
    
    <!-- Resolve NuGet DLL paths from ReferencePath items -->
    <ItemGroup>
      <_NuGetAssemblies Include="@(ReferencePath)" 
                         Condition="'%(ReferencePath.NuGetPackageId)' != ''" />
    </ItemGroup>
    
    <MakeDir Directories="$(ClassLibInfoOutputDir)\nuget" />
    
    <!-- Run SRM extractor on each NuGet assembly -->
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)..\tools\ApiDump.dll&quot; &quot;%(_NuGetAssemblies.Identity)&quot; &quot;$(ClassLibInfoOutputDir)\nuget\%(_NuGetAssemblies.NuGetPackageId).%(_NuGetAssemblies.NuGetPackageVersion).api.txt&quot; --visibility public"
          WorkingDirectory="$(MSBuildProjectDirectory)" />
  </Target>
</Project>
```

### Source Analysis Output

The Roslyn analyzer writes directly during compilation:

```
ClassLibInfo/src/{AssemblyName}.api.txt
```

No MSBuild target needed — the analyzer fires as part of the normal `csc` invocation.

---

## Incremental / Caching Strategy

### NuGet DLLs

- Key the cache on `{PackageId}.{PackageVersion}`
- If the `.api.txt` file already exists with matching version → skip extraction
- NuGet packages are immutable (same version = same content), so this is safe

### Source Projects

- Always regenerate on build (it's fast — just walking the symbol tree, no I/O-heavy work)
- The analyzer only runs when the compiler runs, so it's naturally incremental with `dotnet build`

### Staleness

- `.gitignore` the `ClassLibInfo/` directory (it's derived data)
- OR commit it for offline AI access (trade repo size for convenience)
- Add a `dotnet build` target that cleans it: `dotnet build /t:CleanClassLibInfo`

---

## CLI Usage (Standalone)

For users who don't want the analyzer integration:

```bash
# Dump a single DLL
ApiDump MyLib.dll MyLib.api.txt

# Dump with full visibility (for own code, from DLL fallback)  
ApiDump MyLib.dll MyLib.api.txt --visibility all

# Dump all NuGet deps for a project
ApiDump --project MyProject.csproj --output ./ClassLibInfo/nuget/

# Dump with specific framework target
ApiDump --project MyProject.csproj --framework net8.0 --output ./ClassLibInfo/nuget/
```

---

## Edge Cases & Design Decisions

### Transitive NuGet Dependencies
By default, dump only direct dependencies. Flag `--include-transitive` for everything. Transitive deps can be huge (all of `Microsoft.Extensions.*`, `System.Runtime.*`, etc.) — the AI rarely needs these unless debugging deep framework issues.

### Multi-Targeting
Projects targeting multiple TFMs (`net8.0`, `net9.0`) may have different API surfaces. Use the first/primary TFM by default. Optionally dump per-TFM with subdirectories: `ClassLibInfo/nuget/net8.0/`.

### Ref Assemblies vs Implementation Assemblies
Prefer ref assemblies when available (smaller, no method bodies, cleaner surface). NuGet packages often include them under `ref/` alongside `lib/`. SRM works fine on both.

### Generic Constraints
Include `where T : class, IComparable<T>, new()` — this is critical API surface info the AI needs to understand usage.

### Extension Methods
Tag with `extension` prefix so the AI knows these methods extend a type they're not defined on. Include the `this` parameter type:

```
extension static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)
```

### Obsolete Members
Include with `[Obsolete]` marker — the AI should know not to suggest these.

### Operator Overloads
Show as: `static bool operator ==(Foo left, Foo right)`

### Implicit/Explicit Conversions
Show as: `static implicit operator int(Foo value)` / `static explicit operator Foo(int value)`

---

## Open Questions

1. **Should we dump XML doc comments?** The `<summary>` tag would give the AI semantic context, but dramatically increases file size. Could offer a `--include-docs` flag with a one-line-summary extraction mode.

2. **Should `ClassLibInfo/` be workspace-root or solution-root?** For multi-project solutions, workspace root makes sense. For single-project repos, they're the same. Default to `$(SolutionDir)` if available, fall back to `$(MSBuildProjectDirectory)/..`.

3. **Token budget estimation:** A typical NuGet package dumps to ~2-8KB of text. A 50-package project would be ~100-400KB total. A large own-project (50K lines) might dump to ~20-40KB. This is well within AI context limits for selective inclusion.

4. **ctags compatibility?** The source format (with file:line) is ctags-adjacent but not ctags-compatible. Could optionally emit a `tags` file too, but the primary audience is AI, not vim/emacs.