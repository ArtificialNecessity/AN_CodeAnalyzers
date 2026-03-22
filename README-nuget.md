# AN.CodeAnalyzers

Roslyn code analyzers and MSBuild tools for preventing silent binary compatibility breaks in C# projects.

[Discussions at Github](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/discussions/)

## Analyzer Summary

| Verifier                            | Rule   | Description                                                                                                                                                                                    |
| ----------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
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
