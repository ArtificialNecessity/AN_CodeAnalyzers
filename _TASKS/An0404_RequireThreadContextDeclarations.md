# AN0102 — Thread Context Analyzer

## The Problem

FluidUI has strict threading rules: VT102 mutations happen on the render thread, PTY reads happen on background workers, keyboard events happen on the render thread. These rules are enforced by convention and comments. When they're violated, the result is data races, corrupted state, and crashes that are nearly impossible to reproduce.

Comments don't prevent bugs. Runtime guards catch bugs in testing. Compile-time analysis prevents bugs from existing.

## The Solution: Two Layers

### Layer 1: Runtime Guard Injection (Phase 1 — do this first)

In DEBUG builds, automatically inject `EnsureCorrectThread()` calls at the top of every method that has a `[ThreadContext]` attribute. This catches violations during development with a clear exception and caller info.

### Layer 2: Static Call-Site Analysis (Phase 2 — do this when the runtime guards are proven)

A Roslyn analyzer that checks every call site at compile time. If a method marked `UIRenderThread` is called from a method marked `BackgroundWorker`, emit `AN0102` as a compile error. The bug can't ship.

## The Attribute

```csharp
namespace ArtificialNecessity.CodeAnalyzers;

/// <summary>
/// Declares the required thread context for a class or method.
/// 
/// When applied to a CLASS, all methods in the class default to that context.
/// When applied to a METHOD, it overrides the class default.
/// When applied to an ASSEMBLY, it sets the default for all types in the assembly.
///
/// The analyzer enforces that callers are in a compatible context.
/// In DEBUG builds, a runtime guard is injected at method entry.
/// </summary>
[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
    AttributeTargets.Method | AttributeTargets.Property,
    Inherited = true, AllowMultiple = false)]
public sealed class ThreadContextAttribute : Attribute
{
    public ThreadContext Context { get; }

    public ThreadContextAttribute(ThreadContext context)
    {
        Context = context;
    }
}

/// <summary>
/// Thread context declarations. These represent the logical thread contexts
/// in the application, not specific OS threads.
/// </summary>
public enum ThreadContext
{
    /// <summary>
    /// No thread restriction. Can be called from any thread.
    /// Use sparingly — most code should declare its context.
    /// </summary>
    Any,

    /// <summary>
    /// Must be called on the FluidUI render thread.
    /// This is where Draw(), OnKeyDown(), layout, and all UI mutation happens.
    /// The render thread is the ONLY thread that may mutate FView state,
    /// FlowDocument state, or VT102 terminal model state.
    /// </summary>
    UIRenderThread,

    /// <summary>
    /// Must be called on a background worker thread.
    /// PTY read loops, LLM streaming, file I/O, network requests.
    /// Must NOT touch UI state directly — enqueue via ConcurrentQueue
    /// or ScheduleCallback for render thread processing.
    /// </summary>
    BackgroundWorker,
}
```

## Scope Resolution — Class, Method, Assembly

The attribute cascades from broadest to narrowest scope:

```csharp
// Assembly default — everything in this assembly is UIRenderThread unless overridden
[assembly: ThreadContext(ThreadContext.UIRenderThread)]

// Class override — this whole class is BackgroundWorker
[ThreadContext(ThreadContext.BackgroundWorker)]
public class PtyReadLoop
{
    public void ReadBytes() { ... }  // inherits BackgroundWorker from class

    // Method override — this one method is Any
    [ThreadContext(ThreadContext.Any)]
    public int BytesAvailable => _count;  // safe from any thread
}

// No attribute — inherits assembly default (UIRenderThread)
public class FButton
{
    public void Draw() { ... }  // inherits UIRenderThread from assembly default
}
```

Resolution order:
1. Method attribute (if present) → use it
2. Class/struct attribute (if present) → use it
3. Assembly attribute (if present) → use it
4. No attribute anywhere → treat as `ThreadContext.Any` (no enforcement)

### Namespace-Level Attributes

C# does NOT support namespace-level attributes. Attributes can be applied at the assembly or module level (global attributes) or to specific language elements like classes and methods, but not to namespaces.

**Workaround:** Use assembly-level attributes with a convention. For a multi-project solution, each assembly (project) can declare its own default:

