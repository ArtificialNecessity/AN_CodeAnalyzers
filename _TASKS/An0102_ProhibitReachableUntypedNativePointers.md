# AN0102: ProhibitReachableUntypedNativePointers — and the AN0100 / AN0101 corrections it requires

## Summary

| | |
|---|---|
| **ID** | AN0102 (new) + amendments to AN0100 and AN0101 |
| **Name** | `ProhibitReachableUntypedNativePointers` (analyzer class, folder, MSBuild property) |
| **Category** | AN.TypeSafety |
| **Default Severity** | Warning |
| **Configurable** | Yes — `disallow` / `warn` / `ignore` (rollout knob only; see "No exceptions") |
| **Origin** | 2026-09-10, Blast buffer review (AN_Mirica). `AN.BlastBuffer` built clean under AN0100=`disallow` while holding an untyped OS handle: `File.OpenHandle()` → `SafeFileHandle` → `RandomAccess.Read(SafeFileHandle, …)`. No `IntPtr` token anywhere in the code; the `IntPtr` was one field deeper, inside a BCL class. AN0100 is a *syntax* rule and cannot see it. |
| **Revised** | 2026-09-11 — renamed; vocabulary and rule definition corrected. The earlier draft had a type-level "class with an untyped member is itself untyped" clause that was internally inconsistent (it would have flagged `FileStream`, `Process`, and every `typeof`). Replaced by the **source/sink** rule derived from the litmus test below. |

**Two goals, one rule.**

1. **Safe managed code never touches a native pointer.** No `IntPtr`, no `void*`, no `SafeHandle`, no BCL member that returns or accepts one. If a class is not marked `unsafe`, nothing in it can reach a native pointer — not by spelling one (AN0100) and not by calling something that carries one (AN0102).
2. **FFI and platform code (a) is marked `unsafe` and (b) uses type-checked native pointer types.** `[DllImport]` methods and the types that hold handles are `unsafe` (AN0101), and every pointer and handle in them is `T*` to a named struct — `HWND*`, `HFILE*`, `OVERLAPPED*` — so the compiler type-checks native calls exactly as it type-checks managed ones.

**WE DO NOT ALLOW UNTYPED NATIVE POINTERS.** `IntPtr`, `void*`, `SafeHandle` — not held, not passed, not received, not declared, not called through. Not in safe code, not in FFI code, not in an "interop project". Ever. Code that touches one can go BOOM: you can hand a file handle to `SetForegroundWindow`, you can `hwnd++` and hand it back, you can `new SafeFileHandle(hwnd, ownsHandle: true)` and close a live socket. The compiler cannot stop any of that because all of those values are the same type. This analyzer stops it.

**The intent of AN0100 was always this rule.** Not "we never write the word `IntPtr`." This task closes the gap between those two statements, and corrects guidance in the existing AN0100/AN0101 specs that currently teaches the wrong idiom.

---

## Philosophy — what this is and is not

**`IntPtr` throws away type checking the C# compiler is perfectly able to do.** Native APIs have many distinct pointer and handle types — `HWND`, `HFILE`, `HPCON`, `HDF*`, `NEOERR*` — and they are not interchangeable. `IntPtr` flattens all of them to one type, so `SetForegroundWindow(hFile)` compiles. That flattening existed for one reason: **VB.NET** had no pointer types, so the CLR needed a pointer-sized value type every language could spell. We do not use VB.NET. C# has `unsafe struct HWND { }` and `HWND*`, and with them the compiler type-checks native handles exactly as it type-checks everything else. Every `IntPtr` in a C# codebase is a place where we told the compiler to stop checking. We want the compiler checking.

**This is not a sandbox.** Compiled C# can always crash the machine — `unsafe`, `stackalloc`, a wrong `[DllImport]` signature, reflection over `SafeHandle`, `Unsafe.As`. The programmer is in charge of the code and can route around any analyzer. We are not trying to make that impossible.

**We are raising the friction on paths that lead to dangerous bugs and exploitable surfaces later.** An untyped native pointer is exactly such a path: it is cheap to write today, it compiles silently, and it becomes a crash, a use-after-close on the wrong handle table, or an attacker-controlled dereference months from now. Making the typed idiom the *only* thing that builds clean means the easy path and the safe path are the same path. This matters most when **an AI is writing the code**: an AI reaches for whatever compiles, and `File.OpenHandle` → `RandomAccess.Read` compiles. With AN0102, it does not — and the error message shows the idiom, so the next attempt is the right one.

### The litmus test (the rule is derived from this, not from a list)

