using System;
using System.Runtime.InteropServices;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// ABI guarantees the public API cannot reach on its own, tested through the
    /// raw P/Invoke layer. Everything else is tested through Shape / Mesh.
    /// </summary>
    [TestClass]
    public class InteropTests {

        [TestMethod]
        public void DllLoadsAndAbiVersionMatchesHeader() {
            // One call proves: the DLL was found, the export resolved (no name
            // mangling), the calling convention matches, and the process is x64.
            // It also ties the managed constant to the native one: bump
            // GOOP_ABI_VERSION in goop.h without AbiCheck.ExpectedVersion and
            // this fails.
            Assert.AreEqual(AbiCheck.ExpectedVersion, NativeMethods.goop_get_version());
        }

        [TestMethod]
        public void MismatchedAbiVersionIsRefusedWithAClearMessage() {
            // A real mismatch needs a DLL from another build; the comparison is
            // what matters, so it is tested directly.
            var ex = Assert.ThrowsExactly<GoopException>(() => AbiCheck.Verify(AbiCheck.ExpectedVersion + 1));
            StringAssert.Contains(ex.Message, "ABI version " + (AbiCheck.ExpectedVersion + 1));
            StringAssert.Contains(ex.Message, "requires version " + AbiCheck.ExpectedVersion);
        }

        [TestMethod]
        public void Vec3LayoutMatchesNative() {
            // Size alone would not catch transposed fields; offsets do.
            Assert.AreEqual(NativeMethods.goop_vec3_size(), Marshal.SizeOf<Vec3>());
            Assert.AreEqual(0, Marshal.OffsetOf<Vec3>(nameof(Vec3.X)).ToInt32());
            Assert.AreEqual(8, Marshal.OffsetOf<Vec3>(nameof(Vec3.Y)).ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf<Vec3>(nameof(Vec3.Z)).ToInt32());
        }

        [TestMethod]
        public void NativeErrorBecomesAnExceptionWithTheNativeMessage() {
            // Bypasses the C# validation in Sdf.Sphere to exercise the native path:
            // throw in C++ -> guard -> status + thread-local message -> ThrowIfError.
            int status = NativeMethods.goop_shape_sphere(-1.0, out ShapeSafeHandle shape);
            using (shape) {
                Assert.IsTrue(shape.IsInvalid, "a failed call must hand back no handle");
                var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
                StringAssert.Contains(ex.Message, "radius");
            }
        }

        [TestMethod]
        public unsafe void CopyIntoTooSmallBufferIsRejectedWithoutWriting() {
            // ABI rule 5: native must never write past a caller's buffer.
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(new Vec3(-1.5, -1.5, -1.5), new Vec3(1.5, 1.5, 1.5), 16);
            var marker = new Vec3(-7, -7, -7);
            var tooSmall = new Vec3[mesh.VertexCount - 1];
            for (int i = 0; i < tooSmall.Length; i++) {
                tooSmall[i] = marker;
            }

            int status;
            fixed (Vec3* p = tooSmall) {
                status = NativeMethods.goop_mesh_copy_vertices(mesh.Handle, p, tooSmall.Length);
            }

            Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
            foreach (Vec3 v in tooSmall) {
                Assert.AreEqual(marker.X, v.X, "native wrote into a buffer it had rejected");
            }
        }
    }
}
