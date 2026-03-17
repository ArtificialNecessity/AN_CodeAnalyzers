# VerifyUserConfigGitignore

## Purpose

Verify that a consuming project's `.gitignore` properly ignores user-specific configuration files that should never be committed to source control. These files are intended for per-developer local customization (build output paths, signing keys, NuGet sources, editor preferences, etc.) and committing them causes merge conflicts and overrides developer preferences.

This is an MSBuild pre-build task that ships in the `AN.CodeAnalyzers` NuGet package.

## Motivation

Projects that use `Directory.Build.props` for local build customization (e.g. redirecting `bin/`/`obj/` to an `artifacts/` directory) need to ensure these files are gitignored. Without enforcement:

- A developer accidentally commits their `Directory.Build.props`, breaking other developers' builds
- An AI assistant adds a `global.json` pinning an SDK version that doesn't exist on other machines
- A `nuget.config` with private feed credentials gets committed

This task catches these mistakes at build time.

## Configuration

Consuming projects opt in via MSBuild property:

```xml
<PropertyGroup>
  <VerifyUserConfigGitignore>true</VerifyUserConfigGitignore>
</PropertyGroup>
```

When absent or `false`, the task does not run.

### Severity control

By default, unignored files produce **build errors**. To downgrade to warnings instead:

```xml
<PropertyGroup>
  <VerifyUserConfigGitignoreSeverity>warning</VerifyUserConfigGitignoreSeverity>
</PropertyGroup>
```

| Value | Behavior |
|---|---|
| `error` | Build errors (default) |
| `warning` | Build warnings — build continues |

## Files verified

The following files are hardcoded in the task. Each must be covered by the project's `.gitignore` — either by an explicit entry or by a glob pattern:

| File | Why it must not be tracked |
|---|---|
| `Directory.Build.props` | Per-developer build customization — output paths, signing, SDK settings |
| `Directory.Build.targets` | Per-developer build targets — custom pre/post-build steps |
| `Directory.Packages.props` | Central package management — version pinning varies per environment |
| `global.json` | SDK version pinning — different developers may need different SDKs |
| `nuget.config` | NuGet feed configuration — may contain private feed URLs or credentials |
| `.editorconfig` | Editor preferences — tabs vs spaces, line endings, formatting rules |

## Behavior

### Pre-build check

The task runs as a `BeforeTargets="Build"` target. It:

1. Locates the `.gitignore` file by searching upward from `$(MSBuildProjectDirectory)` toward the repository root (stops at the first `.gitignore` found, or at a `.git/` directory)
2. Parses the `.gitignore` using proper gitignore pattern matching (respecting globs, `*`, `**`, `?`, negation with `!`, directory-only patterns with trailing `/`, comments with `#`)
3. For each hardcoded file, checks whether the filename would be ignored by the parsed rules
4. If any file is NOT ignored: **build error** listing each unignored file

### Error output

```
VerifyUserConfigGitignore: 3 file(s) not covered by .gitignore.
  NOT IGNORED: Directory.Build.targets
  NOT IGNORED: global.json
  NOT IGNORED: nuget.config
Add these entries to your .gitignore to prevent accidental commits of local configuration.
```

### Edge cases

- **No `.gitignore` found**: Build error — "No .gitignore file found. Create one and add entries for user-config files."
- **`.gitignore` exists but is empty**: Build error listing all 6 files
- **Pattern coverage**: `*.props` in `.gitignore` would satisfy both `Directory.Build.props` and `Directory.Packages.props`
- **Negation**: If `.gitignore` has `Directory.Build.props` but also `!Directory.Build.props`, the file is NOT ignored (negation wins per gitignore rules)

## Implementation

### Project structure

```
VerifyUserConfigGitignore/
├── VerifyUserConfigGitignore.csproj     (netstandard2.0, MSBuild task)
├── VerifyUserConfigGitignoreTask.cs     (MSBuild Task class)
└── Tests/
    ├── AN.CodeAnalyzers.VerifyUserConfigGitignore.Tests.csproj
    └── VerifyUserConfigGitignoreTaskTests.cs
```

### VerifyUserConfigGitignoreTask.cs

An MSBuild `Task` (inherits `Microsoft.Build.Utilities.Task`) with:

