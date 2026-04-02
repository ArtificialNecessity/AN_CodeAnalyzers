# AN0105: ProhibitNamespaceAccess

## Summary

|                            |                                                     |
| -------------------------- | --------------------------------------------------- |
| **ID**               | AN0105                                              |
| **Category**         | AN.TypeSafety                                       |
| **Default Severity** | Disabled                                            |
| **Configurable**     | Yes — per-pattern `error` / `warn` via JSON config |

Prohibit access to specific namespaces in a project. Any type reference from a prohibited namespace produces a diagnostic. Patterns support prefix globbing with `*`. Severity is configurable per-pattern group.

---

## The Rule

**Primary detection: type references.** Flag any use of a type whose containing namespace matches a prohibited pattern. This includes:

- Variable declarations (`MemoryMappedFile mmf = ...`)
- Method calls (`MemoryMappedFile.CreateNew(...)`)
- Return types (`public MemoryMappedFile GetFile()`)
- Parameter types (`void Process(MemoryMappedFile file)`)
- Field/property types
- Base types and interface implementations
- Generic type arguments (`List<MemoryMappedFile>`)
- `typeof(MemoryMappedFile)`
- Cast expressions (`(MemoryMappedFile)obj`)
- Object creation (`new MemoryMappedFile(...)`)
- Fully-qualified references (`System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(...)`)

**Secondary detection: `using` directives.** A `using` directive for a prohibited namespace produces a **warning** (never error), regardless of the configured severity. If you can't use anything in the namespace, the `using` is superfluous cruft. The warning helps clean it up but doesn't break the build.

---

## Configuration

MSBuild property: `<ProhibitNamespaceAccess>`

The value is a brace-delimited JSON-like structure with severity keys mapping to arrays of namespace patterns:

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>{ error = [ "System.Runtime.InteropServices", "System.IO.MemoryMappedFiles" ], warn = [ "OpenTK" ] }</ProhibitNamespaceAccess>
</PropertyGroup>
```

### Config format

```
{ severity = [ "pattern1", "pattern2" ], severity = [ "pattern3" ] }
```

- **Keys:** `error` or `warn` — the diagnostic severity for all patterns in that array
- **Values:** Arrays of namespace patterns (quoted strings)
- **Disabled:** When the property is absent or empty, the analyzer does nothing

### Parsing

Uses the same brace-delimited JSON-like parser style as `EnforceNamingConventions` (see `NamingConventionRuleParser.cs`), extended to support array values with `[ ]` brackets.

Malformed configuration produces an AN0106 warning diagnostic with the parse error details.

---

## Pattern Matching

Patterns are **prefix-based string matching** on the fully-qualified namespace of the referenced type.

### Rules

| Pattern | Matches | Does NOT match |
|---|---|---|
| `System.Runtime.InteropServices` | `System.Runtime.InteropServices` | `System.Runtime.InteropServices.Marshalling` |
| | | `System.Runtime` |
| `System.Runtime.InteropServices` | (exact namespace only) | |
| `System.Runtime.Interop*` | `System.Runtime.InteropServices` | `System.Runtime.CompilerServices` |
| | `System.Runtime.InteropServices.Marshalling` | `System.Runtime` |
| | `System.Runtime.InteropStuffNotInventedYet` | |
| `System.Runtime.*` | `System.Runtime.InteropServices` | `System.Runtime` (the namespace itself) |
| | `System.Runtime.CompilerServices` | `System` |
| | `System.Runtime.InteropServices.Marshalling` | |
| `System.*` | `System.IO` | `System` (the namespace itself) |
| | `System.Runtime.InteropServices` | |
| | `System.Collections.Generic` | |

### Matching algorithm

1. If the pattern contains `*`, treat everything before the `*` as a **prefix**. The namespace must start with that prefix. The `*` matches any remaining characters (including dots).
2. If the pattern does NOT contain `*`, it is an **exact match** — the namespace must equal the pattern exactly.

**Implementation:** For each type reference, get the type's containing namespace as a string. For each configured pattern:
- If pattern ends with `*`: check if namespace starts with the prefix (everything before `*`)
- Otherwise: check if namespace equals the pattern exactly

---

## Diagnostic IDs

| ID | Purpose |
|---|---|
| **AN0105** | Type reference or `using` directive touches a prohibited namespace |
| **AN0106** | Configuration parse error in `<ProhibitNamespaceAccess>` value |

---

## Diagnostic Messages

### AN0105 — Type reference (error or warn, per config)

```
AN0105: Access to '{TypeName}' in namespace '{Namespace}' is prohibited by pattern '{Pattern}' in <ProhibitNamespaceAccess>
```

**Example:**
```
AN0105: Access to 'MemoryMappedFile' in namespace 'System.IO.MemoryMappedFiles' is prohibited by pattern 'System.IO.MemoryMappedFiles' in <ProhibitNamespaceAccess>
```

```
AN0105: Access to 'DllImportAttribute' in namespace 'System.Runtime.InteropServices' is prohibited by pattern 'System.Runtime.Interop*' in <ProhibitNamespaceAccess>
```

### AN0105 — Using directive (always warning)

```
AN0105: Using directive for namespace '{Namespace}' is prohibited by pattern '{Pattern}' in <ProhibitNamespaceAccess>. Remove this unused using directive.
```

### AN0106 — Config parse error (always warning)

```
AN0106: Failed to parse <ProhibitNamespaceAccess> value: {ParseErrorDetails}. Expected format: { error = [ "Namespace.Pattern" ], warn = [ "Other.Pattern" ] }
```

---

## Diagnostic Location

- **Type references:** Squiggly on the type name syntax node
- **Using directives:** Squiggly on the entire `using` directive
- **Config errors:** Reported on the first syntax tree in the compilation (no specific location)

---

## Examples

### Configuration

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>{ error = [ "System.Runtime.InteropServices", "System.IO.MemoryMappedFiles" ], warn = [ "OpenTK.*" ] }</ProhibitNamespaceAccess>
</PropertyGroup>
```