> *Can our code, in any way, obtain, distort, or hand off an untyped native pointer such that something will dereference it?*

- Obtaining one is a **source**: `File.OpenHandle()`, `proc.Handle`, `fs.SafeFileHandle`, `DangerousGetHandle()`.
- Handing one off is a **sink**: `RandomAccess.Read(SafeFileHandle, …)`, `new FileStream(SafeFileHandle, …)`, `new SafeFileHandle(IntPtr, bool)`, `x.SafeFileHandle = …`, `new Span<T>(void*, int)`, any `[DllImport]` taking `IntPtr`.
- Declaring or naming the type is **holding** one: `SafeFileHandle f;`, `var h = File.OpenHandle()`, `class X : SafeHandle`.

All three are banned. Nothing else is. A `FileStream` opened by path is a managed object: reading from it gives us nothing to distort and calls no sink. `fs.SafeFileHandle { get; }` hands us a value we cannot declare, cannot store, cannot pass anywhere (every acceptor is a sink) — it fires as a source the moment we read it. `SafeFileHandle { get; set; }` would be a sink, because we could hand it any `SafeFileHandle` whose `IntPtr` we butchered.

---

## Vocabulary (use these words, and only these)

| Term | Meaning |
|---|---|
| **untyped native pointer** | A pointer-sized value that names nothing about what it points at or what handle table it belongs to: `IntPtr`, `UIntPtr`, `void*`, any `SafeHandle`/`CriticalHandle`, and any struct that is one of those with a name painted on ("IntPtr in a costume"). Handles are pointers; a Win32 `HANDLE` is an untyped native pointer. |
| **typed opaque pointer** | `T*` where `T` is an **empty marker struct**. Opaque because managed code never reads through it; typed because `HWND*` and `HFILE*` are different types to the compiler. **This is the idiom.** |
| **IntPtr in a costume** | `struct HWND { IntPtr Value; }`. Looks typed; is not — the payload is still an untyped native pointer, and any code that unwraps `.Value` is back to BOOM. `GCHandle`, `HandleRef`, `RuntimeTypeHandle` are BCL costumes. |

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

