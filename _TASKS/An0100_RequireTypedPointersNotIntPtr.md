# AN0100: RequireTypedPointersNotIntPtr

## Summary

| | |
|---|---|
| **ID** | AN0100 |
| **Category** | AN.TypeSafety |
| **Default Severity** | Warning |
| **Configurable** | Yes — `disallow` / `warn` / `ignore` |

**`IntPtr` is not safe.** It is not a pointer. It is not a handle. It is not a size. It is an untyped bag of bits that the compiler cannot reason about, that the type checker cannot protect, and that silently converts between completely unrelated concepts. Every `IntPtr` in your code is a bug waiting to happen.

`IntPtr` erases type information at the exact boundary where type information matters most. The compiler cannot distinguish an `HWND` from an `HPCON` from a raw memory address from a stale dangling pointer from an integer someone cast for convenience. You can assign a window handle to a console handle. You can increment a handle as if it were a pointer. You can pass a handle value where a pointer-to-handle was expected. All of this compiles. None of it works. Some of it corrupts memory. Some of it creates security vulnerabilities. All of it is preventable.

This analyzer exists because `IntPtr` has no place in user code. There are no exceptions.

---

## The Rule

### `IntPtr`, `UIntPtr` and `void*` — flagged everywhere

**Any use of `IntPtr`, `UIntPtr` or `void*` in user code is flagged**, including:

- Field declarations
- Local variable declarations
- Method parameter types
- Method return types
- Property types
- Cast expressions to/from these types
- P/Invoke (`[DllImport]` / `[LibraryImport]`) signatures
- Inside structs, classes, records — everywhere
- Inside `SafeHandle` subclasses
- Inside "typed wrapper" structs — `struct HWND { IntPtr Value; }` is `IntPtr` in a costume. The compliant struct is **empty** (see "What To Use Instead")
- `void*` in any position — it is `IntPtr` with a different spelling

**There are no exemptions.** If the type is `IntPtr`, `UIntPtr` or `void*`, it is flagged. There is no "interop project where it's allowed" — the interop idiom contains none of these (see "Project Organization").

### `nint` and `nuint` — a review-prompt warning in P/Invoke declarations only

`nint` and `nuint` are C# keywords that are aliases for `IntPtr` and `UIntPtr` at the IL level. In regular code they are native-sized integers with arithmetic support — legitimate, not flagged.

In P/Invoke declarations they are **correct when the native type is a pointer-sized integer** (`SIZE_T`, `DWORD_PTR`, `ULONG_PTR`, a bitfield/flag word, a count) and **wrong when they stand in for a handle or a pointer** — that is `IntPtr` under another name. The analyzer cannot read the native docs, so it emits a *warning* phrased as the question ("integer, or handle/pointer?"); the author leaves it (integer) or replaces it with `T*` (handle/pointer). This is the one place in the family where a warning is the design rather than a rollout stage.

| Type | Flagged everywhere | In P/Invoke |
|---|---|---|
| `IntPtr` | ✅ error/warn per config | ✅ |
| `UIntPtr` | ✅ | ✅ |
| `void*` | ✅ | ✅ |
| `nint` | — | ⚠ review-prompt warning |
| `nuint` | — | ⚠ review-prompt warning |
| `SafeHandle` & subclasses, `Marshal`, `GCHandle`, `HandleRef`, and any API whose signature carries the above | see **AN0102** | see **AN0102** |

The only code not analyzed is auto-generated code (marked with `[GeneratedCode]` or in generated files), which is standard Roslyn analyzer behavior.

---

## Configuration

MSBuild property: `<RequireTypedPointersNotIntPtr>`

```xml
<PropertyGroup>
  <RequireTypedPointersNotIntPtr>warn</RequireTypedPointersNotIntPtr>
</PropertyGroup>
```

| Value | Behavior |
|---|---|
| `warn` | **Warning** (default) — flags IntPtr/UIntPtr usage as warnings |
| `disallow` | **Error** — build fails on any IntPtr/UIntPtr usage |
| `ignore` | Disabled — no diagnostics |

---

## Error Messages

**Every message shows the idiom.** A diagnostic that says "don't use X" without showing what to type instead is a puzzle, not a fix. The idiom block is identical across the AN0100/AN0102 family so nobody can read the error and not know the replacement.

For `IntPtr`/`UIntPtr` (anywhere):

```
AN0100: Do not use 'IntPtr' — it erases the type at the exact boundary where the type matters.
        Handles are EMPTY unsafe marker structs and the pointer IS the handle:
            unsafe struct HWND { }        HWND* hWnd;        (HWND*)null        HWND** phWnd (out-param)
        Pointers name their pointee:  byte* buffer;  OVERLAPPED* ov;   Sizes/bitfields are integers:  nuint cbSize;
        Never IntPtr, never void*, never SafeHandle.   See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md
```

