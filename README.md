# AN.CodeAnalyzers

(C)opyright 2026 by David Jeske
Licensed under the Apache License, Version 2.0

Roslyn code analyzers, MSBuild tools, and runtime libraries for preventing silent binary compatibility breaks and enforcing managed-only assembly loading in C# projects.

## Packages

This repository produces two independent NuGet packages:

| Package | Description |
|---|---|
| **ArtificialNecessity.CodeAnalyzers** | Roslyn analyzers + MSBuild tasks (build-time, development dependency) |
| **ArtificialNecessity.SaferAssemblyLoader** | Runtime library — load assemblies with a managed-only guarantee |

## Analyzer Summary

| Verifier                        | Rule   | Description                                                                                                                                                                         |
| ------------------------------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **RequireTypedPointersNotIntPtr** | AN0100 | Flags any use of `IntPtr`/`UIntPtr` everywhere and `nint`/`nuint` in P/Invoke declarations. These types erase type information, enable silent type confusion, and create security vulnerabilities. No exceptions. |
| **CallersMustNameAllParameters** | AN0103 | Enforces named arguments at call sites for methods with 2+ parameters. Attribute-driven or everywhere mode. Prevents LLM parameter-order confusion. |
| **ProhibitPlatformImports** | AN0104 | Flags `[DllImport]`, `[LibraryImport]`, `[UnmanagedCallersOnly]`, and `NativeLibrary.Load/TryLoad` calls. Project-level policy to prohibit all platform imports. |
| **ProhibitNamespaceAccess** | AN0105 | Prohibit access to specific namespaces. Flags type references (including `var` inference) from prohibited namespaces. Supports prefix globbing with `*`. Per-pattern error/warn severity. |
| **EnforceNamingConventions** | AN0200 | Enforces configurable naming conventions via regex patterns. Phase 1: event naming (e.g., `On.*`). Configured via JSON-like MSBuild property. |
| **ExplicitEnums**         | AN0001 | Enum members must have explicit values. Inserting a member silently shifts all subsequent values.                                                                                   |
| **PublicConstAnalyzer**   | AN0002 | Warning: `public const` values are inlined into callers at compile time. Suppressible with `[PermanentConst]`.                                                                   |
| **StableABIVerification** | —     | MSBuild task that maintains a `$(AssemblyName).stableapi` file tracking all binary-level values baked into callers. (more thorough version of `Microsoft.CodeAnalysis.PublicApiAnalyzers`) |
| **VerifyUserConfigGitignore** | —     | MSBuild pre-build task that verifies user-config files are properly gitignored to prevent accidental commits of per-developer configuration. |
| **JsonPeek** | —     | MSBuild task + standalone CLI tool that reads values from JSON/JSONC/HJSON files by dot-separated key path. Extension-agnostic. |

## Installation

