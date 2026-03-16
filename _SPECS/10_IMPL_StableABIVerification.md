# AN.CodeAnalyzers

A single NuGet package containing all Artificial Necessity code analyzers.

## Package: `AN.CodeAnalyzers`

```xml
<PackageReference Include="AN.CodeAnalyzers" Version="0.1.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

---

## AN.CodeAnalyzers.ExplicitEnums

### Purpose

Prevent silent binary compatibility breaks caused by enum members without explicit integer values. The C# compiler auto-increments enum values by default, meaning inserting a member in the middle silently shifts all subsequent values. Callers compiled against the old values continue using them — no error, no warning, just wrong behavior.

Essential for AI-assisted development, where an AI will confidently insert enum members alphabetically without understanding deployment boundaries.

### AN0001: Enum member must have explicit value

**Scope:** Controlled by MSBuild property.

```xml
<PropertyGroup>
  <EnforceExplicitEnumValues>public</EnforceExplicitEnumValues>
</PropertyGroup>
```

Values:

- `public` — only public enums (default if property present with no value)
- `all` — all enums regardless of visibility
- `none` or absent — disabled

**Diagnostic:**

- ID: `AN0001`
- Severity: Error
- Message: `Enum member '{0}.{1}' must have an explicit value assignment`

**Per-enum opt-out:**

```csharp
[SuppressExplicitEnumValues]
internal enum ThrowawayState { A, B, C }
```

---

## AN.CodeAnalyzers.StableABIVerification

### Purpose

Detect all categories of silent binary compatibility breaks — changes that compile clean but break consumers at runtime. Extends beyond enums to cover every value the C# compiler bakes into the caller's assembly.

### Analyzer Rules

#### AN0002: Avoid public const in libraries (suggest static readonly)

- ID: `AN0002`
- Severity: Warning
- Message: `Public const '{0}' is baked into callers at compile time. Consider 'public static readonly' unless the value is a true universal constant.`

**Suppression:** Via `[SuppressMessage]` or `.editorconfig` for values that genuinely never change (e.g., `Math.PI`-style constants).

#### AN0003: Default parameter value changed

Requires snapshot file to detect. Cannot be caught by syntax analysis alone since the previous value is not known to the compiler.

#### AN0004: Struct field order changed in Sequential layout

Requires snapshot file to detect.

#### AN0005: P/Invoke parameter missing explicit MarshalAs

- ID: `AN0005`
- Severity: Warning
- Message: `P/Invoke parameter '{0}' should have an explicit [MarshalAs] attribute to prevent marshalling surprises.`

### StableABI Snapshot File

#### File Format

**Filename:** `StableABI.snapshot` (committed to source control, next to the .csproj)

**Format:** Flat sorted key-value, one fact per line. Machine-generated, never hand-edited.

```
const.MyClass.MaxRetries: int 3
const.MyClass.Version: string "2.0"
default.MyClass.Connect.retries: int 3
default.MyClass.Connect.timeout: int 30
enum.BufferUsage._flags: true
enum.BufferUsage._type: int
enum.BufferUsage.IndexBuffer: 2
enum.BufferUsage.UniformBuffer: 4
enum.BufferUsage.VertexBuffer: 1
enum.PixelFormat._type: int
enum.PixelFormat.R16UNorm: 2
enum.PixelFormat.R8G8B8A8UNorm: 37
enum.PixelFormat.R8UNorm: 1
pinvoke.NativeMethods.SomeFunc.param0: int
pinvoke.NativeMethods.SomeFunc.param1: IntPtr
struct.VertexPosition._layout: Sequential
struct.VertexPosition._pack: 0
struct.VertexPosition._size: 0
struct.VertexPosition.field0.Position: Vector3 @0
struct.VertexPosition.field1.Color: RgbaFloat @12
```

#### Key design rules

- **Sorted alphabetically.** Deterministic output, clean git diff.
- **One fact per line.** A single changed value = one line in the diff.
- **Fully qualified keys.** Namespace omitted if unambiguous within project; type + member otherwise.
- **Metadata prefixed with underscore.** `_type`, `_flags`, `_layout`, `_pack`, `_size` are metadata about the containing type, not members.
- **Struct fields keyed by ordinal.** `field0`, `field1` etc. to detect reordering. Name and type follow. Offset after `@`.
- **String values quoted.** Numeric and boolean values unquoted.

#### Snapshot workflow

**Generation:** An MSBuild target runs after compile and regenerates `StableABI.snapshot` from the compiled assembly metadata.

```xml
<PropertyGroup>
  <GenerateStableABISnapshot>true</GenerateStableABISnapshot>
  <StableABISnapshotScope>public</StableABISnapshotScope>  <!-- public | all -->
</PropertyGroup>
```

**Verification:** A second MSBuild target (or analyzer reading the snapshot as `AdditionalFile`) compares the freshly generated snapshot against the committed version.

- If no committed snapshot exists: generate and succeed (first run).
- If committed snapshot exists and matches: succeed silently.
- If committed snapshot exists and differs: **build error** listing each changed line.

The developer must then either:

1. Fix the unintended change, or
2. Regenerate the snapshot (explicit `dotnet stableabi-accept` or delete + rebuild) to acknowledge the intentional ABI change

#### Scope control

The snapshot captures only symbols matching `StableABISnapshotScope`:

- `public` — public types and members only (library authors shipping NuGet packages)
- `all` — all types regardless of visibility (internal binary compat scenarios)

---

## Package structure

```
AN_CodeAnalyzers/    <-- repo root
├── .gitignore
├── AN.CodeAnalyzers.csproj
├── ExplicitEnums/
│   └── ExplicitEnumValuesAnalyzer.cs       (AN0001)
│   └── SuppressExplicitEnumValuesAttribute.cs
│   └── Tests/
├── StableABIVerification/is 
│   ├── PublicConstAnalyzer.cs              (AN0002)
│   ├── StableABISnapshotGenerator.cs       (post-compile snapshot generation)
│   └── StableABISnapshotVerifier.cs        (diff against committed snapshot)
│   └── Tests/
├── PInvokeVerification/ (AN0005)
│   ├── PInvokeSnapshotGenerator.cs       (post-compile snapshot generation)
│   └── PInvokeSnapshotVerifier.cs        (diff against committed snapshot)
├── build/
    └── AN.CodeAnalyzers.targets
```

## Non-goals

- Not a general API surface tracker (PublicApiAnalyzers already does that, poorly)
- Not a semver enforcement tool
- Not a runtime check — everything is compile-time / build-time
- Does not attempt to detect behavioral changes, only value/layout changes

## Priority

1. **AN0001 (ExplicitEnums)** — shipping now, solves the Veldrid problem today
2. **Snapshot generation + verification** — ship next, enables all value-change detection
3. **AN0002, AN0005** — ship alongside snapshot
4. **AN0003, AN0004** — automatically covered once snapshot exists
