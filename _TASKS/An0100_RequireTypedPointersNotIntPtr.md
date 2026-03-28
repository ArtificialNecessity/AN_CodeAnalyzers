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

### `IntPtr` and `UIntPtr` — flagged everywhere

**Any use of `IntPtr` or `UIntPtr` in user code is flagged**, including:

- Field declarations
- Local variable declarations
- Method parameter types
- Method return types
- Property types
- Cast expressions to/from these types
- P/Invoke (`[DllImport]` / `[LibraryImport]`) signatures
- Inside structs, classes, records — everywhere
- Inside `SafeHandle` subclasses
- Inside "typed wrapper" structs

**There are no exemptions.** If the type is `IntPtr` or `UIntPtr`, it is flagged.

### `nint` and `nuint` — flagged only in P/Invoke declarations

`nint` and `nuint` are C# keywords that are aliases for `IntPtr` and `UIntPtr` at the IL level. In regular code they function as native-sized integers with arithmetic support, which is a legitimate use case.

However, in P/Invoke declarations (`[DllImport]` or `[LibraryImport]` methods), `nint`/`nuint` carry the same type-erasure danger as `IntPtr`/`UIntPtr` — they hide whether a parameter is a handle, a pointer, or a size. Therefore `nint`/`nuint` are flagged when used as parameter types or return types in P/Invoke method signatures.

| Type | Flagged everywhere | Flagged in P/Invoke only |
|---|---|---|
| `IntPtr` | ✅ | — |
| `UIntPtr` | ✅ | — |
| `nint` | — | ✅ |
| `nuint` | — | ✅ |

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

For `IntPtr`/`UIntPtr` (anywhere):

```
AN0100: Do not use 'IntPtr'. IntPtr and UIntPtr erase type information and are not safe. Use typed structs for handles and unsafe T* for pointers. See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md
```

For `nint`/`nuint` (in P/Invoke signatures):

```
AN0100: Do not use 'nint' in P/Invoke declarations. Use typed structs for handles and unsafe T* for pointers. See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md
```

> **Note:** The `docs/TypeSafePInvoke.md` documentation file needs to be created as a companion guide explaining the type-safe P/Invoke pattern in detail.

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

### For handles: typed structs

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct HWND { public IntPtr Value; }
// NOTE: Yes, the struct itself contains IntPtr internally.
// The analyzer will flag this. Set <RequireTypedPointersNotIntPtr>ignore</RequireTypedPointersNotIntPtr>
// in the specific interop layer project that defines these types,
// and use disallow/warn in all consumer code.
```

**Isolate interop types in a dedicated project** with `<RequireTypedPointersNotIntPtr>ignore</RequireTypedPointersNotIntPtr>`, and set `disallow` or `warn` in all other projects. The interop boundary is the only place `IntPtr` should exist, and it should be as small and auditable as possible.

### For pointer parameters: unsafe T*

```csharp
// BAD — IntPtr erases what we're pointing at.
[DllImport("kernel32.dll")]
static extern bool UpdateProcThreadAttribute(
    IntPtr lpAttributeList, uint dwFlags, IntPtr attribute,
    IntPtr lpValue,       // ← what is this? a handle? a pointer? who knows
    IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

// GOOD — unsafe T* makes the indirection explicit.
[DllImport("kernel32.dll")]
static extern unsafe bool UpdateProcThreadAttribute(
    PROC_THREAD_ATTRIBUTE_LIST* lpAttributeList, uint dwFlags, uint attribute,
    HPCON* lpValue,       // ← pointer to HPCON. The & is required. Type-checked.
    ulong cbSize, void* lpPreviousValue, ulong* lpReturnSize);
```

### For integer-sized values: use the actual integer type

If you need a machine-word-sized integer and it is not a pointer or handle, use `ulong` or `long`. The `nint`/`nuint` keywords are fine in regular code but not in P/Invoke signatures where they mask the same type-erasure problem as `IntPtr`.

---

## Migration Guide

| Before (AN0100 violation) | After (compliant) |
|---|---|
| `IntPtr hWnd;` | Define `struct HWND { ... }` in interop project, use `HWND hWnd;` |
| `IntPtr hProcess;` | `SafeProcessHandle hProcess;` or typed struct |
| `IntPtr pBuffer;` | `unsafe byte* pBuffer;` or `SafeBuffer` |
| `IntPtr lpValue` in P/Invoke | `unsafe T* lpValue` with the correct struct type |
| `IntPtr.Zero` as null handle | `default(HWND)` or add `HWND.Null` static field |
| `(IntPtr)someInt` | Refactor to typed API; if truly needed, isolate in interop project |
| `nint` in P/Invoke param | Use typed struct or `unsafe T*` |

---

## Project Organization

The recommended approach for codebases that must interact with native APIs:

1. **Interop project** — A small, dedicated project that defines typed handle structs and P/Invoke signatures. Set `<RequireTypedPointersNotIntPtr>ignore</RequireTypedPointersNotIntPtr>` here. Keep it minimal and auditable.

2. **All other projects** — Set `<RequireTypedPointersNotIntPtr>disallow</RequireTypedPointersNotIntPtr>` or leave at the default `warn`. These projects consume the typed handles and never touch `IntPtr` directly.

This forces all untyped pointer manipulation into a single, reviewable location and prevents it from leaking into business logic, UI code, or anywhere else it does not belong.