For `void*` (anywhere):

```
AN0100: Do not use 'void*' — it is IntPtr with a different spelling: "points at something" and refuses to say what.
        Name the pointee:  byte* buffer;  OVERLAPPED* ov;  HPCON** phPC;   Reserved-must-be-NULL:  unsafe struct RESERVED_MUST_BE_NULL { }  →  RESERVED_MUST_BE_NULL* p
        Never IntPtr, never void*, never SafeHandle.   See: …TypeSafePInvoke.md
```

For `nint`/`nuint` in P/Invoke signatures (**warning only**, a review prompt — see "For integer-sized values"):

```
AN0100: 'nuint attribute' in P/Invoke: is this an INTEGER (SIZE_T / DWORD_PTR / flags → nuint is correct, leave it) or a HANDLE/POINTER the native side dereferences or closes (→ write T*: unsafe struct HPCON { }  HPCON* h)?
        See: …TypeSafePInvoke.md
```

> **Note:** `docs/TypeSafePInvoke.md` is the canonical idiom page every message links to; its content is the "What To Use Instead" section below plus the ConPTY worked example. It must be written as part of AN0102.

---

## Why IntPtr Is Not Safe

### It erases type information

```csharp
// All three are IntPtr. The compiler sees no difference.
IntPtr windowHandle = CreateWindowEx(...);
IntPtr consoleHandle = CreatePseudoConsole(...);
IntPtr memoryBlock = Marshal.AllocHGlobal(1024);

// Compiles. Passes a window handle to a console API.
// Silent corruption. Discovered at runtime, maybe.
ClosePseudoConsole(windowHandle);
```

### It enables pointer arithmetic on handles

```csharp
IntPtr hWnd = GetForegroundWindow();
hWnd += 1;  // What does this even mean? It compiles. It's nonsense.
             // But IntPtr doesn't know it's a handle, so it lets you.
```

### It silently converts between unrelated concepts

```csharp
IntPtr handle = GetProcessHandle();
IntPtr pointer = Marshal.AllocHGlobal(1024);
handle = pointer;  // A process handle is now a heap pointer. Compiles fine.
```

### It confuses values with pointers-to-values

This is the class of bug that motivated this rule. When an API expects a *pointer to* a handle, and you pass the handle *value* instead, both are `IntPtr`. The compiler cannot help you. The program compiles, runs, and silently does the wrong thing.

---

## The Real-World Bug

We spent hours debugging a ConPTY integration where `CreateProcessW` succeeded but the child process was not attached to the pseudo console. All API calls returned success. The child's output went to the parent console instead of our pipe.

Root cause: `UpdateProcThreadAttribute` received the HPCON handle *value* (e.g., `0x0000025C`) instead of a *pointer to* the HPCON. Windows read memory at address `0x0000025C` looking for a console handle, found garbage, and silently fell back to parent console inheritance.

With `IntPtr` in the signature, this bug compiles and runs. With typed structs and `unsafe T*`, this bug is a compile error.

---

## What To Use Instead

### THE IDIOM — for handles: `unsafe` EMPTY marker structs, used as opaque pointer types only

```csharp
// The handle IS the pointer. The struct is an EMPTY MARKER — no payload, no IntPtr, no void*.
// Its only job is to make HPCON* and HWND* DIFFERENT TYPES so the compiler type-checks them:
//   cross-assignment is an error, arithmetic is an error, passing the value where a
//   pointer-to-value was expected is an error. The marshaller blits a pointer-sized value
//   exactly as it would an IntPtr — nothing is lost at the ABI.
unsafe struct PROC_THREAD_ATTRIBUTE_LIST { }
unsafe struct HPCON { }
unsafe struct HWND  { }
unsafe struct RESERVED_MUST_BE_NULL { }     // for "reserved, must be NULL" parameters: the only constructible value is (RESERVED_MUST_BE_NULL*)null

// Null handle:  (HWND*)null          — NOT IntPtr.Zero, NOT default(HWND). A struct VALUE is never a handle; the pointer is.
// Pointer-to-handle (out-params):  HPCON**   — NOT out IntPtr, NOT ref HPCON.
```

There is **no interop project where `IntPtr` is the job.** The interop project's job is *these marker structs plus `T*` signatures*, and with them it builds clean under `disallow` like every other project. Setting `ignore` on the interop project is not a design; it is a TODO wearing a config value.

### For pointer parameters: `unsafe T*` — every pointer names its pointee

