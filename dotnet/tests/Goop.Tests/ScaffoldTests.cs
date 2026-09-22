using System;
using System.Runtime.InteropServices;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Placeholder suite proving the test projects build and run on both target
    /// frameworks. Real tests replace this as the milestones land.
    /// </summary>
    [TestClass]
    public class ScaffoldTests {
        [TestMethod]
        public void ScaffoldBuilds() {
            var scaffoldIsWiredUp = true;
            Assert.IsTrue(scaffoldIsWiredUp);
        }

        // TODO (M1): assert Environment.Is64BitProcess. On net48 a bitness
        //       mismatch shows up as BadImageFormatException from the loader,
        //       which is a confusing way to learn that PlatformTarget was not
        //       applied. Asserting it directly turns that into a readable
        //       failure.

        // TODO (M4): mesh a sphere and run it through MeshOracle. Then round-trip
        //       an STL: write it, parse it back, compare triangle counts.

        // TODO (M5): the remaining error paths. Invalid argument is covered by
        //       NegativeRadiusThrowsWithMessage. Still open: null handle, out of
        //       memory, cancelled, and internal, each with its mapped exception
        //       and the message from goop_last_error_message.

        // TODO (M6): the delegate-lifetime test described in
        //       Internal/ProgressCallback.cs - force a GC from inside the
        //       progress callback during a long mesh. Plus: cancellation raises
        //       OperationCanceledException, and progress fractions are monotonic
        //       and end at 1.0.

        [TestMethod]
        public void NativeAbiVersionMatchesHeader() {
            Assert.AreEqual(1, NativeMethods.goop_get_version());
        }

        [TestMethod]
        public void Vec3LayoutMatchesNative() {
            Assert.AreEqual(24, Marshal.SizeOf<Vec3>());          // managed view
            Assert.AreEqual(24, NativeMethods.goop_vec3_size());  // native view

            Assert.AreEqual(0, Marshal.OffsetOf<Vec3>(nameof(Vec3.X)).ToInt32());
            Assert.AreEqual(8, Marshal.OffsetOf<Vec3>(nameof(Vec3.Y)).ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf<Vec3>(nameof(Vec3.Z)).ToInt32());
        }

        [TestMethod]
        public void SphereHandleReleasesOnDispose() {
            var shape = CreateSphere(1.0);
            Assert.IsFalse(shape.IsInvalid);
            Assert.IsFalse(shape.IsClosed);

            shape.Dispose();
            Assert.IsTrue(shape.IsClosed);

            shape.Dispose(); // second Dispose must be a harmless no-op, not a double free
        }

        [TestMethod]
        public void NegativeRadiusGivesInvalidHandle() {
            int status = NativeMethods.goop_shape_sphere(-1.0, out ShapeSafeHandle shape);
            Assert.AreNotEqual(0, status);
            Assert.IsTrue(shape.IsInvalid); // native cleared *out_shape to NULL

            shape.Dispose(); // safe: an invalid handle is never passed to ReleaseHandle
        }

        [TestMethod]
        public void NegativeRadiusThrowsWithMessage() {
            int status = NativeMethods.goop_shape_sphere(-1.0, out ShapeSafeHandle shape);
            using (shape) {
                var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
                StringAssert.Contains(ex.Message, "radius");
            }
        }

        private static ShapeSafeHandle CreateSphere(double radius) {
            Errors.ThrowIfError(NativeMethods.goop_shape_sphere(radius, out ShapeSafeHandle shape));
            return shape;
        }
    }
}