### Code that triggers diagnostics

```csharp
// AN0105 WARNING — using directive for prohibited namespace (always warn, never error)
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;

public class MyClass
{
    // AN0105 ERROR — type from System.Runtime.InteropServices (exact match)
    DllImportAttribute attr;

    // AN0105 ERROR — type from System.IO.MemoryMappedFiles (exact match)
    MemoryMappedFile file;

    // AN0105 ERROR — fully-qualified reference, same namespace
    System.IO.MemoryMappedFiles.MemoryMappedFile GetFile() => default;

    // AN0105 WARN — type from OpenTK.Graphics (matches OpenTK.* pattern)
    OpenTK.Graphics.OpenGL.GL gl;
}
```

### Code that does NOT trigger diagnostics

```csharp
// No diagnostic — System.IO is not prohibited, only System.IO.MemoryMappedFiles
using System.IO;

// No diagnostic — System.Collections.Generic is not prohibited
var list = new List<int>();

// No diagnostic — System.Runtime is not prohibited (only System.Runtime.InteropServices)
// (would need "System.Runtime.*" pattern to catch this)
```

---

## Recommended Usage

### Lock down a managed-only application project

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>{ error = [ "System.Runtime.InteropServices*" ] }</ProhibitNamespaceAccess>
  <ProhibitPlatformImports>error</ProhibitPlatformImports>
</PropertyGroup>
```

This combines AN0104 (prohibit P/Invoke constructs) with AN0105 (prohibit even referencing interop types). Together they ensure the project has zero native code surface.

### Warn about deprecated or discouraged APIs

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>{ warn = [ "System.Web*", "System.Data.OleDb" ] }</ProhibitNamespaceAccess>
</PropertyGroup>
```

### Enforce architectural boundaries

```xml
<!-- UI layer — no direct database access -->
<PropertyGroup>
  <ProhibitNamespaceAccess>{ error = [ "System.Data*", "Microsoft.EntityFrameworkCore*" ] }</ProhibitNamespaceAccess>
</PropertyGroup>
```

---

## Implementation Notes

### Analyzer strategy

1. **On compilation start:** Parse the `build_property.ProhibitNamespaceAccess` config value. Build two lists: `errorPatterns` and `warnPatterns`. If parsing fails, report AN0106 and return.

2. **Register symbol action for type references:** Use `RegisterSyntaxNodeAction` on `IdentifierNameSyntax`, `QualifiedNameSyntax`, `MemberAccessExpressionSyntax`, and `GenericNameSyntax`. For each, resolve the semantic type symbol, get its containing namespace, and check against all patterns.

3. **Register syntax node action for using directives:** For each `UsingDirectiveSyntax`, resolve the namespace symbol and check against all patterns. Always report as warning severity.

4. **Deduplication:** To avoid flooding the user with diagnostics, consider reporting at most once per type per file (or per type per method). The exact deduplication strategy can be refined during implementation.

### Parser extension

The existing `NamingConventionRuleParser` handles `{ key = "value" }`. For AN0105, we need `{ key = [ "value1", "value2" ] }`. Options:

- **Extend the existing parser** to support array values (detect `[` after `=`)
- **Write a new parser** specific to AN0105's format

Recommendation: Write a new `ProhibitNamespaceAccessConfigParser` that handles the `{ severity = [ "patterns" ] }` format. Keep it simple and self-contained.

### Performance

- Parse config once per compilation (in `RegisterCompilationStartAction`)
- Convert patterns to a fast-match structure (prefix list for glob patterns, hash set for exact matches)
- Short-circuit if no patterns are configured

---

## File Structure

```
ProhibitNamespaceAccess/
├── ProhibitNamespaceAccessAnalyzer.cs
├── ProhibitNamespaceAccessConfigParser.cs
└── Tests/
    ├── AN.CodeAnalyzers.ProhibitNamespaceAccess.Tests.csproj
    ├── ProhibitNamespaceAccessVerifierHelper.cs
    ├── ProhibitNamespaceAccessConfigParserTests.cs
    └── ProhibitNamespaceAccessAnalyzerTests.cs
```

---

## Related

- **AN0104: ProhibitPlatformImports** — prohibits P/Invoke constructs specifically. AN0105 is more general — it can prohibit any namespace.
- **AN0200: EnforceNamingConventions** — uses a similar JSON-like MSBuild property config format
- AN0104 and AN0105 complement each other: AN0104 catches P/Invoke constructs that don't involve namespace references (like `NativeLibrary.Load`), while AN0105 catches type references from prohibited namespaces.