```csharp
// BAD — IntPtr erases what we're pointing at. This is the signature that caused the ConPTY bug below.
[DllImport("kernel32.dll")]
static extern bool UpdateProcThreadAttribute(
    IntPtr lpAttributeList, uint dwFlags, IntPtr attribute,
    IntPtr lpValue,       // ← what is this? a handle? a pointer? who knows
    IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

// GOOD — every pointer says what it points at; every integer is an integer.
[DllImport("kernel32.dll")]
static extern unsafe bool UpdateProcThreadAttribute(
    PROC_THREAD_ATTRIBUTE_LIST* lpAttributeList,
    uint   dwFlags,
    nuint  attribute,                          // DWORD_PTR: a pointer-SIZED bitfield (PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016). An integer, not a handle — nuint is correct here.
    HPCON* lpValue,                            // ← pointer to the HPCON. The & is required. Type-checked. Passing the handle value is a compile error.
    nuint  cbSize,                             // SIZE_T: sizeof(HPCON*)
    RESERVED_MUST_BE_NULL* lpPreviousValue,    // documented "reserved, must be NULL" — a type with no constructible pointee makes null the only legal value
    RESERVED_MUST_BE_NULL* lpReturnSize);
```

**`void*` is forbidden for the same reason `IntPtr` is.** `void*` is `IntPtr` with a different spelling — it says "points at something" and refuses to say what. Write `byte*`, `OVERLAPPED*`, `HPCON**`. If the native API genuinely takes "any buffer", the C# signature takes `byte*` and the caller casts at the call site, where the intent is visible.

### For integer-sized values: `nint` / `nuint` (or the exact-width integer)

`nint`/`nuint` are **fine in P/Invoke when the native type is an integer that happens to be pointer-sized**: `SIZE_T`, `DWORD_PTR`, `ULONG_PTR`, `LPARAM`/`WPARAM` used as flag words, bitfields, sizes, counts. They are **forbidden in P/Invoke when standing in for a handle or a pointer** — that is `IntPtr` under another name. The test: *would the native side ever dereference or `CloseHandle` this value?* If yes, it is `T*`; if no, it is an integer and `nuint` is honest.

```csharp
static extern unsafe bool F(nuint cbSize);       // SIZE_T — fine
static extern unsafe bool G(nuint hWnd);         // AN0100 — a handle wearing an integer type; write HWND*
```

The analyzer cannot read the Win32 docs, so it flags `nint`/`nuint` in P/Invoke as a **warning** with a message that asks the question above; the author answers by leaving it (integer) or fixing it (handle/pointer). Use `[NativeInteger("SIZE_T")]`-style suppression only if a per-parameter marker is added later; for now the warning is the review prompt.

---

## Migration Guide

| Before (AN0100 violation) | After (compliant) |
|---|---|
| `IntPtr hWnd;` | `unsafe struct HWND { }` (empty) — then `HWND* hWnd;` |
| `IntPtr hProcess;` | `unsafe struct HPROCESS { }` — `HPROCESS* hProcess;`. **Not** `SafeProcessHandle` — `SafeHandle` is `IntPtr` + a finalizer; it gives lifetime safety and zero type safety |
| `IntPtr pBuffer;` | `byte* pBuffer;` (or the real element type). **Not** `SafeBuffer`, **not** `void*` |
| `IntPtr lpValue` in P/Invoke | `T* lpValue` with the correct pointee type |
| `out IntPtr phPC` (pointer-to-handle) | `HPCON** phPC` |
| `IntPtr.Zero` as null handle | `(HWND*)null`. **Not** `default(HWND)` — a struct value is not a handle |
| `void* lpReserved` | `RESERVED_MUST_BE_NULL* lpReserved`, or the documented real type |
| `(IntPtr)someInt` | There is no "if truly needed". Either it is an integer (`nuint`) or it is a pointer/handle (`T*`). Decide, then type it |
| `IntPtr cbSize` / `IntPtr dwFlags` | `nuint cbSize` / `nuint attribute` — integers that are pointer-sized are integers |
| `SafeFileHandle` from `File.OpenHandle` / `RandomAccess.*` | A managed API that does not expose the handle (`FileStream`), or your own `HFILE*` P/Invoke. See AN0102 |
| Struct with an `IntPtr` field "for typing" | Delete the field. The struct is empty; the `*` carries the value |

---

## Project Organization

Every project, including the interop project, is `<RequireTypedPointersNotIntPtr>disallow</RequireTypedPointersNotIntPtr>`.

1. **Interop project** — small, dedicated, `unsafe`: defines the empty marker structs and the `T*` P/Invoke signatures (AN0101 requires the `unsafe` marker on them). It builds clean under `disallow` because the idiom contains no `IntPtr` and no `void*`. It exposes a **safe public API** (methods without `unsafe` in their signatures) so consumers never see a pointer.

2. **All other projects** — consume the interop project's safe API. They contain no P/Invoke, no pointers, no handles of any kind, and are additionally under AN0102 (`ProhibitErasedHandlesAndPointers`) so that BCL types which *wrap* an `IntPtr` (`SafeHandle` family, `Marshal`, `GCHandle`, `File.OpenHandle`) are rejected too.

`ignore` exists only as a **rollout** value for a project that has not been converted yet. It is never a destination.