// Null handle:            (HWND*)null      — NOT IntPtr.Zero, NOT default(HWND) (a struct value is not a handle; the pointer is)
// INVALID_HANDLE_VALUE:   (HFILE*)(-1)     — integer→pointer cast, sign-extended; compare with ==, never arithmetic
// HWND_BROADCAST etc.:    (HWND*)0xFFFF
```

**Forbidden, all of them, no exceptions:** `IntPtr`, `UIntPtr`, `void*` (at any pointer depth: `void**` too), `SafeHandle` and every subclass (`SafeFileHandle`, `SafeWaitHandle`, `SafeProcessHandle`, `SafeBuffer`, …), `CriticalHandle`, `HandleRef`, `GCHandle`, `struct HWND { IntPtr Value; }` (IntPtr in a costume), `DangerousGetHandle()`, and every BCL method that takes or returns any of the above (`Marshal.AllocHGlobal`, `Marshal.ReadInt32(IntPtr,…)`, `NativeMemory.Alloc`, `new Span<T>(void*, int)`, `Buffer.MemoryCopy`, `File.OpenHandle`, `RandomAccess.*`, `Process.Handle`, …).

**`nint`/`nuint` are integers, not pointers.** They are correct for `SIZE_T`, `DWORD_PTR`, `ULONG_PTR`, bitfields. Not flagged outside P/Invoke; AN0100 emits a review-prompt warning inside P/Invoke declarations (decided 2026-09-10: "fully acceptable to use nuint for a bitfield"). The analyzer distinguishes them from `IntPtr` via `INamedTypeSymbol.IsNativeIntegerType`.

**Why `void*` is in the list:** `void*` is `IntPtr` with a different spelling — it says "points at something" and refuses to say what. Every pointer names its pointee: `byte*`, `OVERLAPPED*`, `HPCON**`. Passing a `byte*` to a `void*` parameter is still writing code that uses an untyped native pointer — the call site is that code.

**Why `SafeHandle` is in the list:** `SafeHandle` is `protected IntPtr handle` + a refcount + a finalizer. It gives *lifetime* safety (no leak, no double close). It gives **zero type safety**: the value is untyped the moment anything consumes it (`DangerousGetHandle`, every P/Invoke that takes it, `RandomAccess.*`), and `new SafeFileHandle(hwnd, ownsHandle: true)` compiles and will `close()` a window handle number — which is a live socket in some other table. A class name is not a type discipline; `HFILE*` is.

---

## Practical consequences (state them; do not soften them)

1. **Pointers cannot be generic type arguments, cannot be boxed, cannot be hoisted across `await`.** No `Dictionary<HWND*, …>`, `Task<HFILE*>`, `Func<HWND*>`, no `HFILE*` local live across an `await`. The permitted wrapper is a struct with a **typed** pointer payload: `public readonly unsafe struct HFILE_HANDLE { public readonly HFILE* Value; }`. This is not a costume — the payload is typed, `Value` cannot be handed to `SetForegroundWindow`, and it is a legal generic argument.
2. **Every type that holds a handle is `unsafe`.** `AllowUnsafeBlocks` in every consuming project, `unsafe` on the member or type. This is consistent with AN0101 (`[DllImport]` methods must be `unsafe`): there was never anything safe about holding an OS handle; the keyword now says so.
3. **Unmanaged allocation:** `NativeMemory.Alloc` returns `void*` and `Marshal.AllocHGlobal` returns `IntPtr` — both flagged. Use `stackalloc`, `GC.AllocateUninitializedArray<T>(n, pinned: true)` + `fixed`, or a P/Invoke you declare yourself that returns `T*` (`HeapAlloc` → `BYTE*`).
4. **Wrapping a `T*` in a span:** `new Span<T>(void*, int)` is flagged. Use `MemoryMarshal.CreateSpan(ref *p, n)` / `MemoryMarshal.CreateReadOnlySpan(ref *p, n)`.
5. **Copying:** `Buffer.MemoryCopy(void*, void*, …)` and `Unsafe.CopyBlock(void*, void*, uint)` are flagged. Use `Unsafe.CopyBlock(ref byte, ref byte, uint)`, `Span<T>.CopyTo`, or `new Span<T>` built via `MemoryMarshal.CreateSpan`.
6. **Native callbacks with managed context:** `GCHandle.ToIntPtr` is flagged. Use `delegate* unmanaged<…>` + `[UnmanagedCallersOnly]`, and pass an integer token that indexes a static registry — never a managed object address.
7. **Function pointers:** `delegate* unmanaged<HWND*, int, bool>` names every parameter type and is the idiom for callbacks. `Marshal.GetDelegateForFunctionPointer`/`GetFunctionPointerForDelegate` are flagged (IntPtr in the signature).
8. **`Marshal.GetLastWin32Error()` / `GetLastPInvokeError()` / `SetLastPInvokeError(int)` / `ThrowExceptionForHR(int)`** take and return `int`. They are not untyped native pointers and are **not flagged**. This is not an allowlist; it is the rule applied to their signatures.

---

## No exceptions — remaining corrections to sibling specs

There is no project where `IntPtr` is the job. The interop project's job is *empty marker structs + `T*` signatures*, and with them it builds clean under `disallow` like every other project. `ignore` is not a design — it is a TODO wearing a config value.

`An0100_RequireTypedPointersNotIntPtr.md` was corrected 2026-09-10/11 and already states this (empty-marker idiom, `RESERVED_MUST_BE_NULL*`, no `ignore` step, `void*` banned, VB.NET rationale). Verify on re-read; nothing further is expected there.

`An0101_RequireUnsafeOnPInvokeImports.md` — the "CRITICAL TO HELP USERS UNDERSTAND" example was corrected 2026-09-10 (`HFILE* hInput, HFILE* hOutput, HPCON** phPC`, `private HPCON* _handle;`, `if (_handle != null)`). Verified 2026-09-11; nothing further.

In `README-nuget.md`: the AN0100 section must show the idiom and the VB.NET rationale, must not recommend `ignore` for an interop project, and an AN0102 section must be added with the philosophy paragraph (friction, not sandbox) and the source/sink summary.

---

## The Rules

### Division of labour: AN0100 vs AN0102

| | AN0100 `RequireTypedPointersNotIntPtr` | AN0102 `ProhibitReachableUntypedNativePointers` |
|---|---|---|
| Catches | untyped native pointers **we spelled**: `IntPtr`, `UIntPtr`, `void*` tokens; `nint`/`nuint` review-prompt in P/Invoke | untyped native pointers **we did not spell** but that *reach* our code through someone else's signature or type |
| How | syntax — the token | symbols — callee return/parameter/setter types, declared and inferred types |
| Example | `IntPtr h; void* p; struct HWND { IntPtr Value; }` | `var h = File.OpenHandle(..)`, `RandomAccess.Read(h, ..)`, `fs.SafeFileHandle`, `proc.Handle`, `new Span<byte>(p, n)`, `SafeFileHandle f;`, `class X : SafeHandle`, `new SafeFileHandle(..)` |

**AN0100 implementation gap (prerequisite, not a spec change):** the AN0100 spec already bans `void*` everywhere; `RequireTypedPointersNotIntPtrAnalyzer.cs` has no `PointerTypeSyntax` case. Add it: any `PointerTypeSyntax` whose ultimate element type is `SpecialType.System_Void` (`void**` fires once, on the innermost). Same severity property. Message per "Error Messages".

### AN0102 — Definition: what is an untyped native pointer type

A type `T` **is an untyped native pointer** if any of the following holds. This is a property of the **value's own type**, never of a class that happens to expose one (see "Classes" below).

1. `T` is `System.IntPtr` or `System.UIntPtr`.
   **Implementation finding (2026-09-11):** on .NET 7+ the compiler *unifies* `nint` with `System.IntPtr` (`RuntimeFeature.NumericIntPtr`) — same symbol, `IsNativeIntegerType == true` for both, no `NativeIntegerAttribute`. In **metadata** there is no way to tell `Marshal.AllocHGlobal(): IntPtr` from `Unsafe.Add(ref T, nint)`. Decision:
   - For symbols declared in **our source**, the spelling is available: `nint`/`nuint` is an integer (clean under AN0102; AN0100 review-prompts it in P/Invoke), `IntPtr` is AN0100's token.
   - For **metadata** symbols, a native-int-sized value crossing into code we cannot see is treated as a pointer and **fires**. `Unsafe.Add(ref x, (nint)1)` and `Math.Max(nint, nint)` fire; use the `int`/`long` overloads. This is friction on an ambiguous path, applied deliberately.
   - AN0102-B never reports IntPtr-family types (spelled tokens are AN0100's; an inferred `var n = a + b` on nints is arithmetic; `var p = Marshal.AllocHGlobal(16)` is already reported at the call as a source).
2. `T` is a pointer type whose ultimate pointee is `System.Void` (`void*`, `void**`, …).
3. `T` is a class deriving from `System.Runtime.InteropServices.SafeHandle` or `System.Runtime.InteropServices.CriticalHandle` (walk `BaseType`). These classes *are* untyped pointers: `new SafeFileHandle(IntPtr, bool)` and `SetHandle(IntPtr)` let any integer become a handle.
4. `T` is a **struct** that IS an untyped pointer with a name painted on ("IntPtr in a costume"), recursive through structs only:
   - (a) an instance field (any accessibility) of untyped type — `struct HWND { IntPtr Value; }` in a real assembly; or
   - (b) a **public instance property or field** of untyped type, or a **user-defined conversion** to/from one.
   **Implementation finding (2026-09-11):** clause (b) is required because the compiler builds against *reference assemblies*, where GenAPI replaces private struct fields with `_dummyPrimitive`/`_dummy` — `GCHandle`'s `IntPtr _handle` is invisible at compile time, but its `explicit operator IntPtr` is not. `GCHandle` (conversion), `HandleRef` (`.Handle`), `RuntimeTypeHandle` (`.Value`) fall out of (b) with no list.
5. Applied recursively through `IPointerTypeSymbol` (`SafeFileHandle*`), `IArrayTypeSymbol` (`IntPtr[]`), `Nullable<T>`, `Span<T>`/`ReadOnlySpan<T>`/`Memory<T>`/`ReadOnlyMemory<T>`, and `ref`/`in`/`out`.

**Classes are never untyped native pointers by membership.** `Process`, `FileStream`, `Type`, `WaitHandle`, `Socket` are managed objects; holding a reference to one is not holding a pointer, and `new FileStream(path, …)` gives our code nothing to butcher. Their handle-carrying *members* are **sources** and **sinks** (below) and fire the moment our code touches them: `fs.SafeFileHandle`, `proc.Handle`, `new FileStream(SafeFileHandle, …)`.

**`Marshal` and `GCHandle` are not special cases.** Every `Marshal` member that takes or returns an untyped native pointer fires by signature; `Marshal.GetLastWin32Error()` does not. `GCHandle` is a costume struct (rule 4b: `explicit operator IntPtr`) and fires wherever it is declared, returned, or accepted.

Implementation: memoize per compilation (`ConcurrentDictionary<ITypeSymbol, bool>` with `SymbolEqualityComparer.Default`), created in `RegisterCompilationStartAction`. Rule 4 walks metadata fields and must not be recomputed per reference.

### AN0102-A — Sources and sinks: our code calls or binds a member whose signature carries an untyped native pointer (symbol-based)

A crash needs two things in our code: a **source** (we obtain an untyped pointer value we can distort — hold, add to, cast, rewrap) and a **sink** (we hand an untyped pointer value to something that dereferences, closes, or dispatches on it). Ban both and there is no path.

Register `OperationKind.Invocation`, `ObjectCreation`, `PropertyReference`, `FieldReference`, `EventReference`, `MethodReference` (delegate creation), `DelegateCreation`, `FunctionPointerInvocation`, and assignments to property/field references (setters). For the resolved `IMethodSymbol` / `IPropertySymbol` / `IFieldSymbol` / `IEventSymbol`, flag when **any** of these is an untyped native pointer:

- **A-1 Source** — the return / property-get / field type: `File.OpenHandle(…)`, `proc.Handle`, `fs.SafeFileHandle`, `typeof(X).TypeHandle`, `NativeMemory.Alloc(n)`, `h.DangerousGetHandle()`, `GCHandle.Alloc(o)`.
- **A-2 Sink** — any parameter type (including `ref`/`out`/`in`) or the type of a property/field being **assigned**, **regardless of how we spelled the argument**: `RandomAccess.Read(h, …)`, `new FileStream(h, FileAccess.Read)`, `new Span<byte>(p, n)`, `Buffer.MemoryCopy(p, q, …)`, `Marshal.ReadInt32(ptr, 0)`, `new SafeFileHandle((nint)0x1234, false)`, `x.SafeFileHandle = h`. The argument's spelling is irrelevant: `(nint)0x1234` is an integer and AN0100 is right not to flag it, but the *callee's parameter* is `IntPtr` in metadata and that is what makes the call a sink.
- **A-3 Receiver** — the containing type of an *instance* member (`h.IsInvalid` where `h : SafeHandle`). Static members are covered by A-1/A-2 on their own signatures.

Dedupe: one diagnostic per location when several sub-rules hit the same node (e.g. `h.DangerousGetHandle()` is A-1 and A-3).

**Canonical example — the thing this rule exists to prevent:**
```csharp
new SafeFileHandle((nint)0x1234, ownsHandle: false)   // AN0102-B on 'SafeFileHandle'; A-2 sink (ctor param IntPtr); A-1 source (returns SafeFileHandle)
RandomAccess.Read(thatHandle, buffer, 0)               // A-2 sink → kernel ReadFile() on handle 0x1234
```
No `IntPtr` token appears in either line. AN0100 sees nothing. AN0102 sees three violations.

This is symbol-based, so `var h = File.OpenHandle(path);` fires even though the caller never wrote a forbidden token — the *callee's* signature carries `SafeFileHandle`.

### AN0102-B — Our code declares or names an untyped native pointer type (type-based)

`IdentifierName` / `GenericName` hooks (a `QualifiedName`'s right-hand identifier is an `IdentifierName`, so `Microsoft.Win32.SafeHandles.SafeFileHandle f;` reports once, on `SafeFileHandle`). Flag any type reference (declaration of a field/local/parameter/return/type argument/base type/cast target/`typeof`/`is`/`as`, or an inferred `var`) whose type is an untyped native pointer under the definition. Report at the type syntax (or the `var` keyword). **Either side of a member access is skipped**: the name-part (`x.Foo`) is a member, and the expression-part of a static access (`GCHandle.Alloc`) is a lookup that holds no value — the member reference is judged by A.

**Do not report AN0102-B on IntPtr-family types at all** — spelled `IntPtr`/`UIntPtr`/`void*` tokens are AN0100's; `nint`/`nuint` are integers; and an inferred `var` of that type is either arithmetic or already reported at the call (see Definition rule 1). AN0102-B owns everything AN0100 cannot see: `SafeFileHandle f;`, `var h = File.OpenHandle(…)`, `class Mine : SafeHandleZeroOrMinusOneIsInvalid`, `HWND_COSTUME w;` from another assembly, `GCHandle g;`, `var g = GCHandle.Alloc(o)`.

Known BCL surface this catches (the test matrix, not an allowlist): `File.OpenHandle`, `RandomAccess.*`, `FileStream.SafeFileHandle`, `FileStream(SafeFileHandle, …)` ctors, `Process.Handle`/`MainWindowHandle`/`ProcessorAffinity`, `WaitHandle.SafeWaitHandle`/`Handle`, `Socket.Handle`, `Marshal.*` (pointer-carrying members), `GCHandle.*`, `SafeHandle.DangerousGetHandle`, `NativeLibrary.Load`/`GetExport`, `Type.TypeHandle`/`RuntimeTypeHandle.Value`, `MethodBase.MethodHandle`, `NativeMemory.*`, `Span<T>(void*, int)`, `Buffer.MemoryCopy`, `Unsafe.AsPointer`/`Unsafe.Read<T>(void*)`/`Unsafe.CopyBlock(void*, …)`, `Delegate.Method`→ no (`MethodInfo` is a class; only `.MethodHandle` fires).

### Explicitly NOT flagged
- `nint`/`nuint` as *integers* in **our own** code — locals, arithmetic, our own members spelled `nint` (outside P/Invoke unconditionally; inside P/Invoke AN0100 review-prompts). **Not** BCL callees with `nint` parameters — see Definition rule 1: on .NET 7+ metadata cannot distinguish them from `IntPtr`, and they fire.
- `Span<byte>`, `ReadOnlySpan<T>`, `Memory<T>`, `MemoryMarshal.Cast<TFrom,TTo>`, `MemoryMarshal.CreateSpan(ref T, int)` — typed, bounds-checked.
- `T*` where `T` is a real struct/primitive or an empty marker struct — this **is** the idiom. `T**`, `T***` likewise.
- `readonly struct HFILE_HANDLE { HFILE* Value; }` — a struct whose pointer payload is **typed**; the permitted generic/async wrapper.
- `delegate* unmanaged<…>` function pointers with fully named parameter types.
- `FileStream`, `Process`, `Type`, `Stream`, `File.ReadAllBytes`, `typeof(X)`, `Process.Start()`, `new FileStream(path, …)` — classes / class-returning members; nothing untyped is in our hands.
- `Marshal.GetLastWin32Error()`, `Marshal.GetLastPInvokeError()`, `Marshal.SetLastPInvokeError(int)`, `Marshal.ThrowExceptionForHR(int)`, `Marshal.SizeOf<T>()` — no pointer in the signature.
- Types in generated code (`[GeneratedCode]` / generated files), standard analyzer behaviour.

---

## Configuration

```xml
<PropertyGroup>
  <ProhibitReachableUntypedNativePointers>warn</ProhibitReachableUntypedNativePointers>   <!-- warn (default) | disallow | ignore -->
