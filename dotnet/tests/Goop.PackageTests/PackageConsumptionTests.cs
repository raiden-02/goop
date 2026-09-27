// ===========================================================================
// PackageConsumptionTests - the "does this actually ship" suite.
//
// WHY THIS PROJECT EXISTS, AND WHY IT DOES NOT USE A ProjectReference
//
// Goop.Tests references the Goop project directly. That is right for unit
// tests, but it means MSBuild resolves everything from the build graph: it knows
// where the assembly is, it copies transitive outputs automatically, and a
// sibling target drops goop_native.dll next to the binaries. None of that
// happens for someone who types `dotnet add package Goop`.
//
// So a ProjectReference cannot catch any of the failures that only appear in a
// packed package:
//
//   * the native DLL was never included in the .nupkg at all, or landed at a
//     path (runtimes/win-x64/native/) that the consumer's SDK does not resolve
//     for their target framework - the classic net48-consumer-gets-nothing case;
//   * the package has no build/net48/Goop.targets, so nothing copies the DLL to
//     a .NET Framework consumer's output and every call throws
//     DllNotFoundException;
//   * the lib/ folders are wrong, so the package restores but nothing resolves;
//   * a dependency was left out of the nuspec and only worked locally because
//     the project graph happened to provide it;
//   * the package restores for net8.0 but not for net48, or vice versa.
//
// Every one of those bugs is invisible until a real consumer restores a real
// package. This project IS that consumer: it restores Goop from the local
// folder feed (artifacts/packages) exactly as if it came from nuget.org, on all
// three target frameworks, and runs the whole pipeline once.
// ===========================================================================

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.PackageTests {
    [TestClass]
    public class PackageConsumptionTests {

        [TestMethod]
        public void TheRestoredPackageRunsEndToEnd() {
            // Every stage crosses into goop_native.dll, which reached this
            // process ONLY through the package - so if the DLL is missing from
            // the .nupkg, or not delivered for this target framework, the very
            // first line throws DllNotFoundException.
            using var blob = Sdf.Sphere(1.0).SmoothUnion(Sdf.Sphere(0.8).Translate(1.5, 0, 0), 1.0);
            using var mesh = blob.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(2.8, 1.5, 1.5), 32);
            using var stl = new MemoryStream();
            mesh.SaveStl(stl);

            Assert.IsTrue(mesh.TriangleCount > 0, "the package produced an empty mesh");
            Assert.AreEqual(84 + 50L * mesh.TriangleCount, stl.Length, "STL size does not match its triangle count");
        }
    }
}
