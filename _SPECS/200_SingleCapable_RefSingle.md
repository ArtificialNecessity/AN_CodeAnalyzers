# SPEC-RefSingle: `ref single` Pointers and `[SingleCapable]` Structs

- **Status:** Draft (v2 — supersedes SPEC-SingleRegions; the region abstraction is eliminated)
- **References:** See "External Dependencies & References". Companion to the C#/Rust memory-safety position paper.

- [ ] Milestone 1 — Three-rule core + habitat model formalized (this document)
- [ ] Milestone 2 — **Phase 0: Roslyn-only unique-ownership prototype** (ordinary heap objects, analyzer-enforced move semantics + deterministic teardown; zero runtime changes, zero unsafe)
- [ ] Milestone 3 — Segregated bump allocator + root lifecycle library (`ref single` over `unsafe`, analyzer-proven)
- [ ] Milestone 4 — JIT noalias specialization on `ref single` provenance
- [ ] Milestone 5 — Verifier rules promoted into the runtime (CLR proposal)

## Overview

This spec adds one pointer kind and one struct attribute to a CLR-shaped runtime, yielding `&mut`-grade noalias optimization, untraced allocation, and O(1) bulk reclamation for linear-shaped data — with defined aliasing and tracing GC untouched for everything else, and **zero aliasing contracts visible in any function signature**.