</PropertyGroup>
```

`ignore` exists for **rollout** (a project that has not been converted yet), never as a destination. There is no allowlist property, no per-namespace exemption, no `[AllowUntypedNativePointer]` attribute. If a BCL API exists only in an untyped shape, the answer is: declare the P/Invoke yourself with marker structs and `T*`, or use the higher-level managed API that does not leak the handle (Blast: `FileStream` with `bufferSize: 0` replaced `RandomAccess` at zero measured cost — 100 MB in 52 ms before and after).

---

## Error Messages — MUST show the idiom, every time

The user's instruction, verbatim: *"the ERRORS should CLEARLY SHOW THE IDIOM."* A diagnostic that says "don't use X" without showing the replacement is a puzzle, not a fix. Every message in this family ends with the same idiom block so it is impossible to read the error and not know what to type instead. Messages are multi-line (literal `\n` in the `MessageFormat`); MSBuild collapses them in console output, VS/Rider show them in full in the tooltip and Error List detail — that is acceptable.

```
AN0102: 'File.OpenHandle' returns 'SafeFileHandle', an untyped native handle (IntPtr with a finalizer).
        Handles are typed opaque pointers — an empty marker struct, and the pointer IS the handle:
            public unsafe struct HFILE { }          [DllImport("kernel32")] static extern unsafe bool ReadFile(HFILE* hFile, byte* buffer, uint n, uint* read, OVERLAPPED* ov);
        Never IntPtr, never void*, never SafeHandle. Or use a managed API that does not expose the handle (FileStream).
        See: https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md

