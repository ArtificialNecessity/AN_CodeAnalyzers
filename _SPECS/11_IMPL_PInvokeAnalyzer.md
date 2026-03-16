# AN.CodeAnalyzers.PInvokeVerification

## Purpose

Ensure P/Invoke declarations are documented against their actual native function signatures, making marshalling bugs visible by requiring the ground truth to be stated alongside the managed binding.

## The Problem

P/Invoke bugs are among the hardest to diagnose because:

1. **The native signature is invisible.** The C# declaration is the only thing in the codebase. There's no record of what the native function actually expects. A developer (or AI) must look up the native header to verify correctness, and they usually don't.

2. **Type mismatches compile clean.** `int` vs `IntPtr`, `bool` vs `int`, `string` vs `StringBuilder` — all compile, all potentially wrong, all corrupt data silently at runtime.

3. **Marshalling defaults are implicit and fragile.** The runtime picks default marshallers based on type, and those defaults can vary across .NET versions and platforms.

4. **AI confidently generates wrong signatures.** An AI will produce a P/Invoke binding that looks plausible but has never been verified against the actual native header. It has no way to know `EGLNativeDisplayType` is a pointer, not an int.

5. **IntPtr erases all type information.** Every native pointer type — `EGLDisplay`, `HWND`, `GLXContext`, `Display*` — collapses to `IntPtr`. You can pass an `EGLDisplay` where an `EGLSurface` is expected and the compiler won't blink. The type safety that exists in the native API is thrown away at the managed boundary.

## The Solution

Three complementary mechanisms:

1. **Require a `<native>` doc comment** on every P/Invoke with the actual C function signature
2. **Capture both signatures in a snapshot file** for diffing
3. **Require typed pointer structs** instead of bare `IntPtr` to preserve native type safety

## Analyzer Rules

### AN0010: P/Invoke method must have native signature doc comment

Every method with `[DllImport]` or `[LibraryImport]` must have a `<native>` XML doc comment tag containing the C function signature.

- ID: `AN0010`
- Severity: Error
- Message: `P/Invoke method '{0}' must have a <native> doc comment declaring the native function signature`

**Example — compliant:**
```csharp
/// <summary>Get the EGL display connection.</summary>
/// <native>EGLDisplay eglGetDisplay(EGLNativeDisplayType display_id)</native>
[DllImport("libEGL.so")]
static extern unsafe EGLDisplay* eglGetDisplay(EGLNativeDisplayType* displayId);
```

**Example — violation:**
```csharp
[DllImport("libEGL.so")]
static extern IntPtr eglGetDisplay(IntPtr displayId);  // AN0010: missing <native> tag
```

### AN0011: P/Invoke parameter count mismatch with native signature

If the `<native>` tag is present, the number of parameters in the native signature must match the number of parameters in the managed declaration.

- ID: `AN0011`
- Severity: Error
- Message: `P/Invoke method '{0}' has {1} managed parameters but native signature has {2} parameters`

### AN0012: Pointer-type native parameter mapped to non-pointer managed type

If the `<native>` tag contains a pointer type (ending in `*`, or known pointer typedefs like `HANDLE`, `HWND`, `EGLNativeDisplayType`, `Display*`, `void*`, etc.) and the corresponding managed parameter is `int`, `uint`, `long`, or another non-pointer type — flag it.

- ID: `AN0012`
- Severity: Error
- Message: `Native parameter '{0}' is pointer type '{1}' but managed parameter is '{2}'. Declare a typed pointer struct and use a pointer: 'unsafe struct {1} {{}} ... {1}* {0}'`

**Known pointer typedefs** are configurable via an `.editorconfig` or MSBuild property:

```xml
<PropertyGroup>
  <PInvokePointerTypedefs>HANDLE;HWND;HMODULE;HDC;HGLRC;EGLDisplay;EGLSurface;EGLContext;EGLNativeDisplayType;EGLNativeWindowType;Display;Window;GLXContext</PInvokePointerTypedefs>
</PropertyGroup>
```

### AN0013: Return type mismatch — void native vs non-void managed (or vice versa)

- ID: `AN0013`
- Severity: Error
- Message: `Native function '{0}' returns '{1}' but managed method returns '{2}'`

### AN0014: IntPtr used in P/Invoke — require typed pointer struct

Bare `IntPtr` / `nint` is forbidden in P/Invoke signatures. Every native pointer type must be represented by a typed opaque struct used as an unsafe pointer. No exceptions, no opt-out.

- ID: `AN0014`
- Severity: Error
- Message: `P/Invoke method '{0}' uses IntPtr for parameter '{1}'. IntPtr erases native type safety. Declare a typed pointer struct and use a pointer instead: 'unsafe struct {NativeTypeName} {{}} ... {NativeTypeName}* {1}'`

**Scope:** Controlled by MSBuild property.

```xml
<PropertyGroup>
  <RequireTypedPInvokePointers>true</RequireTypedPInvokePointers>
</PropertyGroup>
```

When `true`, AN0014 fires on any `IntPtr` or `nint` in a `[DllImport]` / `[LibraryImport]` method signature. When absent or `false`, disabled.

**The pattern:**

Instead of:
```csharp
// WRONG — IntPtr erases all type safety
[DllImport("libEGL.so")]
static extern IntPtr eglGetDisplay(IntPtr displayId);
// Can accidentally pass an EGLSurface where EGLDisplay is expected. No error.
```

