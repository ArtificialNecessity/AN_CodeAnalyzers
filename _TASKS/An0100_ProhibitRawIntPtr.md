# AN0100: ProhibitRawIntPtr

## Summary

| | |
|---|---|
| **ID** | AN0100 |
| **Category** | AN.TypeSafety |
| **Severity** | Error |
| **Enabled** | Always (no opt-out) |

**Rationale:** `IntPtr` erases type information at the boundary where it matters most — P/Invoke calls, handle management, and pointer arithmetic. The compiler cannot distinguish an `HPCON` from an `HWND` from a raw memory address from a stale dangling pointer. Every `IntPtr` in user code is a class of bug the type checker could have caught but was prevented from catching.

---

## The Rule

**Error on any use of `IntPtr`, `UIntPtr`, `nint`, or `nuint` in user code**, including:

- Field declarations
- Local variable declarations
- Method parameter types
- Method return types
- Property types
- Cast expressions to/from `IntPtr`

### Exceptions (not flagged)

- Inside a struct that IS a typed handle wrapper (one `IntPtr Value` field is the whole point)
- Inside `SafeHandle` subclasses (framework pattern for handle lifetime)
- In auto-generated code (marked with `[GeneratedCode]`)

### Detection

```csharp
// AN0100 ERROR — raw IntPtr as a field
IntPtr _pseudoConsoleHandle;

// AN0100 ERROR — raw IntPtr as a parameter
void Foo(IntPtr hConsole);

// AN0100 ERROR — raw IntPtr as a local
IntPtr result = SomeCall();

// AN0100 ERROR — nint (alias for IntPtr)
nint bufferSize = 1024;

// OK — inside a typed handle wrapper struct
[StructLayout(LayoutKind.Sequential)]
public struct HPCON
{
    public IntPtr Value;  // ← allowed: this IS the wrapper
}

// OK — SafeHandle subclass
public class SafePipeHandle : SafeHandleZeroOrMinusOneIsInvalid { ... }
```

---

## Error Message

```
AN0100: Do not use raw IntPtr. Wrap in a typed struct and use unsafe T* for pointer
parameters. See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/tree/main/_SPECS/AN0100_ProhibitRawIntPtr.md
```

---

## The Pattern: Typed Handle Structs + Unsafe Pointers

### Problem: IntPtr is untyped

```csharp
// All three are IntPtr. The compiler sees no difference.
IntPtr windowHandle = CreateWindowEx(...);
IntPtr consoleHandle = CreatePseudoConsole(...);
IntPtr memoryBlock = Marshal.AllocHGlobal(1024);

// Compiles fine. Passes a window handle to a console API.
// Silent corruption. Discovered at runtime, maybe.
ClosePseudoConsole(windowHandle);
```

### Solution: One struct per handle type

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct HWND { public IntPtr Value; }

[StructLayout(LayoutKind.Sequential)]
public struct HPCON { public IntPtr Value; }

// Does not compile — HWND is not HPCON.
ClosePseudoConsole(windowHandle);  // ← type error
```

### Solution: Unsafe typed pointers for "pointer to value" parameters

Win32 APIs that take "a pointer to a value" should use `T*`, not `IntPtr`:

```csharp
// BAD — IntPtr erases what we're pointing at.
// The bug that started this rule: accidentally passing the handle VALUE
// where a POINTER TO the handle was expected. Compiles. Silently corrupts.
[DllImport("kernel32.dll")]
static extern bool UpdateProcThreadAttribute(
    IntPtr lpAttributeList, uint dwFlags, IntPtr attribute,
    IntPtr lpValue,       // ← what is this? a handle? a pointer? who knows
    IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

// GOOD — unsafe T* makes the indirection explicit.
// Cannot accidentally pass HPCON where HPCON* is expected.
[DllImport("kernel32.dll")]
static extern unsafe bool UpdateProcThreadAttribute(
    IntPtr lpAttributeList, uint dwFlags, IntPtr attribute,
    HPCON* lpValue,       // ← pointer to HPCON. The & is required. Type-checked.
    IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
```

Call site:

```csharp
HPCON console;
CreatePseudoConsole(size, input, output, 0, out console);

unsafe
{
    // Compiler enforces: you must take the address explicitly.
    UpdateProcThreadAttribute(..., &console, (IntPtr)sizeof(HPCON), ...);
}
```

### The real-world bug this prevents

We spent hours debugging a ConPTY integration where `CreateProcessW` succeeded but the child process wasn't attached to the pseudo console. All API calls returned success. The child's output went to the parent console instead of our pipe.

Root cause: `UpdateProcThreadAttribute` received the HPCON handle *value* (e.g., `0x0000025C`) instead of a *pointer to* the HPCON. Windows read memory at address `0x0000025C` looking for a console handle, found garbage, and silently fell back to parent console inheritance.

With `HPCON*` in the P/Invoke signature, this bug is a compile error.

---

## Migration Guide

| Before (AN0100 violation) | After (compliant) |
|---|---|
| `IntPtr hWnd;` | `HWND hWnd;` — define `struct HWND { IntPtr Value; }` |
| `IntPtr hProcess;` | `HPROCESS hProcess;` — or use `SafeProcessHandle` |
| `IntPtr pBuffer;` | `unsafe byte* pBuffer;` — or `SafeBuffer` |
| `IntPtr lpValue` in P/Invoke | `unsafe T* lpValue` — with the correct struct type |
| `IntPtr.Zero` as null handle | `default(HWND)` — or add `HWND.Null` static field |
| `(IntPtr)someInt` for size | `(nint)someInt` is also banned; use `IntPtr` only inside wrapper struct if truly needed, otherwise refactor to typed API |

---

## Suppression

This rule cannot be suppressed with `#pragma` or `[SuppressMessage]`. If you believe you need raw `IntPtr`, you need a typed wrapper struct. No exceptions in user code.

For genuinely unavoidable interop situations (e.g., callback signatures dictated by external frameworks), file an issue to discuss adding the specific case to the exception list.