The foundation: static uniqueness/linearity is provable exactly over data whose reference structure is a tree of unique paths, and only within a closed world where all producers and consumers of a pointer are visible. The CLR already has both required boundaries: **structs** (memory the GC does not trace or individually track) and **assemblies** (the platform's closed-world compilation unit, with a verifier standing at the gate). This design is two new checks for that existing guard. There are no regions, no epochs, no barriers, no promotion machinery — every runtime mechanism from earlier drafts existed to police at runtime an invariant that assembly confinement makes checkable at build time.

## Architecture / Design

### The Three Rules

**Rule 1 — `[SingleCapable]` declares linear-eligible struct types.**
A struct marked `[SingleCapable]` (mirroring existing constraint-style attributes) may contain only:
- value-type fields (primitives and pure-value structs),
- fields of other `[SingleCapable]` struct types,
- `ref single` pointer fields (Rule 2),
- weak references to traced-heap objects.

It may **not** contain strong object references. Checked per-field at type declaration; transitive eligibility follows by the same induction the verifier already runs for `ref struct` field containment (allow-list swapped). No use-site analysis exists or is needed.

**Rule 2 — `ref single T` pointers.**
A new pointer kind requiring `T : [SingleCapable]`. Unlike `ref` fields today (legal only in `ref struct`, hence stack-lifetime), `ref single` fields are legal **inside `[SingleCapable]` structs** — structs may point at each other. Assignment requires same-provenance `ref single` sources (locally checkable, same flavor as the existing ref-safe-to-escape rules). Soundness of heap-stored interior pointers — the reason general ref-fields-in-structs are forbidden today — is supplied by Rules 1+3: nothing traced can point in, nothing strong points out, and all code that can touch the pointer is in the closed world.

**Rule 3 — Assembly confinement.**
`ref single` may not appear in any exported position: public/exported signatures, fields of exported types, boxed forms, generic instantiations visible across the manifest. Only *values* (copies) cross assembly boundaries. Enforced where cross-assembly type visibility is already checked. This is the entire "hermeticity" story — closed-world by construction, no runtime boundary to police.

### The Habitat Table

The design completes a type dichotomy the CLR already began:

| | `ref struct` | ordinary struct / class | `[SingleCapable]` struct |
|---|---|---|---|
| May contain | stack `ref`s | heap refs | `ref single`s, weakrefs, values |
| May live | stack only | traced heap | untraced segments |
| May not | reach the heap | — | contain the heap (strongly) |
| Escape boundary | frame | none | assembly |

Stack-world, graph-world, linear-world: three habitats, each with a type-level passport, each checked locally at declaration plus one boundary (frame / none / assembly manifest).

### Everything Else Is Derived, Not Specified

- **Untraced placement.** `[SingleCapable]` struct clusters are allocated in segregated bump-pointer segments. The GC skips them not by a rule but vacuously: the type system proved no traced pointer can enter (Rule 3 + no boxing) and no strong pointer can leave (Rule 1). To the collector, a segment is a `byte[]`.
- **Roots and reclamation.** A cluster is reachable from whatever root object the owning assembly's code established — an ordinary small heap object owning the segments (the *only* GC-visible piece). Root unreachable → segments freed as a unit. A million-node structure deallocates by one root death; collection cost over the structure's lifetime is tracking one handle. Cache eviction = drop one strong ref = O(1) bulk free.
- **Borrows.** Interior access uses existing `ref` / `ref struct` machinery unchanged. Soundness composes from shipped invariants: borrow ≤ frame (existing escape rules) and frame ≤ cluster lifetime (a stack-held strong root is GC-reachable for the frame's extent). **Corollary to enforce:** interior borrows are minted only through a stack-held strong root — accessors take the root by value/`in`, making the frame a keep-alive. The same local doubles as the JIT's provenance signal.
- **Noalias.** References derived from a single stack-held root into `[SingleCapable]` clusters provably have no aliases outside the current call tree. The JIT emits noalias-specialized code guarded by typing facts, not runtime checks. Failure mode is a missed optimization, never a miscompilation. Struct clusters with no identity, no sync blocks, and no covariance are the best possible input for existing field-sensitive JIT analyses.
- **Cross-assembly sharing.** Always by copy (Rule 3). Inside the assembly, the closed world lets the compiler check borrow discipline directly; no epochs or runtime view-validation machinery is required in the core. (Epoch-checked weak views remain available as an *assembly-internal library pattern* for heap-duration observation; they are not spec machinery.)
- **No promotion.** Cluster interiors never migrate to the traced heap in place; sharing is always by copy at the (rare) architectural moment data becomes shared. Deletes write barriers, evacuation, and deopt protocols.
- **No relocation.** Segments grow by chaining and never move, so `ref single` is a raw address. Bump allocation over lifetime-homogeneous data does not fragment within a lifetime; compaction is unnecessary by construction. (If relocation were ever wanted, interior refs become base+offset — noted and rejected for v0.)

### Signature Invisibility

`ref single` and `[SingleCapable]` never appear in public API signatures (Rule 3 makes this structural, not stylistic). Functions take ordinary references and values; callees neither know nor care about provenance. A library written once can be instantiated in either implementation flavor — linear internals (noalias, untraced, copy-on-reveal) or ordinary heap internals (zero-copy interior access, traced) — selected at the instantiation site like value-type generic specialization, both satisfying the same interfaces. Consumers carry **no obligations** in either flavor. Aliasing requirements cannot be externalized onto callers because the vocabulary to do so does not exist.

```
// pseudocode — illustrative only
[SingleCapable]
struct BvhNode {
    Aabb Bounds;                    // pure-value struct: universally usable
    ref single BvhNode Left;        // Rule 2: struct-to-struct pointer
    ref single BvhNode Right;
    weak<SceneEntity> Entity;       // Rule 1: outbound edges are weak only
}

class Bvh {                         // ordinary class; the GC-visible root
    // owns the segments; internal ref single machinery invisible here
    public HitResult Query(Ray r) { // ordinary signature — Rule 3
        ref BvhNode n = RootNode(); // borrow via stack-held root (this)
        ...traverse...              // JIT noalias-specializes this path
        return hit;                 // value out; nothing reveals an interior
    }
}
```

### Eligibility of Existing Value Types (strict v0)

Pure-value structs (`Point`, `Vector3`, all-value fields) are admitted **structurally** into `[SingleCapable]` containment without declaration — they are habitat-agnostic today between stack and heap, and forking the ecosystem's value types is unacceptable. The explicit `[SingleCapable]` attribute is required only for structs using the new powers (`ref single` fields or weakref fields), which cannot live in the old world anyway. **Strict v0:** a declared `[SingleCapable]` struct is untraced-habitat-only (may not be a field of ordinary heap classes), even when its fields would be heap-harmless (the weakref-field case). The permissive relaxation (declaration = eligibility, not obligation) is noted as a possible v1 loosening.

## Implementation Strategy

The design is expressible today as **analyzer + library**, with the runtime change deferred to a final promotion step. Phase 0 requires no unsafe code and no allocator — it enforces the full linearity *semantics* over ordinary heap objects, deferring only the performance halves (untraced placement, JIT noalias).

### Phase 0: Roslyn-Only Unique-Ownership Prototype

`[SingleCapable]` types are ordinary classes/structs on the traced heap. The analyzer enforces **affine unique ownership** — `unique_ptr`-shaped semantics — via four local rules (AN02xx series, AN.CodeAnalyzers style):

1. **Field allow-list** (unchanged from the core design): fields of a `[SingleCapable]` type are values, unique references to other `[SingleCapable]` types, or weakrefs. No strong references to ordinary heap objects.
2. **Move-only assignment with free-on-overwrite.** A unique field is assignable only from a fresh allocation or a *move* (the source location is provably relinquished — nulled — at the assignment site, analyzer-checked). Overwriting a non-null unique field emits teardown of the old subtree first. This is sound with zero liveness analysis: uniqueness means the overwritten reference was the *only* strong path, so the old subtree became unreachable-by-construction at the instant of overwrite. **Evaluation order rule:** the new value's construction is fully evaluated before teardown of the old value fires, so a throwing constructor leaves the old subtree intact (exception safety).
3. **Noescape.** A unique reference never flows to a storable location outside its owning structure: no returns except as ownership transfer (source relinquished), no capture into closures or async state machines, no storage into ordinary heap objects. Passing *down* as a non-stored parameter is the borrow; "callee does not store it" is checkable because Rule 3 of the core design (assembly confinement) makes the world closed.
4. **Deterministic teardown.** `[SingleCapable]` roots are `IDisposable`-shaped; dispose recurses the unique tree (O(subtree), deterministic, GC-independent). Overwrite invokes it implicitly per rule 2. Teardown paths may not perform virtual dispatch (analyzer-checked), foreclosing reentrancy into the tree being dismantled. Teardown may return nodes to per-type pools instead of freeing, recovering most of the allocation win before the real allocator exists.

**What Phase 0 preserves:** the complete linearity semantics — unique paths, tree shape by construction (uniqueness makes cycles unrepresentable), weak-only outbound edges, deterministic lifetime — and every analyzer invariant that later becomes a verifier rule. **What it defers:** objects remain GC-traced (the collector redundantly walks trees the analyzer already proved tree-shaped), and the JIT receives no noalias facts (it cannot see analyzer conclusions). Those are exactly the pieces the later phases supply. **Promotion compatibility:** upgrading Phase 0 code to `ref single` segments changes no user code — the analyzer was already enforcing the stricter discipline, so swapping unique-class fields for segment-allocated `ref single` fields and teardown for segment-free is a codegen substitution, not a semantic change. Phase 0 also yields the key measurement for the runtime proposal: how much of the win is semantic discipline + pooling versus untraced placement + noalias.

### Later Phases

1. **Unsafe-backed `ref single` (analyzer-proven).** `ref single` as a discipline over `unsafe` pointers inside one assembly: same Phase 0 rules, plus borrows only via stack-held root accessors and assignment provenance. The analyzers *prove the unsafe never lies*. There is no runtime lifecycle to fake — that is the payoff of eliminating regions.
2. **Allocator library.** Bump-pointer segment chains owned by an ordinary root object; `Dispose`/unreachability frees segments (teardown from Phase 0 becomes O(1) segment-free). POH/`MemoryManager<T>`-adjacent machinery that exists today.
3. **JIT provenance work.** Noalias specialization keyed to stack-held-root provenance — an added dimension to existing guarded-specialization machinery (cf. guarded devirtualization, PGO cloning).
4. **Verifier promotion.** The analyzer invariants become verifier rules: `ref single` as a real pointer kind, `[SingleCapable]` field induction alongside the `ref struct` induction, manifest-boundary check. At this point `unsafe` disappears from the pattern entirely.

## External Dependencies & References

- [C# ref struct / ref safety rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct) — the existing habitat-restriction construction this design mirrors, and the borrow checker it reuses verbatim.
- [Low-level struct improvements (C# 11: ref fields, scoped)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-11.0/low-level-struct-improvements) — ref-safe-to-escape formalization; `ref single` is the second legal habitat for ref fields.
- [Span&lt;T&gt; / MemoryManager&lt;T&gt;](https://learn.microsoft.com/en-us/dotnet/api/system.span-1) — interior-pointer views and custom-memory ownership machinery.
- [Pinned Object Heap](https://devblogs.microsoft.com/dotnet/internals-of-the-poh/) — precedent for segregated, non-standard-lifecycle heap areas in the CLR.
- [LLVM noalias](https://llvm.org/docs/LangRef.html#noalias) — the optimization contract granted privately by JIT specialization.
- [MLKit region inference](https://elsman.com/mlkit/) — untraced regions via inference; foundered on inference as user-visible contract. Fixed here by explicit declaration + invisible optimization.
- [Koka / Perceus](https://www.microsoft.com/en-us/research/publication/perceus-garbage-free-reference-counting-with-reuse/) — ownership inference with runtime fallback; adjacent prior art.
- [Project Verona](https://github.com/microsoft/verona) — region-based ownership research with user-visible regions; contrast with signature invisibility here.
- [Rust references & borrowing](https://doc.rust-lang.org/book/ch04-02-references-and-borrowing.html) — the universal-exclusivity design this model scopes to declared-linear data; a sound and valuable tradeoff for no-GC environments, universalized beyond its theorem's jurisdiction.
- [.NET GC fundamentals / generational hypothesis](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals) — the empirical bet behind born-linear data dying linear or copying out early.
- [C++ unique_ptr](https://en.cppreference.com/w/cpp/memory/unique_ptr) — the affine-ownership, free-on-overwrite, move-only semantics Phase 0 enforces via analyzer rather than via a wrapper type.
- [Roslyn analyzers](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/) — the enforcement substrate for Phase 0 and the analyzer-proven unsafe phase.

## Open Questions

- Generics: can `[SingleCapable]` structs instantiate existing generic types (`FixedList<T>`-style), and what constraint syntax expresses `T : SingleCapable`? Interaction with generic sharing (likely requires unshared instantiation like value types generally).
- Collections vocabulary: idiomatic patterns for arrays/lists *of* `[SingleCapable]` structs inside a cluster (inline arrays? segment-slice types?).
- Nested roots: a `[SingleCapable]` cluster field inside an ordinary heap class other than "the" root — forbidden in strict v0; is "sugar for a nested root object" the right v1?
- `await`/suspension: borrows already cannot cross await per ref-struct rules; confirm copy-out (or the internal weak-view pattern) as the blessed shape for async traversals.
- Serialization/interop: blitting `[SingleCapable]` clusters (they are pointer-bearing, so raw blit is wrong; offset-normalized serialization as a library concern?).
- Diagnostics: debugger visualization of `ref single` graphs in untraced segments (no object identity for the debugger to hang on).

## Prior Art & Comparative Critique

The `[SingleCapable]` / `ref single` design sits in a thirty-year lineage of
ownership-encapsulation systems. Each prior system got one piece right and paid
for it somewhere this design refuses to. This section places the design in that
lineage and states, for each ancestor, what it proved and where it breaks.

The summary judgment: every prior system either (a) sealed the ownership
boundary so tightly that real structures — back-pointers, caches, observers —
became inexpressible, (b) loosened the interior so far that "uniquely owned"
described only the entry point while the interior degenerated into an
untraceable object soup, or (c) proved linearity in the type system but never
told the allocator, forfeiting the performance the proof paid for. The
`ref single` design threads this by combining a closed interior
(single-or-weakref-only field allow-list) with a GC-visible root (the traced
head is the escape hatch to the shared world) — the interior stays provably
tree-shaped, and the aliasing/observer patterns that killed the sealed designs
are outsourced to weakrefs and to the ordinary traced heap, which already exist.

### Balloon Types (Almeida, ECOOP 1997) and Islands (Hogg, 1991)

The ancestral proposals for a hermetically sealed ownership subtree: a
balloon's transitive state is reachable only through the balloon, with no
external pointers in and no internal aliases out.

**What it proved:** full encapsulation is statically checkable, and a sealed
subtree is a sound unit of reasoning.

**Criticism (why not adopted):** hermetic ownership with no non-owning
reference form is unprogrammable. Real structures need back-pointers, caches,
and observer references, and full encapsulation forbids all of them. Every
attempt to build on the pure form hit this wall — the same wall Rust hits when
persistent back-references force `Rc<RefCell<>>`, index-arenas, or `unsafe`.
The lesson taken here: the sealed interior is correct, but it must be paired
with a first-class *non-owning* reference (the weakref leg of the
`[SingleCapable]` allow-list). Weakrefs are the pressure valve that makes the
seal livable.

### External Uniqueness (Clarke & Wrigstad, 2003)

The opposite loosening: the unique pointer is the only *external* reference,
but the interior may alias itself freely.

**Criticism (why not adopted):** once the unique boundary subsumes enough to
contain the back-pointers, the interior is a general mutating object graph, and
a general graph cannot be freed piecewise without tracing. The result is an
allocator with no free: a private heap of changing objects and references in
which nothing can be reclaimed safely — strictly worse than the real heap,
which at least has a collector. "Uniquely owned" describes the door, not the
room. The lesson taken here: uniqueness must be *transitive* through the
strong-reference structure (the field allow-list induction), or it buys
nothing. Interior aliasing goes through weakrefs, which are reclamation-inert
by construction.

### Pony — Deny Capabilities and ORCA (Clebsch & Drossopoulou, 2015; ORCA 2017)

The most complete shipped capability system: `iso` (globally read/write-unique)
with `consume` transfer, the deny-matrix formalization, viewpoint adaptation,
and a per-actor GC with no stop-the-world, co-designed with the type system
(no barriers or traditional safepoints, because capabilities statically exclude
unprotected concurrent access).

**What it proved:** a GC'd managed language can carry *stronger* static
concurrency guarantees than Rust — direct evidence that managed runtimes and
real safety are not in tension. The deny formalization and
definition-side generic capability checking both transfer to open-world
settings and inform this design.

**Criticism (why not adopted):** Pony proves linearity in the type system and
then throws the proof away at runtime — `iso` leaves no runtime footprint, so
the allocator and GC never learn what the type checker knew. Three
consequences:

1. **Tracing despite proof.** An acyclic, uniquely-owned `iso` tree is still
   discovered-dead by mark-phase tracing on the owning actor's heap. Data that
   the type system proves eligible for deterministic O(1)-per-node reclamation
   pays the tracing model anyway.
2. **O(n) "zero-copy" transfer.** Consuming an `iso` graph across an actor
   boundary traces the entire reachable graph at send (`gc_send`) and again at
   receive (`gc_recv`) for ORCA foreign-reference accounting. The type system
   proves the transfer is one pointer of semantic content; the runtime walks
   the graph twice. Ownership-of-object transfers; ownership-of-memory never
   does — objects are collected forever by the allocating actor, so a producer
   actor carries the GC burden for everything it ships away.
3. **Impure interiors.** `iso` trees may contain `val` (shared immutable)
   references, so the ownership boundary and the reachability boundary do not
   coincide; even in principle the tree is not a closed reclamation unit.

The lesson taken here, stated as the design's central bet: **the payoff of
linearity requires the allocator in on the conspiracy.** `[SingleCapable]`
interiors exclude strong traced references entirely (closing consequence 3),
which makes deterministic teardown sound (closing 1) and makes transfer a pure
pointer handoff with no accounting — the unmanaged heap belongs to no thread,
so nothing moves and nothing is re-registered (closing 2).

Secondary criticism (adoption-structural, informing strategy rather than
design): Pony bundled three paradigm shifts — actors, capabilities, and its own
runtime — with no incremental adoption path (whole-program AOT, no dynamic
loading, structural interfaces against a closed world). Its ideas are winning
(Verona is downstream) while the language did not. This design deliberately
inverts every one of those choices: stock CLR, ordinary assemblies, open-world
loading, analyzer-first phasing, and adoption one attribute at a time.

### Vale — Generational References (Ovadia)

The closest shipped realization of the interior discipline: every object has
exactly one owning reference (the heap is a forest of ownership trees,
deterministic destruction, no tracing GC), and all non-owning references are
generational — (pointer, generation) pairs validated in O(1) at dereference
against a per-allocation generation counter. No registration lists, no
control blocks, no tombstone tables.

**What it proved:** iso-only spines with weakref-only cross-links are a
livable programming model, and generational references are the cheap weakref —
the fix for `weak_ptr`'s control-block cost, independently rediscovered by
every game-engine slotmap/handle system.

**Criticism (why not adopted wholesale):** Vale makes the tree discipline the
*entire* memory model — there is no GC, so every piece of graph-shaped,
share-shaped, or lifetime-ambiguous data must be forced into the tree+weakref
mold or hand-managed. That universalizes the same tax Rust universalizes, just
with a friendlier weakref. The costs of the universal version: every
non-owning dereference pays a generation check; generation slots constrain
returning memory to the OS; and every cross-link in the program is fallible at
the type level, forever. The lesson taken here: the tree discipline is an
*opt-in island*, not the world. Graph-shaped data stays on the traced heap
where a real collector handles it; `[SingleCapable]` is declared only where
linear shape is natural and the reclamation/noalias dividend is wanted.
Generational validation is retained, but priced only at the cross-boundary
weak dereference, not at every non-owning access.

### Midori M# — Permissions and the Exchange Heap (Gordon et al., OOPSLA 2012; Duffy)

`isolated`/`immutable`/`readable`/`writable` permissions with an exchange heap
for transferring isolated subgraphs between processes, bulk-freed —
the same OOPSLA 2012 research thread Pony's capabilities descend from, built
into a production OS.

**What it proved:** the isolated-transferable-subgraph model works at OS
scale, and permissions + message passing can carry an entire system.

**Criticism (why not adopted as-is):** Midori required a bespoke runtime,
language, and OS — the whole vertical. The observation animating this spec is
that the Midori dividend for linear data is now reachable on *stock* CLR:
structs (memory the GC does not trace) plus assemblies (a verified closed
world) plus Roslyn analyzers (compiler-grade enforcement without forking the
compiler) supply the substrate Midori had to build. The design goal is the
Midori/Pony payoff with a four-step ladder whose first two steps require zero
runtime changes.

### Comparative Summary

| System | Interior discipline | Non-owning refs | Runtime informed of linearity? | Reclamation of proven-linear data | Transfer cost | Incremental adoption |
|---|---|---|---|---|---|---|
| Balloon/Islands | Sealed, no aliases | None (the flaw) | n/a (never shipped at scale) | n/a | n/a | n/a |
| External uniqueness | Free interior aliasing | Interior strong aliases | No | Tracing required (interior is a graph) | n/a | n/a |
| Pony `iso` | Unique spine + shared `val` leaves | `tag`, `val` | No — `iso` has no runtime footprint | Traced at next actor GC | O(n) double trace (ORCA send/recv) | None (whole-program, bundled paradigms) |
| Vale | Unique everywhere (whole language) | Generational refs | Yes — allocator is the model | Deterministic, universal | Move of owning ref | None (new language, no GC escape) |
| Midori M# | `isolated` subgraphs | `readable` et al. | Yes — exchange heap | Bulk free on exchange heap | Cheap (exchange heap) | None (bespoke runtime + OS) |
| **`ref single` (this spec)** | **`[SingleCapable]` allow-list: singles + weakrefs + values only** | **Weakrefs (generational)** | **Yes — untraced habitat; GC frontier crossed only at traced roots** | **Deterministic teardown (Phase 0); segment free (Phase 1+)** | **O(1) — topological ownership over a shared unmanaged heap; no per-object accounting** | **Attribute + analyzer on stock CLR; per-type opt-in** |

### Additions to External Dependencies & References

- [Balloon Types (Almeida, ECOOP 1997)](https://link.springer.com/chapter/10.1007/BFb0053373) — ancestral sealed-subtree proposal; demonstrates that hermetic encapsulation without a non-owning reference form is unprogrammable.
- Islands (Hogg, OOPSLA 1991) — earlier sibling of balloon types, same lesson. (ACM DL: "Islands: aliasing protection in object-oriented languages.")
- External Uniqueness (Clarke & Wrigstad, ECOOP 2003, "External Uniqueness Is Unique Enough") — the loosened-interior variant; demonstrates that non-transitive uniqueness degenerates into an untraceable private heap.
- [Deny Capabilities for Safe, Fast Actors (Clebsch & Drossopoulou, AGERE 2015)](https://www.ponylang.io/media/papers/fast-cheap.pdf) — Pony's capability formalization; the deny-matrix framing and definition-side generic capability checking inform this design.
- [ORCA: GC and Type System Co-Design for Actor Languages (Clebsch et al., OOPSLA 2017)](https://www.ponylang.io/media/papers/orca_gc_and_type_system_co-design_for_actor_languages.pdf) — documents the send/receive tracing cost and allocator-never-informed gap this design closes.
- [Vale — Generational References (Ovadia)](https://verdagon.dev/blog/generational-references) — O(1)-validated non-owning references without control blocks; the weakref mechanism adopted here for cross-boundary weak dereference.
- [Uniqueness and Reference Immutability for Safe Parallelism (Gordon et al., OOPSLA 2012)](https://www.microsoft.com/en-us/research/publication/uniqueness-and-reference-immutability-for-safe-parallelism/) — the Midori M# permission foundation; common ancestor of Pony's capabilities.
- [Joe Duffy — Blogging about Midori](http://joeduffyblog.com/2015/11/03/blogging-about-midori/) — exchange heap and isolated-transfer machinery at OS scale; the dividend this spec targets on stock CLR.
- [Project Verona](https://github.com/microsoft/verona) — region-based successor to the Pony lineage; the O(1)-region-transfer goal it pursues via allocation-time regions, this spec pursues via topological ownership with no region reification.

## Alternatives Considered

- **Region reification (SPEC-SingleRegions v1).** Regions as runtime identities with lifecycles, epochs, weak-view validation, and copy-on-reveal machinery. Superseded: every runtime mechanism policed an invariant that assembly confinement checks at build time. Preserved insights (root-as-heap-citizen, borrow-soundness composition, weak-only outbound) survive as derived properties.
- **Universal exclusivity contracts (Rust `&mut`).** Sound for tree-shaped data and no-GC environments; as universal reference semantics it imposes proof obligations at every call boundary on graph-shaped programs (bifurcated APIs, defensive copies, runtime flags, unsafe assertions). Scoped here to declared-linear types only.
- **Compiler-inferred linearity.** Rejected: inference makes allocation behavior an unpredictable emergent property under refactoring. Explicit shape, implicit reward.
- **Sanctioned promotion (in-place migration to traced heap).** Rejected for copy-only sharing; promotion requires barriers, evacuation, and deopt of in-flight noalias assumptions.
- **Mutability polymorphism (dual-emitting aliased/unaliased function bodies).** Only sound where aliased execution is defined; subsumed by flavor selection at instantiation sites, where provenance is known.
- **Persistent immutable structures (Clojure-style).** Dissolves aliasing semantically; requires tracing GC for structural sharing and allocation per update. Complementary — usable unchanged on the traced side of this model.
- **Whole-heap dynamic borrow states (RefCell-everywhere).** A managed runtime with worse constants; dominated by GC + defined aliasing.