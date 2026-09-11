# AN.CodeAnalyzers

Roslyn code analyzers and MSBuild tools for preventing silent binary compatibility breaks in C# projects.

[Discussions at Github](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/discussions/)

## Analyzer Summary

| Verifier                            | Rule   | Description                                                                                                                                                                                    |
| ----------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **RequireTypedPointersNotIntPtr**         | AN0100 | Flags any use of `IntPtr`/`UIntPtr` everywhere and `nint`/`nuint` in P/Invoke declarations. These types throw away type checking the compiler can do between distinct native handle/pointer types, enable silent type confusion, and create security vulnerabilities. No exceptions. |
| **CallersMustNameAllParameters**    | AN0103 | Enforces named arguments at call sites for methods with 2+ parameters. Attribute-driven or everywhere mode. Prevents LLM parameter-order confusion. |
| **EnforceNamingConventions**  | AN0200 | Enforces configurable naming conventions via regex patterns. Phase 1: event naming (e.g., `On.*`). Configured via JSON-like MSBuild property. |
| **ExplicitEnums**             | AN0001 | Enum members must have explicit values. Inserting a member silently shifts all subsequent values.                                                                                              |
| **PublicConstAnalyzer**       | AN0002 | Warning:`public const` values are inlined into callers at compile time. Suppressible with `[PermanentConst]`.                                                                              |
| **PureFunction**              | AN0501 | Flags instance state mutation inside `[PureFunction]` methods. Attribute-driven, `Inherited = true` so overrides are automatically constrained. Error severity. |
| **StableABIVerification**     | —     | MSBuild task that maintains a `$(AssemblyName).stableapi` file tracking all binary-level values baked into callers. (more thorough version of `Microsoft.CodeAnalysis.PublicApiAnalyzers`) |
| **VerifyUserConfigGitignore** | —     | MSBuild pre-build task that verifies user-config files are properly gitignored to prevent accidental commits of per-developer configuration.                                                   |
| **JsonPeek**                  | —     | MSBuild task + standalone CLI tool that reads and writes individual values from JSON/JSONC/HJSON files by dot-separated key path. Extension-agnostic.                                          |
| **ClassLibInfo**              | —     | Library + standalone CLI tool that generates dense API surface dumps from compiled assemblies. Outputs HJSON or flat text. Designed for AI context windows. |

## Installation

```xml
<PackageReference Include="ArtificialNecessity.CodeAnalyzers" Version="*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Analyzers

### AN0100: Require typed pointers, not IntPtr

`IntPtr` is not safe. It throws away type checking the compiler can do, at the exact boundary where it matters most — the compiler cannot distinguish an `HWND` from an `HPCON` from a raw memory address from a stale dangling pointer. You can assign a window handle to a console handle, increment a handle as if it were a pointer, or pass a handle value where a pointer-to-handle was expected. All of this compiles. None of it works. `void*` is the same mistake with a different spelling, and `SafeHandle` is an `IntPtr` with a finalizer — lifetime safety, zero type safety. (Why AN0100 and AN0102 exist at all: see "friction, not a sandbox" under AN0102.)

This analyzer flags **any** use of `IntPtr`, `UIntPtr` or `void*` anywhere in user code, and warns on `nint`/`nuint` in P/Invoke declarations (correct for `SIZE_T`/`DWORD_PTR` integers, wrong for handles). There are no exceptions.

**The idiom** — handles are *empty* `unsafe` marker structs and the pointer *is* the handle:

```csharp
unsafe struct HWND  { }                       // no payload — the TYPE is the information
unsafe struct HFILE { }                       // HWND* and HFILE* are different types: cross-assignment and arithmetic are compile errors

[DllImport("user32")]   static extern unsafe bool SetForegroundWindow(HWND* hWnd);
[DllImport("kernel32")] static extern unsafe bool ReadFile(HFILE* hFile, byte* buffer, uint bytesToRead, uint* bytesRead, OVERLAPPED* overlapped);
[DllImport("kernel32")] static extern unsafe int  CreatePseudoConsole(COORD size, HFILE* hIn, HFILE* hOut, uint flags, HPCON** phPC);   // out-param = T**
// null handle: (HWND*)null — never IntPtr.Zero, never default(HWND). Sizes/bitfields: nuint.
```

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

**Recommended:** `disallow` everywhere, *including* the interop project — the idiom contains no `IntPtr` and no `void*`, so the interop project builds clean too. `ignore` is a rollout value for unconverted projects, never a destination. Pair with **AN0102** (below) so untyped pointers that reach your code *without* being spelled `IntPtr` are rejected as well.

### AN0102: Prohibit reachable untyped native pointers

AN0100 catches the untyped pointers **you spelled**. AN0102 catches the ones **you didn't** — the `IntPtr` that lives one field deeper, inside a BCL type, and moves through your code because someone else's signature carries it:

```csharp
var h = File.OpenHandle(path, FileMode.Open);   // returns SafeFileHandle  → AN0102 (source)
RandomAccess.Read(h, buffer, 0);                // takes  SafeFileHandle  → AN0102 (sink)
new SafeFileHandle((nint)0x1234, ownsHandle: false);  // ctor takes IntPtr → AN0102 (sink) — any integer becomes a "file handle"
```

No `IntPtr` token appears in any of those lines. AN0100 sees nothing. AN0102 flags all three.

**What counts as an untyped native pointer:** `IntPtr`/`UIntPtr` (not `nint`/`nuint` used as integers), `void*`, any `SafeHandle`/`CriticalHandle` subclass, and any struct whose fields make it one — `struct HWND { IntPtr Value; }` is `IntPtr` in a costume, as are `GCHandle`, `HandleRef`, `RuntimeTypeHandle`.

**What fires:**

- **Sources** — a member that *returns* one: `File.OpenHandle()`, `proc.Handle`, `fs.SafeFileHandle`, `Marshal.AllocHGlobal()`, `NativeMemory.Alloc()`, `h.DangerousGetHandle()`.
- **Sinks** — a member that *accepts* one as a parameter or setter, regardless of what you pass: `RandomAccess.Read(SafeFileHandle, …)`, `new FileStream(SafeFileHandle, …)`, `new Span<byte>(void*, int)`, `Buffer.MemoryCopy(void*, void*, …)`, `Marshal.ReadInt32(IntPtr, …)`.
- **Declarations** — naming the type at all: `SafeFileHandle f;`, `var h = File.OpenHandle(…)`, `class Mine : SafeHandle`.

**What does not fire:** managed objects that merely *own* a handle internally — `new FileStream(path, …)`, `File.ReadAllBytes`, `Process.Start()`, `typeof(X)`. Holding a `FileStream` is not holding a pointer; there is nothing in your hands to distort. Only touching its `.SafeFileHandle` fires. Also clean: `Marshal.GetLastWin32Error()` (no pointer in the signature), `MemoryMarshal.CreateSpan(ref *p, n)`, `T*` to any real struct, `delegate* unmanaged<…>`.

**The idiom is the same as AN0100:** empty marker struct, `T*` is the handle. If a BCL API exists only in an untyped shape, declare the P/Invoke yourself with `HFILE*`, or use the managed API that never exposes the handle.

**Configuration** via MSBuild property:

```xml
<PropertyGroup>
  <ProhibitReachableUntypedNativePointers>warn</ProhibitReachableUntypedNativePointers>
