# AN.SaferAssemblyLoader

## Summary

A standalone library that loads .NET assemblies with a guarantee: if it loaded, it's managed-only. No P/Invoke, no `IntPtr`, no `Marshal` calls, no `unsafe` IL. If any of these are present, it throws before the assembly enters your AppDomain.

One class. One method. It loads or it throws.

```csharp
Assembly asm = AssemblyManagedOnly.LoadFrom("plugin.dll");
```

---

## Why

.NET has no built-in mechanism to say "load this assembly only if it's 100% managed code." The CLR will happily load an assembly that calls `kernel32.dll` through `[DllImport]`, stores wild pointers in `IntPtr` fields, and does arbitrary memory manipulation through `Marshal.*` — and none of this requires the `unsafe` keyword or triggers any load-time check.

The metadata to detect all of this already exists in the PE file. Nobody wired up a gate. This library is that gate.

---

## Package

|                        |                                                         |
| ---------------------- | ------------------------------------------------------- |
| **Package**      | `ArtificialNecessity.SaferAssemblyLoader`             |
| **Target**       | netstandard2.0                                          |
| **Dependencies** | `System.Reflection.Metadata` 7.0.0                   |
| **Size**         | One file. Tiny.                                         |

This is a standalone library. It does NOT depend on `AN.CodeAnalyzers`. It does NOT depend on Roslyn. You reference it in your runtime project and call it. That's it.

---

## API

```csharp
namespace ArtificialNecessity.SaferAssemblyLoader;

/// <summary>
/// Loads .NET assemblies with a managed-only guarantee.
/// Inspects the assembly metadata WITHOUT loading it into the runtime.
/// If the assembly contains any unmanaged code surface, throws before loading.
/// If it loads, it's clean.
/// </summary>
public static class AssemblyManagedOnly
{
    /// <summary>
    /// Load an assembly from disk. Inspects PE metadata first.
    /// If the assembly is managed-only, loads and returns it.
    /// If not, throws ManagedOnlyViolationException listing every violation.
    /// The assembly is NEVER loaded if it fails inspection.
    /// </summary>
    public static Assembly LoadFrom(string assemblyPath);

    /// <summary>
    /// Load an assembly from a byte array. Same guarantees.
    /// </summary>
    public static Assembly Load(byte[] rawAssembly);

    /// <summary>
    /// Inspect without loading. Returns true if managed-only.
    /// If you need the violation list without loading, use this.
    /// </summary>
    public static bool IsManagedOnly(string assemblyPath);

    /// <summary>
    /// Inspect without loading. Returns the violation list.
    /// Empty list = managed-only.
    /// </summary>
    public static IReadOnlyList<string> GetViolations(string assemblyPath);
}
```

---

## Exception

```csharp
namespace ArtificialNecessity.SaferAssemblyLoader;

public class ManagedOnlyViolationException : Exception
{
    /// <summary>Every violation found, one string per violation.</summary>
    public IReadOnlyList<string> Violations { get; }

    // Message format:
    // "Assembly 'plugin.dll' is not managed-only (7 violations):
    //   [DllImport] NativeMethods.CreateFile() → kernel32.dll
    //   [DllImport] NativeMethods.CloseHandle() → kernel32.dll
    //   [IntPtr field] Foo._handle
    //   [Marshal call] Foo.Init() → Marshal.AllocHGlobal
    //   [unsafe IL] Bar.Process()
    //   ..."
}
```

---

## What It Checks

The inspection uses `System.Reflection.Metadata.MetadataReader` to scan the PE file without loading the assembly. It flags:

| Violation                              | How detected                                                                                  |
| -------------------------------------- | --------------------------------------------------------------------------------------------- |
| `[DllImport]` methods                | MethodDef with `PInvokeImpl` flag or `DllImport` custom attribute                         |
| `[LibraryImport]` methods            | `LibraryImport` custom attribute (source-generated P/Invoke)                                |
| `IntPtr` / `UIntPtr` in signatures | TypeRef/TypeSpec scanning in method signatures, field signatures                              |
| `IntPtr` / `UIntPtr` fields        | FieldDef type signature scanning                                                              |
| `Marshal.*` calls                    | MemberRef scanning for `System.Runtime.InteropServices.Marshal` methods                     |
| `unsafe` method bodies               | MethodDef with `HasSecurity` or IL body containing `localloc`, pointer arithmetic opcodes |
| Native method bodies                   | MethodDef with `MethodImplAttributes.Native` or `MethodImplAttributes.Unmanaged`          |
| Mixed-mode assembly                    | PE header `ILOnly` flag not set                                                             |
| `fixed` statements                   | IL pattern:`conv.u` / `localloc` patterns in method bodies                                |

---

## Usage

### Plugin loading

```csharp
// Load a plugin — if it touches native code, it doesn't load
try
{
    Assembly plugin = AssemblyManagedOnly.LoadFrom(pluginPath);
    // safe to use
}
catch (ManagedOnlyViolationException ex)
{
    logger.Error($"Rejected plugin: {ex.Message}");
    // the assembly was NEVER loaded — your process is clean
}
```

### Build-time verification

```csharp
// In a test or build step — verify your own assemblies
var violations = AssemblyManagedOnly.GetViolations("MyApp.dll");
Assert.Empty(violations);  // enforces managed-only policy on your own code
```

### Quick check without loading

```csharp
if (!AssemblyManagedOnly.IsManagedOnly(dllPath))
{
    Console.WriteLine("Nope.");
    return;
}
```

---

Implementastion strategy.. something like...

```csharp


sealed static class SaferAssemblyLoader {

Assembly LoadFrom(string assemblyPath) {

    AssemblySafetyReport report = ManagedAssemblyInspector.Inspect(assemblyPath);

    if (!report.IsManagedOnly)
    {
        // report.Violations is a list:
        //   "DllImport: kernel32.dll!CreateFile in MyPlugin.NativeMethods.CreateFile()"
        //   "IntPtr field: MyPlugin.Foo._handle"
        //   "Marshal call: Marshal.AllocHGlobal in MyPlugin.Foo.Init()"
        throw new PluginLoadException($"Assembly is not managed-only: {report.Violations.Count} violations");
    }

    // Safe to load — we know it doesn't touch native code
    return Assembly.LoadFrom(assemblyPath);
}


```

---

## What This Is NOT

This is not a sandbox. A managed-only assembly can still do harmful things — file I/O, network calls, reflection, `Assembly.Load` of other assemblies. This gate answers exactly one question: **does this assembly touch native code?** If you need full sandboxing, use process isolation or WASM.

This is not a runtime monitor. It checks once at load time. If the assembly dynamically emits IL that calls native code (via `Reflection.Emit`), this won't catch it. That's a different problem.

This is the gate that should have been a flag on `Assembly.LoadFrom` twenty-three years ago.

---

## Relationship to AN.CodeAnalyzers

`AN.CodeAnalyzers` (AN0100, AN0101) enforces managed-only discipline at **build time** on source code you control.

`ArtificialNecessity.SaferAssemblyLoader` enforces managed-only discipline at **load time** on compiled assemblies you may not control.

They're complementary. Use both. But they ship separately — `ArtificialNecessity.SaferAssemblyLoader` has no dependency on the analyzer package, Roslyn, or anything else heavy. It's one small DLL that reads PE metadata and makes a decision.


## Future WOrk

- Restricted Sandboxes (control what Assemlies the assembly is allowed to reference when loading)