AN0102: 'Marshal.ReadInt32(IntPtr, int)' takes 'IntPtr' (parameter 'ptr'), an untyped native pointer.
        Pointers name their pointee:  int* p = …; int v = *p;
        Never IntPtr, never void*, never SafeHandle.
        See: …TypeSafePInvoke.md

AN0102: 'Span<byte>(void*, int)' takes 'void*' (parameter 'pointer'), an untyped native pointer.
        Build the span from the typed pointer:  MemoryMarshal.CreateSpan(ref *p, n)
        Never IntPtr, never void*, never SafeHandle.
        See: …TypeSafePInvoke.md

AN0102: 'SafeFileHandle' is an untyped native handle type (derives from SafeHandle = IntPtr + finalizer).
        Handles are typed opaque pointers — an empty marker struct, and the pointer IS the handle:
            public unsafe struct HFILE { }     HFILE* h;     (HFILE*)null for "no handle"
        Never IntPtr, never void*, never SafeHandle.
        See: …TypeSafePInvoke.md

AN0102: 'HWND' (Some.Other.Assembly) is IntPtr in a costume: field 'Value' is 'IntPtr'.
        The struct must be EMPTY and the pointer IS the handle:
            public unsafe struct HWND { }      HWND* hWnd;
        Never IntPtr, never void*, never SafeHandle.
        See: …TypeSafePInvoke.md