```csharp
// In FluidUI's AssemblyInfo.cs or a ThreadingDefaults.cs:
[assembly: ThreadContext(ThreadContext.UIRenderThread)]

// In AN.PtyConsole's AssemblyInfo.cs:
[assembly: ThreadContext(ThreadContext.Any)]
```

This is actually BETTER than namespace-level — each project is a compilation unit with clear threading expectations. FluidUI is render-thread by default. The PTY library is thread-agnostic by default. Each class can override.

## Phase 1: Runtime Guard Injection

### How It Works

A Roslyn SOURCE GENERATOR (not an analyzer) runs at compile time and emits modified method bodies with guard calls injected. In DEBUG builds only.

**Before (developer writes):**
```csharp
[ThreadContext(ThreadContext.UIRenderThread)]
public void DrainPendingPtyOutputOnRenderThread()
{
    // process VT102 bytes...
}
```

**After (source generator emits in DEBUG):**
```csharp
public void DrainPendingPtyOutputOnRenderThread()
{
    AN.Threading.ThreadContextGuard.Ensure(
        ThreadContext.UIRenderThread,
        // caller info for diagnostic message:
        "ConsoleSessionModel.DrainPendingPtyOutputOnRenderThread",
        "ConsoleSessionModel.cs", 247);

    // process VT102 bytes...
}
```

Wait — source generators can't MODIFY existing method bodies. They can only ADD new types/members. So the runtime guard injection needs a different approach.

### Practical Approach: Analyzer + Manual Guard Pattern

Instead of injecting code, the Roslyn ANALYZER checks that methods with `[ThreadContext]` attributes (other than `Any`) contain a `ThreadContextGuard.Ensure()` call as their first statement. If they don't, emit a warning:

```
AN0103: Method 'DrainPendingPtyOutputOnRenderThread' has [ThreadContext(UIRenderThread)]
        but does not call ThreadContextGuard.Ensure() as its first statement.
        Add: ThreadContextGuard.Ensure(ThreadContext.UIRenderThread);
```

The developer adds the one-liner. The analyzer ensures they don't forget. The guard runs at runtime in DEBUG builds.

### The Runtime Guard

```csharp
public static class ThreadContextGuard
{
    /// <summary>
    /// The thread ID of the FluidUI render thread. Set once during FWorkspace.Run().
    /// </summary>
    public static int RenderThreadId { get; set; }

    [Conditional("DEBUG")]
    public static void Ensure(
        ThreadContext expected,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        int currentThreadId = Environment.CurrentManagedThreadId;

        bool violation = expected switch
        {
            ThreadContext.UIRenderThread => currentThreadId != RenderThreadId,
            ThreadContext.BackgroundWorker => currentThreadId == RenderThreadId,
            ThreadContext.Any => false,
            _ => false,
        };

        if (violation)
        {
            string message = $"[AN0102] THREAD CONTEXT VIOLATION: " +
                $"Method requires {expected} but running on " +
                $"{(currentThreadId == RenderThreadId ? "UIRenderThread" : $"BackgroundWorker (thread {currentThreadId})")}. " +
                $"At {file}:{line} in {member}";

            // In DEBUG: throw hard. This is a bug. Fix it now.
            throw new InvalidOperationException(message);
        }
    }
}
```

### Where RenderThreadId Gets Set

```csharp
// In FWorkspace.Run(), before entering the main loop:
ThreadContextGuard.RenderThreadId = Environment.CurrentManagedThreadId;
```

## Phase 2: Static Call-Site Analysis

### AN0102 — Thread Context Violation (Compile Error)

The analyzer walks every method call in the compilation. For each call site:

1. Determine the CALLER's thread context (resolve from method → class → assembly)
2. Determine the CALLEE's thread context (resolve from method → class → assembly)
3. Check compatibility:

| Caller Context | Callee Context | Result |
|---|---|---|
| UIRenderThread | UIRenderThread | ✅ OK |
| UIRenderThread | Any | ✅ OK |
| UIRenderThread | BackgroundWorker | ❌ AN0102 |
| BackgroundWorker | BackgroundWorker | ✅ OK |
| BackgroundWorker | Any | ✅ OK |
| BackgroundWorker | UIRenderThread | ❌ AN0102 |
| Any | Any | ✅ OK |
| Any | UIRenderThread | ⚠️ AN0104 (warning — can't verify at compile time) |
| Any | BackgroundWorker | ⚠️ AN0104 (warning — can't verify at compile time) |

### AN0103 — Missing Runtime Guard (Warning)

Methods with `[ThreadContext]` (non-Any) should have `ThreadContextGuard.Ensure()` as their first statement. The analyzer checks for this pattern and warns if missing.

### AN0104 — Unverifiable Thread Context (Info)

When a method marked `Any` calls a method with a specific thread context, the analyzer can't verify correctness at compile time. It emits an informational diagnostic suggesting the developer add a runtime guard or reconsider the `Any` marking.

### Cross-Assembly Analysis

The attributes are in the compiled metadata. When FluidUI (assembly default: UIRenderThread) calls AN.PtyConsole (assembly default: Any), the analyzer resolves both sides correctly from the referenced assembly's metadata.

## Diagnostic IDs

| ID | Severity | Description |
|---|---|---|
| AN0102 | Error | Thread context violation — caller and callee have incompatible thread contexts |
| AN0103 | Warning | Missing runtime guard — method has [ThreadContext] but no ThreadContextGuard.Ensure() call |
| AN0104 | Info | Unverifiable thread context — `Any` caller invoking context-specific callee |

## Implementation Priority

### Now
- Define the `ThreadContext` enum and `ThreadContextAttribute` in `AN.CodeAnalyzers`
- Implement `ThreadContextGuard` runtime guard class
- Set `RenderThreadId` in `FWorkspace.Run()`
- Add `[ThreadContext]` and `ThreadContextGuard.Ensure()` to the critical methods identified in the terminal refactor

### Soon
- Implement AN0103 analyzer — warn on missing runtime guards
- Gradually annotate FluidUI classes with `[ThreadContext(UIRenderThread)]`

### Later
- Implement AN0102 analyzer — compile-time call-site analysis
- Add assembly-level defaults to each project
- Implement AN0104 informational diagnostic
- Full coverage across the codebase

## Example: Terminal System After Full Annotation

```csharp
// Assembly default for FluidUI:
[assembly: ThreadContext(ThreadContext.UIRenderThread)]

// ConsoleSessionModel — mostly Any, some specific
[ThreadContext(ThreadContext.Any)]
public class ConsoleSessionModel
{
    public string SessionName { get; }  // Any — safe to read from anywhere
    public bool IsProcessDead { get; }  // Any — volatile flag, safe to read

    [ThreadContext(ThreadContext.UIRenderThread)]
    public void DrainPendingPtyOutputOnRenderThread()
    {
        ThreadContextGuard.Ensure(ThreadContext.UIRenderThread);
        // feed bytes to VT102 — must be on render thread
    }

    [ThreadContext(ThreadContext.BackgroundWorker)]
    private void PtyReadLoopWorker()
    {
        ThreadContextGuard.Ensure(ThreadContext.BackgroundWorker);
        // read from PTY stream — runs on dedicated background thread
    }

    [ThreadContext(ThreadContext.Any)]
    public void WriteToPty(byte[] data)
    {
        // PTY write stream is thread-safe — can write from any thread
    }
}

// FPtyTerminalView — inherits UIRenderThread from assembly default
public class FPtyTerminalView : FView
{
    // All methods default to UIRenderThread — correct, it's a view

    public override void Draw(FDrawContext ctx)
    {
        // Render thread only — inherited from assembly default
        _sessionModel.DrainPendingPtyOutputOnRenderThread();  // ✅ both UIRenderThread
        // render cells...
    }

    // If someone tried to add this, AN0102 would fire:
    // public void BadMethod()
    // {
    //     _sessionModel.PtyReadLoopWorker();  // ❌ AN0102: UIRenderThread calling BackgroundWorker
    // }
}
```

## Why This Matters

Threading bugs are the HARDEST bugs to find. They manifest as:
- Corrupted VT102 state (garbled terminal output)
- Race conditions in FlowDocument layout (the streaming "bop")
- Stale UI after model changes (missed invalidation)
- Crashes that only happen under load

Every one of these has cost hours of debugging time. The thread context analyzer turns them into compile errors or immediate runtime exceptions with exact file:line:method diagnostic info.

The cost is one attribute per class/method that has a threading contract. The benefit is an entire category of bugs that can never ship.