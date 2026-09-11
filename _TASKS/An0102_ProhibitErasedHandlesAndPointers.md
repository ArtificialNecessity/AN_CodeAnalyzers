# AN0102: ProhibitErasedHandlesAndPointers — and the AN0100 / AN0101 corrections it requires

## Summary

| | |
|---|---|
| **ID** | AN0102 (new) + amendments to AN0100 and AN0101 |
| **Category** | AN.TypeSafety |
| **Default Severity** | Warning |
| **Configurable** | Yes — `disallow` / `warn` / `ignore` (rollout knob only; see "No exceptions") |
| **Origin** | 2026-09-10, Blast buffer review (AN_Mirica). `AN.BlastBuffer` built clean under AN0100=`disallow` while holding an erased OS handle: `File.OpenHandle()` → `SafeFileHandle` → `RandomAccess.Read(SafeFileHandle, …)`. No `IntPtr` token anywhere in the code; the `IntPtr` was one field deeper, inside a BCL class. AN0100 is a *syntax* rule and cannot see it. |

**The intent of AN0100 was always: no erased handle or pointer of any kind, anywhere in our code.** Not "we never write the word `IntPtr`." This task closes the gap between those two statements, and corrects guidance in the existing AN0100/AN0101 specs that currently teaches the wrong idiom.

---

## THE IDIOM (every diagnostic in this family must show this — see "Error Messages")

```csharp
// The handle IS the pointer. The struct is an EMPTY MARKER whose only job is to make
// HWND* and HFILE* different types. No payload. No IntPtr. No void*. Ever.
public unsafe struct HWND  { }
public unsafe struct HFILE { }
public unsafe struct HPCON { }

// P/Invoke signatures name what every pointer points at. The marshaller blits a
// pointer-sized value exactly as it would an IntPtr — nothing is lost at the ABI,
// everything is gained at the type level: SetForegroundWindow(hFile) is a compile error.
[DllImport("user32")]   static extern unsafe bool SetForegroundWindow(HWND* hWnd);
[DllImport("kernel32")] static extern unsafe bool ReadFile(HFILE* hFile, byte* buffer, uint bytesToRead, uint* bytesRead, OVERLAPPED* overlapped);
[DllImport("kernel32")] static extern unsafe int  CreatePseudoConsole(COORD size, HFILE* hInput, HFILE* hOutput, uint flags, HPCON** phPC);
//                                                                                                                   ^^ pointer-to-handle is HPCON**, not IntPtr, not out IntPtr

// Null handle:  (HWND*)null   — NOT IntPtr.Zero, NOT default(HWND) (a struct value is not a handle; the pointer is)
```

**Forbidden, all of them, no exceptions:** `IntPtr`, `UIntPtr`, `nint`/`nuint` in P/Invoke *when standing in for a handle or pointer* (they are correct for pointer-sized integers: `SIZE_T`, `DWORD_PTR`, bitfields — AN0100 warns as a review prompt), `void*`, `SafeHandle` and every subclass (`SafeFileHandle`, `SafeWaitHandle`, `SafeProcessHandle`, `SafeBuffer`, …), `CriticalHandle`, `HandleRef`, `GCHandle`, a struct with an `IntPtr`/`void*` payload pretending to be typed (`struct HWND { IntPtr Value; }` is `IntPtr` in a costume), `Marshal.*`, `DangerousGetHandle()`.

**Why `void*` is in the list:** `void*` is `IntPtr` with a different spelling — it says "points at something" and refuses to say what. Every pointer names its pointee: `byte*`, `OVERLAPPED*`, `HPCON**`.

**Why `SafeHandle` is in the list:** `SafeHandle` is `protected IntPtr handle` + a refcount + a finalizer. It gives *lifetime* safety (no leak, no double close). It gives **zero type safety**: the value is erased the moment anything consumes it (`DangerousGetHandle`, every P/Invoke that takes it, `RandomAccess.*`), and `new SafeFileHandle(hwnd, ownsHandle: true)` compiles and will `close()` a window handle number — which is a live socket in some other table. A class name is not a type discipline; `HFILE*` is.

---

## No exceptions — correcting the AN0100 spec

