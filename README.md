# Goop

*Sculpt shapes that melt into each other, then print them.*

A small [signed-distance-field](docs/sdf.md) kernel: a C++17 core behind a stable
C ABI, driven from C# through a fluent API on .NET Framework 4.8, .NET 8 and
.NET 10. Shapes are combined with CSG and smooth blends, meshed with surface nets,
and exported as STL.

```csharp
using var blob = Sdf.Sphere(1.0)
                    .SmoothUnion(Sdf.Sphere(0.8).Translate(2.0, 0, 0), 1.2);

using var mesh = blob.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(3.2, 1.5, 1.5),
                             resolution: 128,
                             progress: p => Console.Write($"\r{p:P0}"));

mesh.SaveStl("blob.stl");
```

![Two spheres combined with a plain union, then melted together with a smooth union](gallery/blob.png)

**Status:** experimental. The whole pipeline works end to end on Windows x64, but
the shape vocabulary is still small. See
[limitations](docs/design.md#limitations-and-future-work).

## Features

- **Modelling:** a sphere, combined with union, intersection, subtraction and smooth union, and moved with translation.
- **Evaluation:** a single point, or a whole batch of points in one native call.
- **Meshing:** surface nets with progress reporting and cancellation. The output is watertight and wound consistently.
- **Export:** binary STL.
- **Interop:** a narrow, documented C ABI with reference-counted handles, `SafeHandle` ownership, exception-safe callbacks, and an ABI version check on first use.
- **Packaging:** one NuGet package that carries the native DLL for all three target frameworks, verified by a project that consumes the packed `.nupkg`.

## Quick start

On Windows x64, with Visual Studio 2022+ (C++ workload), the .NET 10 SDK and the
.NET Framework 4.8 Developer Pack installed:

```powershell
.\scripts\build.ps1                                     # build, test, pack
dotnet run --project dotnet\samples\Goop.Gallery        # writes STL files to gallery\output
```

## Documentation

- **[How SDFs work](docs/sdf.md):** distance fields, CSG as `min`/`max`, the smooth-union blend, and meshing.
- **[Building and testing](docs/building.md):** prerequisites, the build steps, and debugging across the C# / C++ boundary.
- **[Design notes](docs/design.md):** architecture, the ABI rules, ownership, errors, callbacks, threading, packaging, testing, and limitations.

## License

[MIT](LICENSE).