```xml
<PackageReference Include="AN.CodeAnalyzers" Version="0.1.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Project Structure

```
AN_CodeAnalyzers/
├── AN_CodeAnalyzers.sln
├── AN.CodeAnalyzers.csproj              ← analyzer DLL (Roslyn analyzers, netstandard2.0)
├── ExplicitEnums/                       ← AN0001 analyzer
│   ├── ExplicitEnumValuesAnalyzer.cs
│   ├── RequireExplicitEnumValuesAttribute.cs
│   ├── SuppressExplicitEnumValuesAttribute.cs
│   └── Tests/
├── PublicConstAnalyzer/                 ← AN0002 analyzer
│   ├── PublicConstAnalyzer.cs
│   ├── PermanentConstAttribute.cs
│   └── Tests/
├── RequireTypedPointersNotIntPtr/       ← AN0100 analyzer
│   ├── RequireTypedPointersNotIntPtrAnalyzer.cs
│   └── Tests/
├── CallersMustNameAllParameters/        ← AN0103 analyzer
│   ├── CallersMustNameAllParametersAttribute.cs
│   ├── CallersMustNameAllParametersAnalyzer.cs
│   └── Tests/
├── ProhibitPlatformImports/             ← AN0104 analyzer
│   ├── ProhibitPlatformImportsAnalyzer.cs
│   └── Tests/
├── ProhibitNamespaceAccess/             ← AN0105 analyzer
│   ├── ProhibitNamespaceAccessAnalyzer.cs
│   ├── ProhibitNamespaceAccessConfigParser.cs
│   └── Tests/
├── EnforceNamingConventions/            ← AN0200 analyzer
│   ├── EnforceNamingConventionsAnalyzer.cs
│   ├── NamingConventionRuleParser.cs
│   └── Tests/
├── StableABIVerification/               ← MSBuild task (separate project)
│   ├── StableABIVerification.csproj
│   ├── StableABISnapshotGenerator.cs    (SRM-based, reads compiled DLL)
│   ├── StableABIVerifyTask.cs           (MSBuild Task: verify + generate)
│   └── Tests/
├── CoreTools/                           ← MSBuild tasks + CLI tools (separate project)
│   ├── CoreTools.csproj                 (assembly: JsonPeekTask.dll, netstandard2.0)
│   ├── JsonPeekParser.cs                (HJSON/JSON/JSONC parser)
│   ├── JsonPeekTask.cs                  (MSBuild Task: JsonPeek)
│   ├── JsonPeekTool/                    (standalone CLI: JsonPeek.exe)
│   │   └── AN.CodeAnalyzers.JsonPeek.Tool.csproj
│   └── Tests/
├── SaferAssemblyLoader/                 ← standalone runtime library (separate NuGet package)
│   ├── ArtificialNecessity.SaferAssemblyLoader.csproj  (netstandard2.0)
│   ├── AssemblyManagedOnly.cs           (public API: LoadFrom, Load, IsManagedOnly, GetViolations)
│   ├── ManagedAssemblyInspector.cs      (PE metadata scanning engine)
│   ├── ManagedOnlyViolationException.cs
│   └── Tests/
├── build/
│   └── AN.CodeAnalyzers.targets
└── _SPECS/
```

### Layout conventions

- **Each analyzer area** (e.g. `ExplicitEnums/`, `StableABIVerification/`) is a top-level directory containing the analyzer source files.
- **Tests live inside each area** in a `Tests/` subdirectory with their own `.csproj`. This keeps tests co-located with the code they verify.
- **Test projects are listed at the solution root** — they appear as top-level projects in the `.sln` even though their files live inside analyzer subdirectories.
- **The analyzer `.csproj`** uses `<DefaultItemExcludes>$(DefaultItemExcludes);**/Tests/**</DefaultItemExcludes>` to prevent test files from being compiled into the analyzer assembly.

## Analyzers

### AN0100: Require typed pointers, not IntPtr

`IntPtr` is not safe. It erases type information at the exact boundary where it matters most. The compiler cannot distinguish an `HWND` from an `HPCON` from a raw memory address from a stale dangling pointer. You can assign a window handle to a console handle, increment a handle as if it were a pointer, or pass a handle value where a pointer-to-handle was expected. All of this compiles. None of it works.

This analyzer flags **any** use of `IntPtr` or `UIntPtr` anywhere in user code, and `nint`/`nuint` in P/Invoke declarations. There are no exceptions. Use typed structs for handles and `unsafe T*` for pointers.

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <RequireTypedPointersNotIntPtr>warn</RequireTypedPointersNotIntPtr>
</PropertyGroup>
```

| Value        | Behavior                                      |
| ------------ | --------------------------------------------- |
| `warn`     | Warning (default)                              |
| `disallow` | Error — build fails on any IntPtr usage       |
| `ignore`   | Disabled                                       |

**Recommended project organization:** Isolate native interop type definitions in a small dedicated project with `<RequireTypedPointersNotIntPtr>ignore</RequireTypedPointersNotIntPtr>`, and set `disallow` or `warn` in all other projects. This forces all untyped pointer manipulation into a single, reviewable location.

### AN0103: Callers must name all parameters

Enforces named arguments at call sites for methods with **2+ parameters**. Prevents LLM parameter-order confusion by making intent explicit at the call site.

**Why:** LLMs guess parameter order by vibes. A method like `ResolveHeight(float value)` gets called with whatever float is nearby. Named parameters make the intent visible: `ResolveHeight(resolvedWidth: availableHeight)` — the name mismatch is now obvious even before the compiler catches it.

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <RequireNamedArgumentsEverywhereLikeObjectiveC>attribute-error</RequireNamedArgumentsEverywhereLikeObjectiveC>
</PropertyGroup>
```

| Value | Behavior |
|-------|----------|
| `attribute-error` | Methods with `[CallersMustNameAllParameters]` require named args. Unnamed = **Error**. **(default)** |
| `attribute-warn` | Same as above but unnamed = **Warning** |
| `everywhere-error` | **Every** call site must name **every** argument. Unnamed = **Error**. (Objective-C style) |
| `everywhere-warn` | Same as above but unnamed = **Warning** |
| `ignore` | Disabled entirely |

**Combining values:** Comma-separated list supported. `attribute-error, everywhere-warn` means attribute-decorated methods produce errors, all other call sites produce warnings.

**Single-parameter methods are always exempt** — they're clear enough without naming.

**Example:**

```csharp
using AN.CodeAnalyzers.CallersMustNameAllParameters;

[CallersMustNameAllParameters]
public void SetMargin(float vertical, float horizontal) { }

// ✅ Compiles
SetMargin(vertical: 4, horizontal: 8);

// ❌ AN0103 error: "Argument 1 to 'SetMargin' must be named.
//                   Use named arguments for all parameters, e.g. MyMethod(argA: 1, argB: 2)"
SetMargin(4, 8);
```

### AN0104: Prohibit platform imports

Flags any platform import construct in a project that has `<ProhibitPlatformImports>` set. This is a project-level policy: when enabled, **no** P/Invoke or native library loading is allowed in that project.

**What it detects:**
- `[DllImport]` attributes on methods
- `[LibraryImport]` attributes on methods
- `[UnmanagedCallersOnly]` attributes on methods
- `NativeLibrary.Load()` calls
- `NativeLibrary.TryLoad()` calls

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <ProhibitPlatformImports>error</ProhibitPlatformImports>
</PropertyGroup>
```

| Value      | Behavior                        |
| ---------- | ------------------------------- |
| `disabled` | No diagnostics **(default)**    |
| `warn`     | Warning severity                |
| `error`    | Error — build fails            |

**Recommended project organization:** Isolate native interop in a small dedicated project with `<ProhibitPlatformImports>disabled</ProhibitPlatformImports>`, and set `error` in all other projects. This forces all platform-specific code into a single, reviewable location.

**Example diagnostics:**

```
AN0104: Platform import 'CloseHandle' is prohibited because <ProhibitPlatformImports> is set to 'error'
AN0104: Call to 'NativeLibrary.Load' is prohibited because <ProhibitPlatformImports> is set to 'error'
```

### AN0105: Prohibit namespace access

Prohibit access to specific namespaces in a project. Any type reference from a prohibited namespace produces a diagnostic — including types leaked through `var` inference. Patterns support prefix globbing with `*`.

**Configuration** via MSBuild property with JSON-like syntax:

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>{ error = [ "System.Runtime.InteropServices", "System.IO.MemoryMappedFiles" ], warn = [ "OpenTK.*" ] }</ProhibitNamespaceAccess>
</PropertyGroup>
```

| Key | Behavior |
|-----|----------|
| `error` | Error — build fails on any type reference from matching namespaces |
| `warn` | Warning severity |

**Pattern matching:**
- `System.Runtime.InteropServices` — exact namespace match only
- `System.Runtime.Interop*` — prefix glob: matches `InteropServices`, `InteropServices.Marshalling`, `InteropStuffNotInventedYet`
- `System.Runtime.*` — matches all child namespaces of `System.Runtime`

**Using directives** for prohibited namespaces always produce **warnings** (never errors) — they're superfluous cruft if you can't use anything in the namespace.

**Deduplication:** One diagnostic per unique prohibited type per file. If `MemoryMappedFile` appears 10 times, one diagnostic with the count.

**Transitive type exposure:** `var handle = Factory.Create()` — if the inferred type is from a prohibited namespace, it's flagged. No way to leak prohibited types through type inference.

**Example diagnostics:**

```
AN0105: Access to 'MemoryMappedFile' in namespace 'System.IO.MemoryMappedFiles' is prohibited by pattern 'System.IO.MemoryMappedFiles' in <ProhibitNamespaceAccess>
AN0105: Using directive for namespace 'System.Runtime.InteropServices' is prohibited by pattern 'System.Runtime.Interop*' in <ProhibitNamespaceAccess>. Remove this unused using directive.
```

See [`_TASKS/An0105_ProhibitNamespaceAccess.md`](_TASKS/An0105_ProhibitNamespaceAccess.md) for the full specification.

### AN0200: Enforce naming conventions

Enforces configurable naming conventions via regex patterns. **Currently supports: events only.** The architecture is designed for future expansion to methods, properties, fields, classes, interfaces, etc.

**Configuration** via MSBuild property with JSON-like syntax:

```xml
<PropertyGroup>
  <EnforceNamingConventions>{ event = "On.*" }</EnforceNamingConventions>
</PropertyGroup>
```

The value is a brace-delimited set of `key = "value"` pairs where:
- **key** = symbol category (currently: `event`; future: `method`, `property`, `field`, `class`, `interface`)
- **value** = regex pattern the symbol name must match (auto-anchored: `On.*` becomes `^(?:On.*)$`)

**Multiple rules** (for future expansion):

```xml
<EnforceNamingConventions>{ event = "On.*", interface = "I.*" }</EnforceNamingConventions>
```

**Disabled by default** — no diagnostics when property is absent or empty.

**Example diagnostic:**

```
AN0200: Event 'ButtonClick' does not match required naming pattern 'On.*'. Rename to match the convention.
```

**Configuration errors** are reported as AN0201 warnings if the JSON-like syntax is malformed or regex patterns are invalid.

**Supported symbol categories:**
- ✅ `event` — Event declarations (Phase 1)
- 🔜 `interface`, `class`, `struct`, `enum`, `method`, `property`, `field` — planned for Phase 2+

See [`_TASKS/30_IMPL_EnforceNamingConventions.md`](_TASKS/30_IMPL_EnforceNamingConventions.md) for the full roadmap.

### AN0001: Enum member must have explicit value

Prevents silent binary compatibility breaks caused by enum members without explicit integer values. When the C# compiler auto-increments enum values, inserting a member in the middle silently shifts all subsequent values — callers compiled against the old values get wrong behavior with no error or warning.

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <EnforceExplicitEnumValues>public</EnforceExplicitEnumValues>
</PropertyGroup>
```

| Value        | Behavior                                                  |
| ------------ | --------------------------------------------------------- |
| `public`   | Only public enums (default)                               |
| `all`      | All enums regardless of visibility                        |
| `explicit` | Only enums decorated with `[RequireExplicitEnumValues]` |
| `none`     | Disabled                                                  |

**Per-enum opt-out:**

```csharp
[SuppressExplicitEnumValues]
internal enum ThrowawayState { A, B, C }
```

**Per-enum opt-in** (useful with `explicit` scope):

```csharp
[RequireExplicitEnumValues]
internal enum ImportantState { Ready = 0, Running = 1, Done = 2 }
```

### AN0002: Public const warning

A **warning** (not error) that fires on `public const` fields in public types. The C# compiler inlines const values into the caller's assembly, so changing a const value in a library silently breaks consumers unless they recompile.

**Suppress with `[PermanentConst]`** for values that genuinely never change:

```csharp
using AN.CodeAnalyzers.StableABIVerification;

public class MathConstants
{
    [PermanentConst]
    public const double Pi = 3.14159265358979;  // no warning
  
    public const int MaxRetries = 3;            // AN0002 warning
}
```

### StableABI Snapshot Verification

An MSBuild task that maintains a `$(AssemblyName).stableapi` file (e.g. `MyLibrary.stableapi`) recording every binary-level value the compiler bakes into callers. A more thorough replacement for `Microsoft.CodeAnalysis.PublicApiAnalyzers` — tracks actual values, not just API surface names.

**Enable** in your `.csproj`:

```xml
<PropertyGroup>
  <StableABISnapshotScope>public</StableABISnapshotScope>  <!-- public | all -->
</PropertyGroup>
```

**What the snapshot tracks:**

- Enum member values and underlying types
- `const` field values
- Default parameter values
- Struct field order and layout (`Sequential`/`Explicit`)
- P/Invoke parameter types

**Workflow:**

1. Set `StableABISnapshotScope` in your `.csproj`
2. Generate initial snapshot: `dotnet msbuild -t:UpdateStableABISnapshot`
3. Commit `$(AssemblyName).stableapi` to source control
4. On subsequent builds, any ABI change produces a build error with a detailed diff:
   ```
   StableABI snapshot mismatch: 3 change(s) detected.
     CHANGED: enum.PixelFormat.R8UNorm: 1 -> 2
     ADDED:   enum.PixelFormat.NewFormat: 99
     REMOVED: const.MyClass.OldValue: int 7
   ```
5. To accept intentional changes: `dotnet msbuild -t:UpdateStableABISnapshot`

### VerifyUserConfigGitignore

An MSBuild pre-build task that verifies user-config files are properly gitignored to prevent accidental commits of per-developer configuration files.

**Enable** in your `.csproj`:

```xml
<PropertyGroup>
  <VerifyUserConfigGitignore>true</VerifyUserConfigGitignore>
</PropertyGroup>
```

**Files verified** (hardcoded list):
- `Directory.Build.props` — per-developer build customization
- `Directory.Build.targets` — per-developer build targets
- `Directory.Packages.props` — central package management
- `global.json` — SDK version pinning
- `nuget.config` — NuGet feed configuration
- `.editorconfig` — editor preferences

**Severity control** (optional):

```xml
<PropertyGroup>
  <VerifyUserConfigGitignoreSeverity>warning</VerifyUserConfigGitignoreSeverity>
</PropertyGroup>
```

| Value | Behavior |
|---|---|
| `error` | Build errors (default) — build fails if files not ignored |
| `warning` | Build warnings — build continues |

**Example error output:**

```
VerifyUserConfigGitignore: 2 file(s) not covered by .gitignore.
  NOT IGNORED: Directory.Build.targets
  NOT IGNORED: global.json
Add these entries to your .gitignore to prevent accidental commits of local configuration.
```

### JsonPeek

An MSBuild task and standalone CLI tool that reads values from JSON, JSONC (JSON with comments), or HJSON files by dot-separated key path. Uses the [Hjson](https://hjson.github.io/) parser which is a superset of JSON — handles all three formats transparently regardless of file extension.

**MSBuild usage:**

```xml
<JsonPeek File="config.hjson" KeyPath="version">
  <Output TaskParameter="Value" PropertyName="ConfigVersion" />
</JsonPeek>

<!-- Nested key paths with dot notation -->
<JsonPeek File="package.json" KeyPath="dependencies.Newtonsoft.Json">
  <Output TaskParameter="Value" PropertyName="NewtonsoftVersion" />
</JsonPeek>
```

**Standalone CLI usage:**

```bash
# Read a top-level key
JsonPeek config.json version
# Output: 1.0.0

# Read a nested key
JsonPeek package.json dependencies.Hjson
# Output: 3.0.0

# Works with HJSON (unquoted keys/values, comments)
JsonPeek config.hjson database.host
# Output: localhost
```

**Supported formats** (detected by content, not extension):
- **JSON** — standard `{ "key": "value" }`
- **JSONC** — JSON with `//` and `/* */` comments
- **HJSON** — Human JSON: unquoted keys/values, comments, multiline strings

**Task parameters:**

| Parameter | Direction | Description |
|---|---|---|
| `File` | Input (required) | Path to the JSON/JSONC/HJSON file |
| `KeyPath` | Input (required) | Dot-separated key path (e.g. `version` or `parent.child.key`) |
| `Value` | Output | The extracted value as a string |

## SaferAssemblyLoader

A standalone runtime library that loads .NET assemblies with a managed-only guarantee. Inspects PE metadata **before** loading — if the assembly contains `[DllImport]`, `IntPtr`, `Marshal.*` calls, or unsafe IL, it throws before the assembly enters your AppDomain.

**Separate NuGet package:** `ArtificialNecessity.SaferAssemblyLoader` — no dependency on Roslyn or the analyzer package.

```csharp
using ArtificialNecessity.SaferAssemblyLoader;