Require:
```csharp
// Opaque typed pointer structs — one per native handle/pointer type
unsafe struct EGLDisplay {}
unsafe struct EGLNativeDisplayType {}

/// <native>EGLDisplay eglGetDisplay(EGLNativeDisplayType display_id)</native>
[DllImport("libEGL.so")]
static extern unsafe EGLDisplay* eglGetDisplay(EGLNativeDisplayType* displayId);
```

**Why this works:**

- `EGLDisplay*` and `EGLSurface*` are different types. The compiler rejects mixing them.
- The struct names match (or clearly correspond to) the native typedefs.
- The `<native>` doc comment and the managed signature now read almost identically, making verification trivial.
- The empty struct compiles to nothing — zero runtime cost, pure compile-time safety.
- The `unsafe` requirement is appropriate because you're doing pointer interop — it should look dangerous.

**Why no opt-out:** The whole point of this rule is that IntPtr is the bug. Every use of IntPtr in a P/Invoke is an erased type. `void*` in native maps to `void*` in managed — that's still not IntPtr. Callback user-data `void*` is `void*`. There is no legitimate reason to use IntPtr in a P/Invoke signature when typed pointer structs exist.

## PInvoke Snapshot File

### File Format

**Filename:** `PInvoke.snapshot` (committed to source control, next to the .csproj)

**Format:** Flat sorted key-value, one fact per line. Managed signature first (what you're reviewing), `<=` meaning "derived from," native signature as provenance with `@library` prefix.

```
pinvoke.EglBindings.eglGetDisplay: EGLDisplay* eglGetDisplay(EGLNativeDisplayType* displayId) <= @libEGL.so EGLDisplay eglGetDisplay(EGLNativeDisplayType display_id)
pinvoke.EglBindings.eglInitialize: bool eglInitialize(EGLDisplay* display, out int major, out int minor) <= @libEGL.so EGLBoolean eglInitialize(EGLDisplay dpy, EGLint *major, EGLint *minor)
pinvoke.EglBindings.eglTerminate: bool eglTerminate(EGLDisplay* display) <= @libEGL.so EGLBoolean eglTerminate(EGLDisplay dpy)
pinvoke.VulkanNative.vkCreateInstance: int vkCreateInstance(VkInstanceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkInstance* pInstance) <= @libvulkan.so VkResult vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkInstance* pInstance)
```

### What the snapshot catches

- **Managed signature changed** — someone changes a parameter type, the managed side of the line diffs.
- **Native signature corrected** — someone fixes the `<native>` tag to match an updated header, the native side of the line diffs.
- **Library changed** — someone moves a function to a different native library, the `@lib` diffs.
- **New P/Invoke added** — new line appears in the snapshot.
- **P/Invoke removed** — line disappears from the snapshot.

### Snapshot workflow

```xml
<PropertyGroup>
  <GeneratePInvokeSnapshot>true</GeneratePInvokeSnapshot>
</PropertyGroup>
```

- No committed snapshot: generate and succeed.
- Committed snapshot matches: succeed silently.
- Committed snapshot differs: build error listing each changed line.
- Accept changes: `dotnet pinvoke-accept` or delete + rebuild.

## Configuration Summary

```xml
<PropertyGroup>
  <!-- Require <native> doc comment on all P/Invoke methods -->
  <EnforcePInvokeNativeSignature>true</EnforcePInvokeNativeSignature>

  <!-- Forbid bare IntPtr in P/Invoke signatures, require typed pointer structs -->
  <RequireTypedPInvokePointers>true</RequireTypedPInvokePointers>

  <!-- Known native pointer typedefs for AN0012 pointer mismatch detection -->
  <PInvokePointerTypedefs>HANDLE;HWND;HMODULE;HDC;HGLRC;EGLDisplay;EGLSurface;EGLContext;EGLNativeDisplayType;EGLNativeWindowType;Display;Window;GLXContext</PInvokePointerTypedefs>

  <!-- Generate snapshot file -->
  <GeneratePInvokeSnapshot>true</GeneratePInvokeSnapshot>
</PropertyGroup>
```

## Package structure

Lives inside the `AN.CodeAnalyzers` NuGet package:

```
AN.CodeAnalyzers/
├── ...
├── PInvokeVerification/
│   ├── PInvokeNativeSignatureAnalyzer.cs       (AN0010)
│   ├── PInvokeParameterCountAnalyzer.cs        (AN0011)
│   ├── PInvokePointerTypeMismatchAnalyzer.cs   (AN0012)
│   ├── PInvokeReturnTypeMismatchAnalyzer.cs    (AN0013)
│   ├── PInvokeTypedPointerAnalyzer.cs          (AN0014)
│   ├── PInvokeSnapshotGenerator.cs
│   └── PInvokeSnapshotVerifier.cs
└── ...
```

## Non-goals

- Does not auto-generate P/Invoke bindings (use CsWin32, ClangSharp, etc. for that)
- Does not parse native headers — the `<native>` tag is human/AI-authored, not extracted
- Does not validate full marshalling correctness — only structural mismatches
- Does not replace tools like `LibraryImport` source generator — complements them

## Priority

1. **AN0010 (require native tag)** — ship with ExplicitEnums, zero infrastructure needed
2. **AN0011, AN0013 (count and void mismatches)** — simple parsing of the native tag
3. **AN0014 (require typed pointer structs)** — high value, eliminates IntPtr confusion
4. **AN0012 (pointer type mismatch)** — needs configurable typedef list
5. **Snapshot generation** — alongside StableABIVerification snapshot work