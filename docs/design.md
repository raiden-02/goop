# Goop design notes

The decisions behind Goop and the reasoning for each. The code comments go into
detail at the point of use. This document is the overview. For background on
distance fields themselves, see [How SDFs work](sdf.md). For build commands, see
[Building and testing](building.md).

## Goals & non-goals

**Goals.** A small, correct signed-distance-field kernel whose native core can be
consumed from .NET with no ambiguity about ownership, errors or lifetimes. The
C boundary is treated as a product surface: narrow, documented, stable, and
tested from the other side.

**Non-goals.** Being a full modelling kernel (no B-rep, no NURBS). Speed at any
cost: the mesher samples a dense grid on purpose, because it is simple and
verifiable. C++/CLI, SWIG, or any generated binding layer: the interop is written
by hand so that every marshalling decision is explicit.

## Layering

```
  consumer app  (e.g. the Gallery sample)
        │
        ▼
  Goop.dll        C#, fluent API        Sdf.Sphere(...).SmoothUnion(...).ToMesh(...)
        │                               Shape · Mesh · Vec3 · StlWriter
        │  P/Invoke                     SafeHandles, blittable structs, pinned buffers
        ▼
  goop.h          C ABI                 extern "C", opaque handles, status codes
        │                               goop_shape · goop_mesh · goop_vec3
        ▼
  goop_native     C++17 core            SDF node graph  →  surface-nets mesher
                                        (shape.cpp)         (mesher.cpp)
```

Each arrow is a boundary with its own rules. The one that matters most is the C
ABI in the middle: everything above it may be C#, everything below it may be
C++, and the line itself is C and nothing but C.

- **The C++ core** (`shape.*`, `mesher.cpp`) uses ordinary C++: classes,
  virtual dispatch, `std::vector`, exceptions. It knows nothing about the ABI.
- **`api.cpp`** is the only file that exports anything. It translates handles
  to objects, turns every exception into a status code, and validates arguments.
- **`goop.h`** contains C types only. It is the contract.
- **`NativeMethods`** mirrors `goop.h` one-for-one and is `internal`.
- **`Shape`, `Sdf`, `Mesh`, `StlWriter`** are the public .NET API: exceptions,
  `IDisposable`, spans, no pointers.

Each layer depends only on the one directly below it. The core is a static
library so that C++ tests can link it directly. The ABI is tested from C#,
because only a foreign caller genuinely exercises calling conventions, layout
and marshalling.

## Shape graph ownership

An expression is a DAG, not a tree: in `a.Union(b)` and `a.Intersect(c)`, both
results share `a`. Copying subtrees on every combine would duplicate work and
memory, so nodes are **intrusively reference counted**:

- A node is created with a count of 1, owned by whoever created it.
- A combinator or transform `retain`s its children in its constructor and
  `release`s them in its destructor (RAII), so its lifetime *is* its ownership.
- Copying a node is forbidden (`= delete` on `Shape`): a copy would duplicate
  child pointers without retaining them, which leads to a double free.
- On the .NET side, **each `ShapeSafeHandle` owns exactly one count.** Handles
  returned by native constructors already carry that count, so nothing in C#
  calls `retain`.

The count is `std::atomic<int>` because `SafeHandle` finalizers run on the
finalizer thread, concurrently with user threads.

A mesh is different: it is a snapshot with a single owner and holds no
reference to its shape.

## Memory ownership of buffers

**The caller allocates, and native code only fills.** Batch evaluation and mesh
copy-out both take a pointer plus a capacity:

1. ask for the count (`goop_mesh_vertex_count`),
2. allocate a managed array of that size,
3. pin it and let native code copy into it.

Native code never returns memory the caller must free. That removes the question
"which allocator owns this?", which is the classic cross-runtime crash. Copy
functions check capacity *before* writing, and fail without touching the buffer.

The one exception is `goop_last_error_message`, which returns a pointer to a
string the DLL owns. The caller copies it immediately and never frees it.

## Error model

- Every fallible export returns `int32_t` status (`goop_status` values) and
  delivers its result through an out-parameter. The return slot is reserved for
  the outcome.
- Out-parameters are cleared *first* (to `NULL`, or to `NaN` for distances), so
  a failed call can never leave a stale handle or a plausible-looking number.
- Inside `api.cpp`, one `guard` template wraps every body in a catch-all that
  maps exceptions to statuses and stores a message in a **thread-local** string.
  Thread-local because two threads failing at once must not overwrite each
  other's message.
- On the .NET side, `Errors.ThrowIfError` reads that message and throws the
  matching standard exception: `ArgumentException`, `OutOfMemoryException`,
  `OperationCanceledException`, or `GoopException` for everything else.
- Public methods also validate in C#, so callers get `ArgumentOutOfRangeException`
  with the correct `ParamName`. The native side validates again, because C# is
  not the only possible caller.

## Callbacks & cancellation

Meshing reports progress through `int32_t (*)(double fraction, void* user_data)`.
Returning nonzero means "stop". It is the simplest cancellation mechanism a C ABI
can express, and it is checked once per grid slice.

