using System;
using System.Runtime;
using System.Runtime.InteropServices;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests
{
    /// <summary>
    /// Placeholder suite proving the test projects build and run on both target
    /// frameworks. Real tests replace this as the milestones land.
    /// </summary>
    [TestClass]
    public class ScaffoldTests
    {
        [TestMethod]
        public void ScaffoldBuilds()
        {
            var scaffoldIsWiredUp = true;
            Assert.IsTrue(scaffoldIsWiredUp);
        }

        // TODO (M1): the first real test, and the most valuable one in the file -
        //       call goop_get_version through P/Invoke and assert it equals
        //       GOOP_ABI_VERSION. If that passes, the DLL was found, the calling
        //       convention is right, and the copy target works. If it throws
        //       DllNotFoundException, the copy target is broken. If it returns
        //       garbage or corrupts the stack, the calling convention is wrong.
        //       Three different failure modes, one test.

        // TODO (M1): assert Environment.Is64BitProcess. On net48 a bitness
        //       mismatch shows up as BadImageFormatException from the loader,
        //       which is a confusing way to learn that PlatformTarget was not
        //       applied. Asserting it directly turns that into a readable
        //       failure.

        // TODO (M1): Marshal.SizeOf<Vec3>() == 24, and a round-trip of a known
        //       point through goop_shape_eval_batch. Layout bugs are much easier
        //       to diagnose from a size assertion than from wrong geometry.

        // TODO (M2): reference counting. Build a shared subexpression, dispose
        //       one operand, then keep using the combined shape. Also assert that
        //       using a disposed Shape throws ObjectDisposedException rather than
        //       crashing the process.

        // TODO (M3): batch evaluation against analytically known distances -
        //       sphere centre, surface, and a point far outside.

        // TODO (M4): mesh a sphere and run it through MeshOracle. Then round-trip
        //       an STL: write it, parse it back, compare triangle counts.

        // TODO (M5): force each error path and assert the mapped exception type
        //       AND that the message from goop_last_error_message actually
        //       arrives intact.

        // TODO (M6): the delegate-lifetime test described in
        //       Internal/ProgressCallback.cs - force a GC from inside the
        //       progress callback during a long mesh. Plus: cancellation raises
        //       OperationCanceledException, and progress fractions are monotonic
        //       and end at 1.0.

        [TestMethod]
        public void NativeAbiVersionMatchesHeader()
        {
            Assert.AreEqual(1, NativeMethods.goop_get_version());
        }

        [TestMethod]
        public void Vec3LayoutMatchesNative()
        {
            Assert.AreEqual(24, Marshal.SizeOf<Vec3>());          // managed view
            Assert.AreEqual(24, NativeMethods.goop_vec3_size());  // native view

            Assert.AreEqual(0,  Marshal.OffsetOf<Vec3>(nameof(Vec3.X)).ToInt32());
            Assert.AreEqual(8,  Marshal.OffsetOf<Vec3>(nameof(Vec3.Y)).ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf<Vec3>(nameof(Vec3.Z)).ToInt32());
        }

        [TestMethod]
        public void SphereCanBeCreatedAndReleased()
        {
            int status = NativeMethods.goop_shape_sphere(1.0, out IntPtr shape);
            Assert.AreEqual(0, status);
            Assert.AreNotEqual(IntPtr.Zero, shape);
            NativeMethods.goop_shape_release(shape);
        }

        [TestMethod]
        public void NegativeRadiusIsRejected()
        {
            int status = NativeMethods.goop_shape_sphere(-1.0, out IntPtr shape);
            Assert.AreNotEqual(0, status);
            Assert.AreEqual(IntPtr.Zero, shape);
        }
    }
}