// Load a plugin — if it touches native code, it doesn't load
try
{
    Assembly plugin = AssemblyManagedOnly.LoadFrom(pluginPath);
    // safe to use
}
catch (ManagedOnlyViolationException ex)
{
    logger.Error($"Rejected plugin: {ex.Message}");
    // the assembly was NEVER loaded — your process is clean
}

// Or just check without loading
bool isSafe = AssemblyManagedOnly.IsManagedOnly(dllPath);
IReadOnlyList<string> violations = AssemblyManagedOnly.GetViolations(dllPath);
```

**What it detects:**
- `[DllImport]` and `[LibraryImport]` P/Invoke methods
- `IntPtr`/`UIntPtr` in fields and method signatures
- `Marshal.*` method calls
- Unsafe IL opcodes (pointer loads/stores, `localloc`, `cpblk`)
- Native/Unmanaged method implementations
- Mixed-mode assemblies (PE `ILOnly` flag not set)

See [`_TASKS/20_AN_SaferAssemblyLoader.md`](_TASKS/20_AN_SaferAssemblyLoader.md) for the full specification.

## Building

```bash
dotnet build
```

## Running Tests

```bash
dotnet test
```

## NOTE: No `Directory.Build.props` by design

This repository **intentionally does not include** a `Directory.Build.props` file. This is so that developers who clone the repo can create their own local `Directory.Build.props` to customize build output paths (e.g. redirect `bin/` and `obj/` to an `artifacts/` directory), set local signing keys, or apply any other per-machine build customizations — without risk of conflicting with a committed version.

`Directory.Build.props` is listed in [`.gitignore`](.gitignore) to prevent accidental commits of local overrides.

If you want to redirect build output locally, create a `Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
    <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```
