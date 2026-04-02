# AN0104: ProhibitPlatformImports

## Summary

|                            |                                            |
| -------------------------- | ------------------------------------------ |
| **ID**               | AN0104                                     |
| **Category**         | AN.TypeSafety                              |
| **Default Severity** | Disabled                                   |
| **Configurable**     | Yes — `error` / `warn` / `disabled`  |

Flag any platform import construct in a project that has `<ProhibitPlatformImports>` set. This is a project-level policy: when enabled, **no** P/Invoke or native library loading is allowed in that project.

---

## The Rule

Flag all of the following constructs when `ProhibitPlatformImports` is set to `error` or `warn`:

1. **`[DllImport]`** attribute on methods
2. **`[LibraryImport]`** attribute on methods
3. **`[UnmanagedCallersOnly]`** attribute on methods
4. **`NativeLibrary.Load()`** calls
5. **`NativeLibrary.TryLoad()`** calls

---

## Configuration

MSBuild property: `<ProhibitPlatformImports>`

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

When the property is absent, the analyzer is disabled (same as `disabled`).

---

## Diagnostic Messages

**For attribute-based imports (`[DllImport]`, `[LibraryImport]`, `[UnmanagedCallersOnly]`):**

```
AN0104: Platform import '{MethodName}' is prohibited because <ProhibitPlatformImports> is set to '{ConfigValue}'
```

**For `NativeLibrary.Load` / `NativeLibrary.TryLoad` calls:**

```
AN0104: Call to '{NativeLibrary.Load}' is prohibited because <ProhibitPlatformImports> is set to '{ConfigValue}'
```

---

## Diagnostic Location

- **Attribute-based:** The squiggly underline spans from the attribute through the method name (the entire method declaration up to the parameter list).
- **NativeLibrary calls:** The squiggly underline spans the invocation expression.

---

## Examples

```csharp
// AN0104 — [DllImport] prohibited
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr hObject);

// AN0104 — [LibraryImport] prohibited
[LibraryImport("kernel32.dll")]
static partial bool CloseHandle(IntPtr hObject);

// AN0104 — [UnmanagedCallersOnly] prohibited
[UnmanagedCallersOnly]
static int ManagedCallback(int value) => value * 2;

// AN0104 — NativeLibrary.Load prohibited
var lib = NativeLibrary.Load("mylib.dll");

// AN0104 — NativeLibrary.TryLoad prohibited
NativeLibrary.TryLoad("mylib.dll", out var handle);
```

---

## Recommended Usage

Isolate native interop in a small dedicated project with `<ProhibitPlatformImports>disabled</ProhibitPlatformImports>`, and set `error` in all other projects. This forces all platform-specific code into a single, reviewable location.

```xml
<!-- Application project — no native code allowed -->
<PropertyGroup>
  <ProhibitPlatformImports>error</ProhibitPlatformImports>
</PropertyGroup>

<!-- Interop project — native code lives here -->
<PropertyGroup>
  <ProhibitPlatformImports>disabled</ProhibitPlatformImports>
</PropertyGroup>
```

---

## Related

- **AN0100: RequireTypedPointersNotIntPtr** — flags IntPtr/UIntPtr usage
- **AN0101: RequireUnsafeOnPInvokeImports** — requires `unsafe` on P/Invoke methods
- Both can coexist with AN0104 — they serve different purposes