AN0100: Do not use 'void*'. void* is IntPtr with a different spelling — it says "points at something" and refuses to say what.
        Name the pointee:  OVERLAPPED* overlapped;  byte* buffer;  HPCON** phPC;
        See: …TypeSafePInvoke.md
```

Message construction rules:
1. First line: **what** was found, **where** (member name + the offending type + parameter name if applicable), and **why it is untyped** in ≤ 12 words. Costume structs name the offending field.
2. Second/third lines: **the idiom**, as code, specialized to the finding when possible (a `SafeFileHandle` finding shows `HFILE*`; an `HWND`-ish name shows `HWND*`; a `void*` parameter finding shows the specific typed replacement if known — `MemoryMarshal.CreateSpan` for `Span<T>(void*,int)`, `Unsafe.CopyBlock(ref, ref, uint)` for `MemoryCopy`; unknown → generic `HANDLE_NAME`). For handle findings always include both halves: the empty struct **and** a `T*` use.
3. "Never IntPtr, never void*, never SafeHandle." appears in every AN0102 message — the three spellings of the same mistake, named together so nobody swaps one for another.
4. Last line: the docs link. `docs/TypeSafePInvoke.md` must be rewritten to match (see Deliverables).
5. Messages say **untyped native pointer** / **untyped native handle**. No other synonym.

---

## Test Matrix (write these first)

| # | Code under test | Expect |
|---|---|---|
| 1 | `var h = File.OpenHandle(p, FileMode.Open);` | AN0102-A1 on invocation (returns SafeFileHandle) + AN0102-B on `var` |
| 2 | `RandomAccess.Read(h, span, 0);` | AN0102-A2 (param SafeFileHandle) |
| 3 | `SafeFileHandle f;` (declaration) | AN0102-B, exactly one diagnostic |
| 4 | `class MyHandle : SafeHandleZeroOrMinusOneIsInvalid` with `: base(true)` | AN0102-B on base type, exactly one (`base(...)` constructs nothing new) |
| 5 | `fs.SafeFileHandle` (property read) | AN0102-A1; **`fs` / `FileStream` itself clean** |
| 6 | `var p = Marshal.AllocHGlobal(16)` | AN0102-A1 on the call (returns IntPtr); **no** B on `var` (IntPtr-family) |
| 7 | `var g = GCHandle.Alloc(o)` | AN0102-A1 on the call (returns GCHandle, costume by rule 4b) + B on `var`; **no** B on the `GCHandle` in `GCHandle.Alloc` (static-access lookup) |
| 8 | `var p = h.DangerousGetHandle()` | B on `SafeFileHandle h` param + AN0102-A1 on the call → **one** on the call (source wins over A3); no B on `var` |
| 9 | `Action<IntPtr> a = Foo;` (delegate creation) | AN0100 on `IntPtr` token; AN0102-A on MethodReference (Foo's parameter). Two IDs, two locations — acceptable |
| 10 | `void* p = null;` | AN0100 (void* case), **no** AN0102-B (AN0100 owns the token) |
| 11 | `static extern void F(void* p);` | AN0100 |
| 12 | `struct HWND { public IntPtr Value; }` compiled in a **reference assembly**; consumer writes `HWND w = Get();` | AN0102-B (costume, rule 4) naming field `Value` |
| 13 | `public unsafe struct HWND { }` + `static extern bool F(HWND* h);` | **clean** |
| 14 | `(HWND*)null`, `(HFILE*)(-1)` | **clean** |
| 15 | `MemoryMarshal.Cast<byte, ushort>(span)`, `MemoryMarshal.CreateSpan(ref *p, n)` | **clean** |
| 16 | `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0)` | **clean** |
| 17 | `nint n = 5; n += 2;`, our own `static nint Twice(nint x)`, `Unsafe.Add(ref x, 1)` (int overload) | **clean** |
| 17b | `Unsafe.Add(ref x, (nint)1)`, `Math.Max(n, (nint)2)` | **AN0102-A2** — BCL `nint` is `IntPtr` in .NET 7+ metadata (Definition rule 1); use the `int` overloads |
| 18 | `new Span<byte>(bytePtr, 16)` | AN0102-A2 (void* parameter) |
| 19 | `Buffer.MemoryCopy(src, dst, 16, 16)` with `byte*` args | AN0102-A2 |
| 20 | `NativeMemory.Alloc(16)` | AN0102-A1 (returns void*) |
| 21 | `Marshal.GetLastWin32Error()` | **clean** |
| 22 | `typeof(Foo)` / `proc.Start()` / `Process.GetCurrentProcess()` | **clean** |
| 23 | `var h = typeof(Foo).TypeHandle` | AN0102-A1 (RuntimeTypeHandle is a costume struct via public `Value: IntPtr`, rule 4b) + B on `var` |
| 24 | `var h = proc.Handle` | AN0102-A1 on the property read; no B on `var` (IntPtr-family) |
| 25 | `readonly unsafe struct HFILE_HANDLE { public HFILE* Value; }` + `Dictionary<int, HFILE_HANDLE>` | **clean** |
| 26 | `delegate* unmanaged<HWND*, int, bool> cb` | **clean** |
| 27 | `ProhibitReachableUntypedNativePointers=ignore` with case 1 | no diagnostic |
| 28 | `=disallow` with case 1 | severity Error |
| 29 | Every AN0102 message in cases 1–12, 18–20, 23–24 | contains `Never IntPtr, never void*, never SafeHandle`; handle findings contain `unsafe struct` and `*` |
| 30 | AN0100 void* message | contains `Name the pointee` |

---

## Deliverables

1. `src/ProhibitReachableUntypedNativePointers/ProhibitReachableUntypedNativePointersAnalyzer.cs` (AN0102 sources + sinks + type references, shared definition + per-compilation memo) with the MSBuild property plumbing matching the other analyzers (`build_property.ProhibitReachableUntypedNativePointers`).
2. AN0100 analyzer: implement the already-specified `void*` case (implementation gap, not a spec change) + update messages to the "Error Messages" format in the AN0100 spec.
3. Spec corrections: `An0100_*.md`, `An0101_*.md`, `README-nuget.md` — **re-read each first**; the AN0100 spec was partially corrected on 2026-09-10 (it already says the costume struct is banned and there is no interop-project exemption). Fix only what is still wrong.
4. `docs/TypeSafePInvoke.md` — the canonical idiom page every message links to. The existing file is the 2012 Clearsilver article (HDF*/NEOERR*) and is the right *story*; rewrite it to: lead with the IDIOM block from this file, keep the Clearsilver walk-through as the worked example, add the ConPTY `HPCON**` bug from the AN0100 spec as the second worked example, add the "Practical consequences" section (async/generic wrapper, INVALID_HANDLE_VALUE, allocation, spans, callbacks), and **delete** the closing paragraph that presents `SafeHandle` as an acceptable alternative. Update `_SPECS/80_Article_PInvoke_TypeSafePointers.md` if it shows the costume struct.
5. Tests per the matrix. Test #12 requires the verifier helper to accept an additional pre-compiled reference project; extend the AN0102 helper accordingly.
6. Consumer proof: `AN_Mirica/src/AN.BlastBuffer` and `AN.CodeEditorBlast` build clean under `disallow` (they should already — the `FileStream` rewrite landed 2026-09-10). `AN.PtyConsole/Win32ProcessTreeWatcher.cs` and `AN.PtyConsole.PtyDotNet/Posix.cs` currently emit AN0100 and are the first conversion targets for the idiom — **conversion is a follow-up task**, not part of this one; this task's deliverable is that they emit the expected AN0100/AN0102 diagnostics with idiom-bearing messages.

## Related
- **AN0100 RequireTypedPointersNotIntPtr** — syntax-level: the untyped pointers **we spelled**. AN0102 is the untyped pointers **we did not spell** but that reach our code through someone else's signature.
- **AN0101 RequireUnsafeOnPInvokeImports** — the `unsafe` marker; this task corrects its example.
- **AN0104 ProhibitPlatformImports**, **AN0105 ProhibitNamespaceAccess** — siblings; AN0102 could be expressed partly as AN0105 over `System.Runtime.InteropServices`, but the costume-struct rule and the signature walk are not namespace rules, so it is its own analyzer.