</PropertyGroup>
```

| Value        | Behavior                                      |
| ------------ | --------------------------------------------- |
| `warn`     | Warning (default)                              |
| `disallow` | Error — build fails on any reachable untyped pointer |
| `ignore`   | Disabled — rollout only, never a destination   |

No allowlist, no per-namespace exemption, no opt-out attribute.

#### Why AN0100 + AN0102 exist — friction, not a sandbox

`IntPtr` throws away type checking the C# compiler is perfectly able to do. Native APIs have many distinct pointer and handle types — `HWND`, `HFILE`, `HPCON` — and they are not interchangeable. `IntPtr` flattens all of them to one type so `SetForegroundWindow(hFile)` compiles. That flattening existed for one reason: **VB.NET has no pointer types**, so the CLR needed a pointer-sized value every language could spell. We don't use VB.NET. C# has `unsafe struct HWND { }` and `HWND*`, and with them the compiler type-checks native handles exactly as it type-checks everything else. Every `IntPtr` in a C# codebase is a place where you told the compiler to stop checking.

These analyzers are **not** a sandbox. Compiled C# can always crash the machine — `unsafe`, a wrong `[DllImport]` signature, reflection — and the programmer is in charge of the code. The goal is to **raise the friction on paths that lead to dangerous bugs and exploitable surfaces later**, so that the easy path and the safe path are the same path. This matters most when **an AI is writing the code**: an AI reaches for whatever compiles, and `File.OpenHandle` → `RandomAccess.Read` compiles. With AN0102 it does not — and the error shows the idiom, so the next attempt is the right one.

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

### AN0501: PureFunction — no instance state mutation

Flags any instance state mutation inside methods marked with `[PureFunction]`. Designed to enforce side-effect-free methods for render passes, layout measurement, and hit testing.

**Always enabled** — no MSBuild property needed. Purely attribute-driven.

`Inherited = true` on the attribute means overrides automatically inherit the constraint. Mark the base method once, every override is enforced.

**What it flags (Error):** field assignment, compound assignment, increment/decrement, property setter, `ref`/`out` instance field.

**What it allows:** local variables, parameters, static fields, method calls (no transitive checking in v1), event raises.

**Example:**

```csharp
using AN.CodeAnalyzers.PureFunction;

public abstract class FView
{
    [PureFunction]
    public virtual void Draw(FDrawContext dc) { }
}

public class FCodeView : FView
{
    private bool _needsRecalc;

    public override void Draw(FDrawContext dc)
    {
        _needsRecalc = false;  // AN0501 error

        var localTemp = 5;
        localTemp = 6;         // Fine — local variable
    }
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

### ClassLibInfo

A standalone CLI tool that generates dense API surface dumps from compiled .NET assemblies using `System.Reflection.Metadata`. Reads PE metadata directly — no runtime loading of the target assembly. Designed for AI context windows.

**CLI usage:**

```bash
# Dump public+protected API (default)
ClassLibInfo MyLibrary.dll

# Output to file, flat text format
ClassLibInfo MyLibrary.dll output.api.txt --format flat

# Include private/internal members
ClassLibInfo MyLibrary.dll --include-private-and-internal
```

**Output formats:** HJSON (default) or keyword-prefixed flat text (`--format flat`).

**Visibility:** By default, dumps the public + protected API surface. Protected members are annotated (`vis: protected` in HJSON, `protected` prefix in flat text). Public members have no annotation.

**CLI flags:**

| Flag | Description |
|------|-------------|
| `--format hjson\|flat` | Output format (default: `hjson`) |
| `--include-private-and-internal` | Include all private/internal members |

## License

Apache License, Version 2.0 — see [LICENSE](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/LICENSE.txt)

## Source

[github.com/ArtificialNecessity/AN_CodeAnalyzers](https://github.com/ArtificialNecessity/AN_CodeAnalyzers)
