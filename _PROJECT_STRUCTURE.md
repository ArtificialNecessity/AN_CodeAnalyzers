# AN.CodeAnalyzers — Project Structure

## Overview

This repository contains **Roslyn code analyzers**, **MSBuild tasks**, and a **runtime library** for enforcing code quality, preventing silent binary compatibility breaks, and restricting unsafe patterns in C# projects. It produces two independent NuGet packages:

| Package | Type | Description |
|---------|------|-------------|
| `ArtificialNecessity.CodeAnalyzers` | Build-time (development dependency) | Roslyn analyzers + MSBuild tasks |
| `ArtificialNecessity.SaferAssemblyLoader` | Runtime library | Load assemblies with managed-only guarantee |

**Target framework:** `netstandard2.0` (analyzers + tasks), `net8.0` (tests + CLI tools)
**Roslyn version:** `Microsoft.CodeAnalysis.CSharp 4.8.0`
**Test framework:** xUnit 2.7 + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.2`

---

## Directory Layout

```
AN_CodeAnalyzers/
├── AN_CodeAnalyzers.sln                    ← Solution file (all projects)
├── AN.CodeAnalyzers.csproj                  ← Main analyzer DLL (netstandard2.0, packed as NuGet)
├── build/
│   └── ArtificialNecessity.CodeAnalyzers.targets  ← MSBuild .targets shipped in NuGet package
├── AN.CodeAnalyzers.shared.Build.props      ← Shared build infrastructure (versioning, NuGet deploy)
│
├── ExplicitEnums/                           ← AN0001: Enum members must have explicit values
│   ├── ExplicitEnumValuesAnalyzer.cs
│   ├── ExplicitEnumValuesAnalyzer.cs
│   ├── RequireExplicitEnumValuesAttribute.cs
│   ├── SuppressExplicitEnumValuesAttribute.cs
│   └── Tests/
│       ├── AN.CodeAnalyzers.ExplicitEnums.Tests.csproj
│       ├── AnalyzerVerifierHelper.cs
│       └── ExplicitEnumValuesAnalyzerTests.cs
│
├── PublicConstAnalyzer/                     ← AN0002: Warning on public const fields
│   ├── PublicConstAnalyzer.cs
│   ├── PermanentConstAttribute.cs
│   └── Tests/
│
├── RequireTypedPointersNotIntPtr/           ← AN0100: Ban IntPtr/UIntPtr usage
│   ├── RequireTypedPointersNotIntPtrAnalyzer.cs
│   └── Tests/
│
├── CallersMustNameAllParameters/            ← AN0103: Enforce named arguments at call sites
│   ├── CallersMustNameAllParametersAnalyzer.cs
│   ├── CallersMustNameAllParametersAttribute.cs
│   └── Tests/
│
├── ProhibitPlatformImports/                 ← AN0104: Ban DllImport/LibraryImport/NativeLibrary
│   ├── ProhibitPlatformImportsAnalyzer.cs
│   └── Tests/
│
├── ProhibitNamespaceAccess/                 ← AN0105: Block access to specific namespaces
│   ├── ProhibitNamespaceAccessAnalyzer.cs
│   ├── ProhibitNamespaceAccessConfigParser.cs
│   └── Tests/
│
├── EnforceNamingConventions/                ← AN0200: Regex-based naming convention enforcement
│   ├── EnforceNamingConventionsAnalyzer.cs
│   ├── NamingConventionRuleParser.cs
│   └── Tests/
│
├── StableABIVerification/                   ← MSBuild task: binary ABI snapshot verification
│   ├── StableABIVerification.csproj         (separate project, netstandard2.0)
│   ├── StableABISnapshotGenerator.cs
│   ├── StableABIVerifyTask.cs
│   └── Tests/
│
├── VerifyUserConfigGitignore/               ← MSBuild task: verify config files are gitignored
│   ├── VerifyUserConfigGitignore.csproj     (separate project, netstandard2.0)
│   ├── VerifyUserConfigGitignoreTask.cs
│   └── Tests/
│
├── CoreTools/                               ← MSBuild task + CLI: JsonPeek (read JSON/HJSON)
│   ├── CoreTools.csproj
│   ├── JsonPeekParser.cs
│   ├── JsonPeekTask.cs
│   ├── JsonPeekTool/                        (standalone CLI exe, net8.0)
│   │   └── AN.CodeAnalyzers.JsonPeek.Tool.csproj
│   └── Tests/
│
├── ClassLibInfo/                            ← API dump generation tool
│   ├── ClassLibInfoLib.csproj
│   ├── ApiDumpGenerator.cs
│   ├── ClassLibInfoTool/
│   └── Tests/
│
├── SaferAssemblyLoader/                     ← Standalone runtime library (separate NuGet)
│   ├── ArtificialNecessity.SaferAssemblyLoader.csproj
│   ├── AssemblyManagedOnly.cs
│   ├── ManagedAssemblyInspector.cs
│   ├── ManagedOnlyViolationException.cs
│   └── Tests/
│
├── cmd/                                     ← Build/publish scripts (PowerShell)
│   ├── build.ps1
│   ├── publish-local.ps1
│   ├── publish-nuget-codeanalyzers.ps1
│   └── publish-nuget-saferassemblyloader.ps1
│
├── docs/                                    ← Documentation articles
├── _SPECS/                                  ← Implementation specifications
├── _TASKS/                                  ← Task/feature implementation plans
│
├── README.md                                ← Full project documentation
├── README-nuget.md                          ← Stripped-down README for NuGet.org
└── LICENSE.txt                              ← Apache 2.0
```

---

## Architecture Conventions

### Analyzer Source Organization

- **Each analyzer lives in its own top-level directory** (e.g., `ExplicitEnums/`, `CallersMustNameAllParameters/`).
- **Tests live inside each analyzer directory** in a `Tests/` subdirectory with their own `.csproj`.
- **Test projects are listed at the solution root** — they appear as top-level projects in the `.sln` even though their files are nested.
- The main `AN.CodeAnalyzers.csproj` uses `<DefaultItemExcludes>` to prevent test files and separate-project directories from being compiled into the analyzer DLL.

### How Analyzers Read Configuration

Analyzers are configured via **MSBuild properties** in the consuming project's `.csproj`. These properties are made visible to the Roslyn analyzer at compile time via `<CompilerVisibleProperty>` entries in `build/ArtificialNecessity.CodeAnalyzers.targets`.

At runtime in the analyzer, the property is read via:
```csharp
options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree)
    .TryGetValue("build_property.PropertyName", out var value);
