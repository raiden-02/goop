# Goop

*Sculpt shapes that melt into each other, then print them.*

**Status: pre-alpha.** The build, test and packaging infrastructure is in place
across both languages; the kernel itself is under active development.

```csharp
using var shape = Sdf.Sphere(1.0).SmoothUnion(Sdf.Box(0.8, 1.4, 0.8), 0.3);
using var mesh  = shape.ToMesh(resolution: 128, progress: p => Console.Write($"\r{p:P0}"));
mesh.SaveStl("blob.stl");
```

## What is an SDF?

A **signed distance field** is a function that takes a point in space and returns
the distance to the nearest surface of a shape — *negative* inside the shape,
*zero* exactly on the surface, *positive* outside. A unit sphere at the origin is
just `length(p) - 1`. That is the whole representation: no vertices, no faces,
just a function you can ask about any point.

The reason this is worth doing is that shapes composed this way combine
trivially. The union of two shapes is the `min` of their two distances; the
intersection is the `max`; subtracting one from the other is `max(a, -b)`. Solid
modelling that would be fiddly with triangle meshes becomes three lines of
arithmetic.

And because those combinations are just arithmetic, they can be *softened*. A
plain `min` leaves a sharp crease where two surfaces meet. Replace it with a
smooth interpolation between the two distances and the surfaces bulge into one
another instead — the blend radius `k` controls how far the influence reaches. At
`k = 0` you get an ordinary union; turn it up and the shapes melt together like
two drops of water touching. That effect is what this project is named after, and
it is the reason to build an SDF kernel rather than a mesh library.

The distance functions and smooth-blend formulae used here come from **Inigo
Quilez's** public articles on distance functions, which are the standard
reference for this material: <https://iquilezles.org/articles/distfunctions/>
(see also his article on smooth minimum functions) — the background for anything
in `native/src/shape.cpp`.

## Prerequisites

| Requirement | Notes |
|---|---|
| **Visual Studio 2022 or later** | With the **Desktop development with C++** workload. The generator is detected by CMake, not hardcoded, so any recent version works. |
| **CMake 3.20+** | Either installed standalone and on `PATH`, or the **C++ CMake tools for Windows** component inside Visual Studio — `scripts/build.ps1` finds either. |
| **.NET SDK 8.0 or newer** | Builds both target frameworks. A newer SDK (9 or 10) is fine and will still build `net8.0`. |
| **.NET Framework 4.8 Developer Pack** | Required to *build* the `net48` target — the runtime alone is not enough, MSBuild needs the reference assemblies. <https://dotnet.microsoft.com/download/dotnet-framework/net48> |
| **Windows, x64** | The only supported configuration. `goop.dll` is 64-bit and every managed project is pinned to x64 so it can load. |

## Build

One command does everything — configure, build, both test suites, both target
frameworks:

```powershell
.\scripts\build.ps1
```

```powershell
.\scripts\build.ps1 -Configuration Release
.\scripts\build.ps1 -SkipTests
```

The same thing by hand, if you want to see the pieces:

```powershell
# native: configure, build, test
cmake -S . -B build -A x64
cmake --build build --config Debug
ctest --test-dir build --build-config Debug --output-on-failure

# managed: build and test both net48 and net8.0
dotnet build dotnet\Goop.sln --configuration Debug
dotnet test  dotnet\Goop.sln --configuration Debug
```

`goop.dll` lands at `build/bin/<Config>/goop.dll`. The test and sample projects
copy it next to their own output automatically — the OS loader resolves
`goop.dll` from the running executable's directory, and CMake's output is outside
the MSBuild graph, so the copy has to be explicit.

Run the sample:

```powershell
dotnet run --project dotnet\samples\Goop.Gallery --framework net8.0
```

## Architecture

```
  Gallery / consumer app
           |
           v
  Goop  (C#, fluent API)          Sdf.Sphere(...).SmoothUnion(...).ToMesh(...)
           |                      Shape / Mesh / Vec3 / StlWriter
           |  P/Invoke            SafeHandles, blittable structs, pinned buffers
           v
  goop.h  (C ABI)                 extern "C", opaque handles, status codes
           |                      goop_shape / goop_mesh / goop_vec3
           v
  C++17 core                      SDF node graph  ->  surface-nets mesher
                                  (shape.cpp)         (mesher.cpp)
```

Each arrow is a boundary with its own rules. The one that matters most is the
third: everything above it may be C#, everything below it may be C++, and the
line itself is C and nothing but C. `native/include/goop/goop.h` spells out those
rules, and `native/src/api.cpp` is the only file allowed to cross them.

The static/shared split in `native/CMakeLists.txt` follows from that: the C++
core is a static library so the Catch2 tests can link it and use real C++ types,
while the C ABI is tested from C# — because the only honest test of an ABI is a
foreign caller.

## Roadmap

- [ ] **M0** — Both sides build. `goop.dll` compiles, `ctest` runs, `dotnet build` succeeds for `net48` and `net8.0`.
- [ ] **M1** — C# creates a sphere handle and evaluates a single distance on both `net48` and `net8.0`, establishing the P/Invoke path and the x64 loading contract.
- [ ] **M2** — Node graph with reference-counted ownership (`goop_shape_retain` / `goop_shape_release`, `ShapeSafeHandle`) and the CSG operators on top of it.
- [ ] **M3** — Batch evaluation into caller-allocated buffers. Points in, distances out, no per-point transition cost.
- [ ] **M4** — Surface-nets mesher, the copy-out APIs, and STL export. First `blob.stl`.
- [ ] **M5** — Error model: `goop_status` codes plus thread-local last-error message, mapped back into real .NET exceptions.
- [ ] **M6** — Progress callback and cancellation, including getting delegate lifetime right so a GC mid-mesh does not crash the process.
- [ ] **M7** — Real test suites both sides: MSTest and Catch2, including the mesh oracle (watertight, manifold, no degenerate triangles).
- [ ] **M8** — `dotnet pack`, and `Goop.PackageTests` restoring the packed `.nupkg` from a local feed to prove an outside consumer can actually use it.
- [ ] **M9** — ABI compatibility testing: verify that a mismatch between `goop.dll` and `Goop.dll` is detected via `GOOP_ABI_VERSION` at load time rather than surfacing later as a crash.
- [ ] **Stretch** — Gyroid and twist shapes; a turntable render for the gallery.

## Gallery

*(Renders land here as the mesher comes online. Generated STL files go in
`gallery/output/`, which is git-ignored.)*
