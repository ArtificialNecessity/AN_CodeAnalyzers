# AN0101: RequireUnsafeOnPInvokeImports

## Summary

|                            |                                            |
| -------------------------- | ------------------------------------------ |
| **ID**               | AN0101                                     |
| **Category**         | AN.TypeSafety                              |
| **Default Severity** | Warning                                    |
| **Configurable**     | Yes —`prohibit` / `warn` / `ignore` |

**P/Invoke is not safe.** Any method decorated with `[DllImport]` or `[LibraryImport]` is calling unmanaged code that can corrupt memory, crash the process, and create security vulnerabilities. The `unsafe` keyword exists to mark exactly this kind of danger — but C# does not require it on P/Invoke declarations by default.

This analyzer requires that all P/Invoke methods (and their containing classes) are marked `unsafe`, making the danger visible at every call site.

---

## The Rule

**Flag any `[DllImport]` or `[LibraryImport]` method that is not declared `unsafe`**, and any class containing such methods that is not itself marked `unsafe`.

The caller should see `unsafe` in the signature. If calling unmanaged code looks safe, the API is lying.

---

## Configuration

MSBuild property: `<RequireUnsafeOnPInvokeImports>`

```xml
<PropertyGroup>
  <RequireUnsafeOnPInvokeImports>warn</RequireUnsafeOnPInvokeImports>
</PropertyGroup>
```

| Value        | Behavior                       |
| ------------ | ------------------------------ |
| `warn`     | **Warning** (default)    |
| `prohibit` | **Error** — build fails |
| `ignore`   | Disabled                       |

---

## Error Message

```
AN0101: P/Invoke method '{0}' must be declared 'unsafe'. P/Invoke calls unmanaged code that can corrupt memory and crash the process. See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md
```

---

## Why

P/Invoke bypasses every safety guarantee the CLR provides. It can:

- Read/write arbitrary memory
- Call into code with no bounds checking
- Corrupt the GC heap if marshalling is wrong
- Create security vulnerabilities through buffer overflows
- Crash the entire process with no managed exception

The `unsafe` keyword is C#'s mechanism for marking code that can do these things. P/Invoke should require it.

---

## Examples

```csharp
// AN0101 — not marked unsafe (and AN0100 — IntPtr)
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr hObject);

// Compliant — marked unsafe; HANDLE is an EMPTY marker struct and the pointer IS the handle
unsafe struct HANDLE { }
[DllImport("kernel32.dll")]
static extern unsafe bool CloseHandle(HANDLE* hObject);

// AN0101 — class not marked unsafe
static class NativeMethods
{
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(HANDLE* handle);
}

// Compliant — all members considered unsafe
static unsafe class NativeMethods
{
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(HANDLE* handle);
}


//// CRITICAL TO HELP USERS UNDERSTAND HOW TO USE THIS — this is the example people copy; it must be exactly right.
//// No IntPtr. No void*. No SafeHandle (SafeHandle is IntPtr + a finalizer — lifetime safety, zero type safety).
//// Handles are EMPTY unsafe marker structs; the pointer IS the handle; out-params are T**.

unsafe struct HFILE { }
unsafe struct HPCON { }
[StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }

// The interop boundary — unsafe class, safe public API
public unsafe class PseudoConsole : IDisposable
{
    // P/Invoke — unsafe, compiler enforced
    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(
        COORD size, HFILE* hInput, HFILE* hOutput,
        uint dwFlags, HPCON** phPC);                 // pointer-to-handle: HPCON**, never out IntPtr

    [DllImport("kernel32.dll")]
    private static extern int ClosePseudoConsole(HPCON* hPC);

    private HPCON* _handle;                          // null = no handle. Never IntPtr.Zero, never default(HPCON)

    // Safe public method — callers don't need unsafe context.
    // This is the boundary. You chose it. It's visible. It's auditable.
    public void Close()
    {
        if (_handle != null)
        {
            ClosePseudoConsole(_handle);
            _handle = null;
        }
    }
}

// Consumer code — no unsafe keyword anywhere. Clean.
var console = new PseudoConsole();
console.Close();  // safe call, no unsafe context needed

```

## Related

- **AN0100: RequireTypedPointersNotIntPtr** — companion rule that eliminates `IntPtr` and `void*` from P/Invoke signatures
- **AN0102: ProhibitErasedHandlesAndPointers** — rejects BCL types that *wrap* an `IntPtr` (`SafeHandle` family, `Marshal`, `GCHandle`) and any API whose signature carries one
- See [docs/TypeSafePInvoke.md](https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md) for the full type-safe P/Invoke pattern