```

### Attribute Pattern

When an analyzer uses a marker attribute (e.g., `[CallersMustNameAllParameters]`, `[PermanentConst]`):
1. The attribute class lives alongside the analyzer in the same directory.
2. It's compiled into the `AN.CodeAnalyzers.dll` assembly.
3. In tests, the attribute source is included as a string literal in a `VerifierHelper` class so the test compilation can resolve it.
4. The analyzer matches attributes **by name string** (not by type reference) to avoid assembly-loading issues.

### Test Pattern

Each test project follows this pattern:
- **`*VerifierHelper.cs`** — Static helper class that:
  - Embeds the attribute source text (if applicable)
  - Creates `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` instances
  - Injects `.globalconfig` content for MSBuild property simulation
  - Provides `Expect*` methods for building `DiagnosticResult`
- **`*Tests.cs`** — xUnit test class with `[Fact]` methods that:
  - Define inline source code as `const string`
  - Use `{|#0:code|}` markup for expected diagnostic locations
  - Call the helper to create and run tests

### NuGet Packaging

The `AN.CodeAnalyzers.csproj` packs everything into a single NuGet:
- `analyzers/dotnet/cs/` — The analyzer DLL itself
- `build/` — The `.targets` file (auto-imported by consuming projects)
- `tasks/netstandard2.0/` — MSBuild task DLLs (StableABI, VerifyGitignore, JsonPeek)
- `tools/net8.0/any/` — CLI tools (invoked via `dotnet JsonPeek.dll`, `dotnet ClassLibInfo.dll`)

### Versioning

Timestamp-based versioning (v2) via `AN.CodeAnalyzers.shared.Build.props`. Every build
gets a unique version automatically — no version files, no generated props, no scripts.

```
AssemblyVersion      = {major}.{YYMM}.{DDHH}.{mmss}
FileVersion          = (same)
Version              = {major}.{YYMMDD}.{HHmmss}      (3-segment, used as PackageVersion)
InformationalVersion = {AssemblyVersion}-{MACHINE}+g{gitshort}
```

Just `dotnet build` — zero ceremony.

---

## Building & Testing

```bash
dotnet build              # Build everything
dotnet test               # Run all tests
dotnet pack               # Create NuGet package
```

## Key Design Decisions

1. **No `Directory.Build.props`** — Intentionally omitted so developers can create local overrides (gitignored).
2. **netstandard2.0 for analyzers** — Required by Roslyn analyzer hosting infrastructure.
3. **Attribute matching by name** — Avoids assembly version conflicts when consumers reference different versions.
4. **MSBuild properties for configuration** — No `.editorconfig` rules; everything is project-level via `<PropertyGroup>`.
5. **`TaskHostFactory` for MSBuild tasks** — Runs tasks out-of-process to avoid DLL locking during development.