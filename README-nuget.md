# AN.CodeAnalyzers

Roslyn code analyzers and MSBuild tools for preventing silent binary compatibility breaks in C# projects.

[Discussions at Github](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/discussions/)

## Analyzer Summary

| Verifier                            | Rule   | Description                                                                                                                                                                                    |
| ----------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **RequireTypedPointersNotIntPtr**         | AN0100 | Flags any use of `IntPtr`/`UIntPtr` everywhere and `nint`/`nuint` in P/Invoke declarations. These types erase type information, enable silent type confusion, and create security vulnerabilities. No exceptions. |
| **CallersMustNameAllParameters**    | AN0103 | Enforces named arguments at call sites for methods with 2+ parameters. Attribute-driven or everywhere mode. Prevents LLM parameter-order confusion. |
| **EnforceNamingConventions**  | AN0200 | Enforces configurable naming conventions via regex patterns. Phase 1: event naming (e.g., `On.*`). Configured via JSON-like MSBuild property. |
| **ExplicitEnums**             | AN0001 | Enum members must have explicit values. Inserting a member silently shifts all subsequent values.                                                                                              |
| **PublicConstAnalyzer**       | AN0002 | Warning:`public const` values are inlined into callers at compile time. Suppressible with `[PermanentConst]`.                                                                              |
| **StableABIVerification**     | —     | MSBuild task that maintains a `$(AssemblyName).stableapi` file tracking all binary-level values baked into callers. (more thorough version of `Microsoft.CodeAnalysis.PublicApiAnalyzers`) |
| **VerifyUserConfigGitignore** | —     | MSBuild pre-build task that verifies user-config files are properly gitignored to prevent accidental commits of per-developer configuration.                                                   |
| **JsonPeek**                  | —     | MSBuild task + standalone CLI tool that reads and writes individual values from JSON/JSONC/HJSON files by dot-separated key path. Extension-agnostic.                                          |

## Installation

```xml
<PackageReference Include="ArtificialNecessity.CodeAnalyzers" Version="0.1.9">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Analyzers

### AN0100: Require typed pointers, not IntPtr

`IntPtr` is not safe. It erases type information at the exact boundary where it matters most — the compiler cannot distinguish an `HWND` from an `HPCON` from a raw memory address from a stale dangling pointer. You can assign a window handle to a console handle, increment a handle as if it were a pointer, or pass a handle value where a pointer-to-handle was expected. All of this compiles. None of it works.

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

**Recommended:** Isolate native interop type definitions in a small dedicated project with `<RequireTypedPointersNotIntPtr>ignore</RequireTypedPointersNotIntPtr>`, and set `disallow` or `warn` in all other projects.

### AN0103: Callers must name all parameters

Enforces named arguments at call sites for methods with **2+ parameters**. Prevents LLM parameter-order confusion by making intent explicit.

**Why:** LLMs guess parameter order by vibes. A method like `ResolveHeight(float value)` gets called with whatever float is nearby. Named parameters make the intent visible: `ResolveHeight(resolvedWidth: availableHeight)` — the name mismatch is now obvious.

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
| `everywhere-error` | **Every** call site must name **every** argument. Unnamed = **Error**. |
| `everywhere-warn` | Same as above but unnamed = **Warning** |
| `ignore` | Disabled entirely |

**Combining values:** `attribute-error, everywhere-warn` means attribute-decorated methods produce errors, all other call sites produce warnings.

**Single-parameter methods are always exempt** — they're clear enough without naming.

**Example:**

```csharp
using AN.CodeAnalyzers.CallersMustNameAllParameters;

[CallersMustNameAllParameters]
public void SetMargin(float vertical, float horizontal) { }

// ✅ Compiles
SetMargin(vertical: 4, horizontal: 8);

// ❌ AN0103 error
SetMargin(4, 8);
```

### AN0200: Enforce naming conventions

Enforces configurable naming conventions via regex patterns. **Currently supports: events only.**

**Configuration:**

```xml
<PropertyGroup>
  <EnforceNamingConventions>{ event = "On.*" }</EnforceNamingConventions>
</PropertyGroup>
```

- **key** = symbol category (currently: `event`; future: `method`, `property`, `field`, `class`, `interface`)
- **value** = regex pattern (auto-anchored: `On.*` → `^(?:On.*)$`)

**Example diagnostic:**

```
AN0200: Event 'ButtonClick' does not match required naming pattern 'On.*'. Rename to match the convention.
```

**Disabled by default** — no diagnostics when property is absent.

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

| Value       | Behavior                                                   |
| ----------- | ---------------------------------------------------------- |
| `error`   | Build errors (default) — build fails if files not ignored |
| `warning` | Build warnings — build continues                          |

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

| Parameter   | Direction        | Description                                                      |
| ----------- | ---------------- | ---------------------------------------------------------------- |
| `File`    | Input (required) | Path to the JSON/JSONC/HJSON file                                |
| `KeyPath` | Input (required) | Dot-separated key path (e.g.`version` or `parent.child.key`) |
| `Value`   | Output           | The extracted value as a string                                  |

## License

Apache License, Version 2.0 — see [LICENSE](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/LICENSE.txt)

## Source

[github.com/ArtificialNecessity/AN_CodeAnalyzers](https://github.com/ArtificialNecessity/AN_CodeAnalyzers)