The existing `An0100_RequireTypedPointersNotIntPtr.md` and `README-nuget.md` say: *"Isolate interop types in a dedicated project with `<RequireTypedPointersNotIntPtr>ignore</…>`; the interop boundary is the only place `IntPtr` should exist."* And its "What To Use Instead" shows `struct HWND { public IntPtr Value; }` with a note that the analyzer will flag it and to set `ignore`.

**That guidance is wrong and must be removed.** There is no project where `IntPtr` is the job. The interop project's job is *empty marker structs + `T*` signatures*. With the idiom above, the interop project builds clean under `disallow` like every other project. `ignore` is not a design — it is a TODO wearing a config value.

Concretely, in `An0100_RequireTypedPointersNotIntPtr.md`:
- "What To Use Instead → For handles": replace the `{ public IntPtr Value; }` struct and the `ignore` note with the empty-marker idiom above.
- "For pointer parameters": the GOOD example has `void* lpPreviousValue` — change to the real type (`PROC_THREAD_ATTRIBUTE_LIST*` or the documented pointee).
- "Migration Guide": remove `SafeProcessHandle hProcess;` and `SafeBuffer` as compliant targets; `IntPtr.Zero` → `(HWND*)null`, not `default(HWND)`; `(IntPtr)someInt` → there is no "if truly needed, isolate" — refactor.
- "Project Organization": delete step 1's `ignore`. Both steps are `disallow`.
- Add `void*` to the flagged-everywhere table (see AN0100 amendment below).

In `An0101_RequireUnsafeOnPInvokeImports.md`, the "CRITICAL TO HELP USERS UNDERSTAND" example passes `SafeFileHandle hInput, SafeFileHandle hOutput` and `out HPCON phPC` with `_handle.IsValid`. Correct it to `HFILE* hInput, HFILE* hOutput, HPCON** phPC`, `private HPCON* _handle;`, `if (_handle != null)`. This example is the one people copy; it must be right.

In `README-nuget.md` AN0100 section: delete the "Recommended: isolate … with `ignore`" paragraph; replace with the idiom.

---

## The Rules

### AN0100 amendment — `void*` is flagged everywhere
Add a third case to the existing syntax analyzer: any `PointerTypeSyntax` whose element type resolves to `SpecialType.System_Void`. Same severity property (`RequireTypedPointersNotIntPtr`). Message names the idiom (below).

### AN0102-A — Signatures that carry an erased type (symbol-based)
Register `OperationKind.Invocation`, `ObjectCreation`, `PropertyReference`, `FieldReference`, `MethodReference` (delegate creation), `EventReference`. For the resolved `IMethodSymbol` / `IPropertySymbol` / `IFieldSymbol`, flag when **any** of these is an erased type (definition below):
- return type
- any parameter type, including `ref`/`out`/`in`
- the containing type (so `IntPtr.Add`, `IntPtr.ToPointer`, `Marshal.*`, `GCHandle.*`, `SafeHandle.DangerousGetHandle` all fire)

"Erased type", recursively through `IPointerTypeSymbol`, `IArrayTypeSymbol`, `Nullable<T>`, `Span<T>`/`ReadOnlySpan<T>`/`Memory<T>`, `ref`:
- `System.IntPtr`, `System.UIntPtr` (SpecialType)
- `void*` (pointer whose pointee is `System.Void`)
- any type satisfying AN0102-B

This is symbol-based, so `var h = File.OpenHandle(path);` fires even though the caller never wrote a forbidden token — the *callee's* signature carries `SafeFileHandle`.

