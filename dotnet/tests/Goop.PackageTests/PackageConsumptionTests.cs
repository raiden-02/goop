// ===========================================================================
// PackageConsumptionTests - the "does this actually ship" suite.
//
// WHY THIS PROJECT EXISTS, AND WHY IT DOES NOT USE A ProjectReference
//
// Goop.Tests references the Goop project directly. That is right for unit
// tests, but it means MSBuild resolves everything from the build graph: it knows
// where the assembly is, it copies transitive outputs automatically, and a
// sibling target drops goop.dll next to the binaries. None of that happens for
// someone who types `dotnet add package Goop`.
//
// So a ProjectReference cannot catch any of the failures that only appear in a
// packed package:
//
//   * the native DLL was never included in the .nupkg at all, or landed at a
//     path (runtimes/win-x64/native/) that the consumer's SDK does not resolve
//     for their target framework - the classic net48-consumer-gets-nothing case;
//   * the package has no build/Goop.targets, so nothing copies goop.dll to the
//     consumer's output directory and every call throws DllNotFoundException;
//   * the lib/ folders are wrong, so the package restores but nothing resolves;
//   * a dependency was left out of the nuspec and only worked locally because
//     the project graph happened to provide it;
//   * the package restores for net8.0 but not for net48, or vice versa.
//
// Every one of those bugs is invisible until a real consumer restores a real
// package. This project IS that consumer: it restores Goop from a local folder
// feed containing the freshly packed .nupkg, exactly as if it came from
// nuget.org, and then runs the same basic smoke test.
//
// The wiring lands in milestone M8 (`dotnet pack` -> artifacts/packages ->
// enable the source in nuget.config -> PackageReference). Until then this is a
// placeholder that only proves the project builds and runs on both frameworks.
// ===========================================================================

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.PackageTests
{
    [TestClass]
    public class PackageConsumptionTests
    {
        [TestMethod]
        public void ScaffoldBuilds()
        {
            var scaffoldIsWiredUp = true;
            Assert.IsTrue(scaffoldIsWiredUp);
        }

        // TODO (M8): the whole point, once the package exists:
        //
        //   * restore Goop from the local feed and assert the assembly loads;
        //   * call the simplest native function (goop_get_version) through the
        //     public API and assert it succeeds - proving goop.dll came along
        //     inside the package and got copied to this project's output;
        //   * do the above on BOTH net48 and net8.0, because the RID-specific
        //     runtimes/ folder is resolved very differently by the two;
        //   * build the README's headline example end to end and write an STL,
        //     as a consumer-level smoke test;
        //   * assert the package version matches what was packed, so a stale
        //     .nupkg sitting in the feed cannot quietly satisfy the restore.
    }
}