A native function pointer to a managed delegate stays valid only while the
delegate is alive, and the GC cannot see native references. Goop therefore uses
**one static delegate for the whole process**, which can never be collected. All
per-call state (the user's lambda, the `CancellationToken`, and any exception)
lives in a `ProgressBridge` object. A normal (not pinned) `GCHandle` carries that
object through `user_data`, and it is freed in `Dispose`.

The callback must never let an exception unwind through native frames. If the
user's lambda throws, the bridge stores the exception, returns "stop", and
`ToMesh` rethrows it once back in managed code, with its original stack trace.

## Threading

- **Evaluation is read-only** and safe to call concurrently on the same shape.
- **Reference counts are atomic**, so handles may be retained and released from
  any thread, including the finalizer thread.
- **The last-error message is per thread.**
- **The progress callback runs synchronously** on the thread that called
  `ToMesh`.
- **Disposing a shape while another thread uses it is not supported.** The
  `SafeHandle` prevents a dangling pointer, but the other call fails.

## ABI rules

Listed in full at the top of `goop.h`. In short:

- C types only.
- No exceptions cross the boundary.
- Fixed-width integers.
- Every symbol is prefixed `goop_`.
- Output buffers are allocated by the caller.
- Callbacks never throw.
- Handles are opaque (incomplete types).
- Struct layout is frozen once shipped.

The native DLL is named **`goop_native.dll`**, not `goop.dll`. On Windows,
filenames are case-insensitive, so a native `goop.dll` and the managed `Goop.dll`
cannot share an output directory: one silently overwrites the other.

## Versioning & compatibility

`GOOP_ABI_VERSION` changes whenever the ABI changes in a way that breaks
existing callers: a changed signature, a reordered struct, a renumbered status.
Adding a new function is not a break. Fields are only ever appended to a
struct, and status values are never renumbered.

`goop_get_version()` returns the version compiled into the DLL. It is the one
export whose signature can never change, so it is safe to call on a DLL of
unknown version.

`Goop.dll` carries its own expected version (`AbiCheck.ExpectedVersion`). Before
the first real native call, which is always an `Sdf` factory because every shape
starts there, it asks the DLL for its version once and throws a `GoopException`
naming both numbers if they differ. Without the check, a mismatch fails in
whatever way the particular change dictates. A renamed export fails loudly, but
a reordered struct or a renumbered status code gives **silently wrong results**.
With it, every mismatch becomes one clear error at first use.

A test ties the two constants together: bumping `GOOP_ABI_VERSION` in `goop.h`
without updating `AbiCheck.ExpectedVersion` fails the build's test step.

## Packaging

One NuGet package contains:

```
lib/net48/Goop.dll   lib/net8.0/Goop.dll   lib/net10.0/Goop.dll
runtimes/win-x64/native/goop_native.dll
build/net48/Goop.targets   buildTransitive/net48/Goop.targets
```

The two runtime families find native libraries differently:

- **.NET 8 and .NET 10** resolve `runtimes/win-x64/native/` through `deps.json`.
- **.NET Framework 4.8** only looks beside the executable, so the package's
  `Goop.targets` copies the DLL there.

`dotnet pack` fails if the native DLL has not been built, rather than producing
a package that throws `DllNotFoundException` in someone else's app.

`Goop.PackageTests` consumes the packed `.nupkg` from a local folder feed, the
way an outside project would. It is not part of `Goop.sln`, because the package
does not exist until `dotnet pack` runs. It uses a private package cache,
because NuGet caches by version and would otherwise keep serving a stale
`0.1.0`.

## Testing strategy

One test per real risk, placed at the layer where that risk lives.

- **C++ (Catch2), against the static core:** distance maths, CSG, smooth union,
  translation, the melted gap, reference counting (observed through a probe
  node that counts live instances), mesh quality, and cancellation.
- **C# (MSTest), through the ABI, on all three frameworks:** DLL loading and the
  ABI version, struct layout, error propagation, buffer bounds, handle lifetimes,
  batch evaluation, STL bytes, and callbacks. The callback tests include forcing
  full garbage collections during a native call, and four concurrent meshes.
- **Packaged:** one end-to-end run through the restored `.nupkg`.

There is no reference mesh to compare against, so mesh tests check properties
any correct closed mesh must have:

- Every index is in range, and no triangle reuses a vertex.
- Every vertex lies within one cell of the true surface.
- **Directed edges:** each appears exactly once and its reverse also appears,
  which catches holes, flipped triangles and non-manifold edges.
- The **signed volume** is positive (outward winding) and close to the analytic
  volume.

## Limitations and future work

- **More primitives and transforms.** Only the sphere, the box and translation
  exist. Torus and cylinder need their exact distance functions, and rotation
  and uniform scale are exact too. Twist is only a distance *bound*, so the mesher
  has to tolerate a field that underestimates.
- **Automatic bounds.** `ToMesh` needs the caller to supply a box that contains
  the shape. The options are a bounding box per node (exact, but every new
  primitive and transform must supply one) or a coarse search of the field (no
  per-node work, but it can miss thin features). Per-node boxes fit the
  existing node graph better.
- **Sharper meshing.** Averaging edge crossings rounds off sharp features, so a
  CSG edge comes out bevelled. Dual contouring's per-cell least-squares solve
  (the "QEF"), using surface normals, would put vertices on the edge.
- **Sparse sampling.** Every grid sample is evaluated, including the deep
  interior and far exterior. A narrow band or an octree around the surface would
  make high resolutions affordable.
- **Recursive teardown.** Releasing a very deeply nested expression recurses
  once per level and could exhaust the stack. An explicit worklist instead of
  recursion would remove the limit.
- **More platforms.** Only Windows x64 is supported. The native payload would move into
  a separate `Goop.runtime.<rid>` package per platform, which is the pattern
  larger native libraries use.