### AN0102-B — Erased-handle types (type-based)
Same `IdentifierName`/`GenericName`/`QualifiedName`/`VarKeyword`-inferred hooks as AN0100, plus the AN0102-A operation hooks. Flag any reference to an `INamedTypeSymbol` that:
- derives from `System.Runtime.InteropServices.SafeHandle` or `System.Runtime.InteropServices.CriticalHandle` (walk `BaseType`)
- is `System.Runtime.InteropServices.HandleRef` or `System.Runtime.InteropServices.GCHandle`
- is `System.Runtime.InteropServices.Marshal` (static class; all members are erased by construction)
- **has any public or protected field/property whose type is erased** (general clause — catches `struct HWND { public IntPtr Value; }` in *someone else's* assembly, `Process.Handle`, `FileStream.SafeFileHandle`, `WaitHandle.SafeWaitHandle`, `Delegate`-to-function-pointer helpers). This clause is what makes the list above illustrative rather than exhaustive.

Known BCL surface this catches (the test matrix, not an allowlist): `File.OpenHandle`, `RandomAccess.*`, `FileStream.SafeFileHandle`, `FileStream(SafeFileHandle, …)` ctors, `Process.Handle`/`MainWindowHandle`, `WaitHandle.SafeWaitHandle`, `Marshal.*`, `GCHandle.*`, `SafeHandle.DangerousGetHandle`, `NativeLibrary.Load` (returns `IntPtr`), `Type.TypeHandle`/`RuntimeTypeHandle.Value`.

### Explicitly NOT flagged
- `nint`/`nuint` anywhere as *integers* — outside P/Invoke unconditionally; inside P/Invoke for `SIZE_T`/`DWORD_PTR`/`ULONG_PTR`/bitfields (AN0100 emits a review-prompt warning there, not an error; decided 2026-09-10: "fully acceptable to use nuint for a bitfield").
- `Span<byte>`, `ReadOnlySpan<T>`, `Memory<T>`, `MemoryMarshal.Cast<TFrom,TTo>` — typed, bounds-checked; not erasure.
- `T*` where `T` is a real struct/primitive — this **is** the idiom.
- `FileStream`, `Stream`, `File.ReadAllBytes`, etc. — the type itself is not erased; only its `SafeFileHandle` *member* is (and AN0102-B fires on the member reference, not on `FileStream`).
- Types in generated code (`[GeneratedCode]` / generated files), standard analyzer behaviour.

---

## Configuration

```xml
<PropertyGroup>
  <ProhibitErasedHandlesAndPointers>warn</ProhibitErasedHandlesAndPointers>   <!-- warn (default) | disallow | ignore -->
</PropertyGroup>
```

`ignore` exists for **rollout** (a project that has not been converted yet), never as a destination. There is no allowlist property, no per-namespace exemption, no `[AllowErasedHandle]` attribute. If a BCL API exists only in an erased shape, the answer is: declare the P/Invoke yourself with marker structs and `T*`, or use the higher-level managed API that does not leak the handle (Blast: `FileStream` with `bufferSize: 0` replaced `RandomAccess` at zero measured cost — 100 MB in 52 ms before and after).

---

## Error Messages — MUST show the idiom, every time

The user's instruction, verbatim: *"the ERRORS should CLEARLY SHOW THE IDIOM."* A diagnostic that says "don't use X" without showing the replacement is a puzzle, not a fix. Every message in this family ends with the same two-line idiom block so it is impossible to read the error and not know what to type instead.

```
AN0102: 'File.OpenHandle' returns 'SafeFileHandle', an erased OS handle (IntPtr with a finalizer).
        Handles are empty marker structs and the pointer IS the handle:
            public unsafe struct HFILE { }          [DllImport("kernel32")] static extern unsafe bool ReadFile(HFILE* hFile, byte* buffer, uint n, uint* read, OVERLAPPED* ov);
        Never IntPtr, never void*, never SafeHandle. Or use a managed API that does not expose the handle (FileStream).
        See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md

AN0102: 'Marshal.ReadInt32(IntPtr, int)' accepts 'IntPtr' (parameter 'ptr') — an erased pointer.
        Pointers name their pointee:  int* p = …; int v = *p;   never IntPtr, never void*.
        See: …TypeSafePInvoke.md

AN0102: 'SafeFileHandle' is an erased OS handle type (derives from SafeHandle = IntPtr + finalizer).
        Handles are empty marker structs and the pointer IS the handle:
            public unsafe struct HFILE { }     HFILE* h;     (HFILE*)null for "no handle"
        See: …TypeSafePInvoke.md

AN0100: Do not use 'void*'. void* is IntPtr with a different spelling — it says "points at something" and refuses to say what.
        Name the pointee:  OVERLAPPED* overlapped;  byte* buffer;  HPCON** phPC;
        See: …TypeSafePInvoke.md
```

Message construction rules:
1. First line: **what** was found, **where** (member name + the offending type + parameter name if applicable), and **why it is erased** in ≤ 12 words.
2. Second/third lines: **the idiom**, as code, specialized to the finding when possible (a `SafeFileHandle` finding shows `HFILE*`; an `HWND`-ish name shows `HWND*`; unknown → generic `HANDLE_NAME`). Always include both halves: the empty struct **and** a `T*` use.
3. "Never IntPtr, never void*, never SafeHandle." appears in every AN0102 message — the three spellings of the same mistake, named together so nobody swaps one for another.
4. Last line: the docs link. `docs/TypeSafePInvoke.md` must be rewritten to match (it currently does not exist in final form per the AN0100 spec's note).

---

## Test Matrix (write these first)

| # | Code under test | Expect |
|---|---|---|
| 1 | `var h = File.OpenHandle(p, FileMode.Open);` | AN0102-A on invocation (returns SafeFileHandle) |
| 2 | `RandomAccess.Read(h, span, 0);` | AN0102-A (param SafeFileHandle) |
| 3 | `SafeFileHandle f;` (declaration) | AN0102-B |
| 4 | `class MyHandle : SafeHandleZeroOrMinusOneIsInvalid` | AN0102-B on base type |
| 5 | `fs.SafeFileHandle` (property read) | AN0102-A/B |
| 6 | `Marshal.AllocHGlobal(16)` | AN0102-A (returns IntPtr; containing type Marshal) |
| 7 | `GCHandle.Alloc(o)` | AN0102-A/B |
| 8 | `h.DangerousGetHandle()` | AN0102-A |
| 9 | `Action<IntPtr> a = Foo;` (delegate creation) | AN0102-A on MethodReference |
| 10 | `void* p = null;` | AN0100 (void* case) |
| 11 | `static extern void F(void* p);` | AN0100 |
| 12 | `struct HWND { public IntPtr Value; }` referenced from another project | AN0102-B (public erased member) |
| 13 | `public unsafe struct HWND { }` + `static extern bool F(HWND* h);` | **clean** |
| 14 | `(HWND*)null` | **clean** |
| 15 | `MemoryMarshal.Cast<byte, ushort>(span)` | **clean** |
| 16 | `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0)` | **clean** |
| 17 | `nint n = 5; n += 2;` outside P/Invoke | **clean** |
| 18 | `ProhibitErasedHandlesAndPointers=ignore` with case 1 | no diagnostic |
| 19 | `=disallow` with case 1 | severity Error |
| 20 | Every diagnostic message in cases 1–12 | contains `unsafe struct`, contains `*`, contains `Never IntPtr, never void*, never SafeHandle` (AN0102) or `Name the pointee` (AN0100 void*) |

---

## Deliverables

1. `src/ProhibitErasedHandlesAndPointers/ProhibitErasedHandlesAndPointersAnalyzer.cs` (AN0102-A + B) with the MSBuild property plumbing matching the other analyzers.
2. AN0100 analyzer: `void*` case + message update.
3. Spec corrections: `An0100_*.md`, `An0101_*.md`, `README-nuget.md` as listed under "No exceptions".
4. `docs/TypeSafePInvoke.md` — the canonical idiom page every message links to. Content = the IDIOM section of this file, expanded with the ConPTY `HPCON**` bug from the AN0100 spec as the worked example.
5. Tests per the matrix.
6. Consumer proof: `AN_Mirica/src/AN.BlastBuffer` and `AN.CodeEditorBlast` build clean under `disallow` (they should already — the `FileStream` rewrite landed 2026-09-10). `AN.PtyConsole/Win32ProcessTreeWatcher.cs` and `AN.PtyConsole.PtyDotNet/Posix.cs` currently emit AN0100 and are the first conversion targets for the idiom.

## Related
- **AN0100 RequireTypedPointersNotIntPtr** — syntax-level; this task amends it (void*) and corrects its guidance.
- **AN0101 RequireUnsafeOnPInvokeImports** — the `unsafe` marker; this task corrects its example.
- **AN0104 ProhibitPlatformImports**, **AN0105 ProhibitNamespaceAccess** — siblings; AN0102 could be expressed partly as AN0105 over `System.Runtime.InteropServices`, but the "public erased member" clause and the signature walk are not namespace rules, so it is its own analyzer.