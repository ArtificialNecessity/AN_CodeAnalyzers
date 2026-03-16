# AN.CodeAnalyzers

Roslyn code analyzers for preventing silent binary compatibility breaks in C# projects.

## Installation

```xml
<PackageReference Include="AN.CodeAnalyzers" Version="0.1.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Project Structure

```
AN_CodeAnalyzers/
├── AN_CodeAnalyzers.sln
├── AN.CodeAnalyzers.csproj          ← analyzer package (netstandard2.0)
├── ExplicitEnums/
│   ├── ExplicitEnumValuesAnalyzer.cs       (AN0001)
│   ├── RequireExplicitEnumValuesAttribute.cs
│   ├── SuppressExplicitEnumValuesAttribute.cs
│   └── Tests/
│       ├── AN.CodeAnalyzers.ExplicitEnums.Tests.csproj
│       ├── AnalyzerVerifierHelper.cs
│       └── ExplicitEnumValuesAnalyzerTests.cs
├── StableABIVerification/                  (planned)
│   └── Tests/
├── build/
│   └── AN.CodeAnalyzers.targets            (planned)
└── _SPECS/
    └── 10_IMPL_StableABIVerification.md
```

### Layout conventions

- **Each analyzer area** (e.g. `ExplicitEnums/`, `StableABIVerification/`) is a top-level directory containing the analyzer source files.
- **Tests live inside each area** in a `Tests/` subdirectory with their own `.csproj`. This keeps tests co-located with the code they verify.
- **Test projects are listed at the solution root** — they appear as top-level projects in the `.sln` even though their files live inside analyzer subdirectories.
- **The analyzer `.csproj`** uses `<DefaultItemExcludes>$(DefaultItemExcludes);**/Tests/**</DefaultItemExcludes>` to prevent test files from being compiled into the analyzer assembly.

## Analyzers

### AN0001: Enum member must have explicit value

Prevents silent binary compatibility breaks caused by enum members without explicit integer values. When the C# compiler auto-increments enum values, inserting a member in the middle silently shifts all subsequent values — callers compiled against the old values get wrong behavior with no error or warning.

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <EnforceExplicitEnumValues>public</EnforceExplicitEnumValues>
</PropertyGroup>
```

| Value      | Behavior |
|------------|----------|
| `public`   | Only public enums (default) |
| `all`      | All enums regardless of visibility |
| `explicit` | Only enums decorated with `[RequireExplicitEnumValues]` |
| `none`     | Disabled |

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