- **Input**: `ProjectDirectory` (string, required) — `$(MSBuildProjectDirectory)`
- **Input**: `Severity` (string, optional, default `"error"`) — `"error"` or `"warning"`
- **Output**: success/failure via `Log.LogError()` or `Log.LogWarning()` depending on severity

The task:
1. Walks up from `ProjectDirectory` looking for `.gitignore` (stops at `.git/` directory or filesystem root)
2. Reads and parses the `.gitignore`
3. Checks each hardcoded filename against the parsed patterns
4. Reports errors for any unmatched files

### Gitignore matching via MAB.DotIgnore

Use the `MAB.DotIgnore` NuGet package for proper gitignore pattern matching. This package implements the full gitignore specification (globs, negation, directory-only patterns, comments, etc.) and is well-tested. No reason to reimplement what already exists.

The `.csproj` should reference:

```xml
<PackageReference Include="MAB.DotIgnore" Version="*" />
```

The task uses `MAB.DotIgnore.IgnoreList` to parse the `.gitignore` file and check each filename against it.

### MSBuild integration in AN.CodeAnalyzers.targets

```xml
<!-- VerifyUserConfigGitignore pre-build check -->
<UsingTask
  TaskName="AN.CodeAnalyzers.VerifyUserConfigGitignore.VerifyUserConfigGitignoreTask"
  AssemblyFile="$(MSBuildThisFileDirectory)..\tasks\netstandard2.0\VerifyUserConfigGitignore.dll"
  TaskFactory="TaskHostFactory"
  Condition="Exists('$(MSBuildThisFileDirectory)..\tasks\netstandard2.0\VerifyUserConfigGitignore.dll')" />

<!-- Fallback for local development -->
<UsingTask
  TaskName="AN.CodeAnalyzers.VerifyUserConfigGitignore.VerifyUserConfigGitignoreTask"
  AssemblyFile="$(MSBuildThisFileDirectory)..\VerifyUserConfigGitignore\bin\$(Configuration)\netstandard2.0\VerifyUserConfigGitignore.dll"
  TaskFactory="TaskHostFactory"
  Condition="!Exists('$(MSBuildThisFileDirectory)..\tasks\netstandard2.0\VerifyUserConfigGitignore.dll')" />

<PropertyGroup>
  <VerifyUserConfigGitignoreSeverity Condition="'$(VerifyUserConfigGitignoreSeverity)' == ''">error</VerifyUserConfigGitignoreSeverity>
</PropertyGroup>

<Target Name="VerifyUserConfigGitignore"
        BeforeTargets="Build"
        Condition="'$(VerifyUserConfigGitignore)' == 'true'">
  <VerifyUserConfigGitignoreTask
    ProjectDirectory="$(MSBuildProjectDirectory)"
    Severity="$(VerifyUserConfigGitignoreSeverity)" />
</Target>
```

### Packaging in AN.CodeAnalyzers.csproj

Add to the pack ItemGroup:

```xml
<None Include="VerifyUserConfigGitignore\bin\$(Configuration)\netstandard2.0\VerifyUserConfigGitignore.dll"
      Pack="true" PackagePath="tasks/netstandard2.0" Visible="false" />
```

## Gitignore matching — implementation notes

Uses the `MAB.DotIgnore` NuGet package for proper gitignore pattern matching. The task creates an `IgnoreList` from the `.gitignore` file content and calls `IsIgnored()` for each filename to check.

This avoids reimplementing the gitignore specification and ensures correct handling of all edge cases (globs, negation, character classes, directory-only patterns, etc.).

## Non-goals

- Does not modify `.gitignore` — only verifies
- Does not check whether files are actually tracked by git — only checks gitignore rules
- Does not verify nested `.gitignore` files — only the nearest ancestor
- Does not support configurable file lists — the list is hardcoded for consistency

## Testing

### VerifyUserConfigGitignoreTaskTests

- All files covered by exact entries → task succeeds
- All files covered by glob patterns (e.g. `*.props`) → task succeeds
- Some files missing → task fails with correct error messages listing each unignored file
- No `.gitignore` found → task fails with appropriate message
- Negation pattern undoes coverage → task correctly reports file as not ignored
- Empty `.gitignore` → task fails listing all 6 files