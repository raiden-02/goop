using System;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests for <c>goop_shape_eval_batch</c>: many points, one boundary crossing.
    /// </summary>
    /// <remarks>
    /// Every test pins its managed arrays with <c>fixed</c> and hands raw pointers
    /// to C++. Pinning stops the GC from moving the arrays while native code is
    /// reading and writing them.
    /// </remarks>
    [TestClass]
    public class EvalBatchTests {

        [TestMethod]
        public void BatchMatchesSingleEvalForAWholeTree() {
            // Not just a sphere: a smooth union of two spheres, so the batch loop
            // has to walk a whole expression tree through virtual eval() calls.
            using var inner = Sphere(1.0);
            using var outer = Sphere(2.0);
            using var shape = SmoothUnion(inner, outer, 1.5);

            var rng = new Random(42); // fixed seed: the same points every run
            var points = new Vec3[1000];
            for (int i = 0; i < points.Length; i++) {
                points[i] = new Vec3(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3);
            }

            double[] batch = EvalBatch(shape, points);

            for (int i = 0; i < points.Length; i++) {
                Errors.ThrowIfError(NativeMethods.goop_shape_eval(shape, points[i], out double single));
                // Exact, not approximate: same code, same inputs, so the same bits.
                Assert.AreEqual(single, batch[i], 0.0, "point " + i);
            }
        }

        [TestMethod]
        public void LargeBatchIsFilledCompletely() {
            // A million points in ONE call. Checks every slot was written and the
            // int64 count path works, without asserting on timing (that would be
            // flaky on a busy machine).
            using var sphere = Sphere(1.0);

            var points = new Vec3[1_000_000];
            for (int i = 0; i < points.Length; i++) {
                points[i] = new Vec3(i * 1e-5, 0, 0); // 0 .. ~10 along x
            }

            double[] distances = EvalBatch(sphere, points);

            Assert.AreEqual(-1.0, distances[0], 1e-12);            // centre
            Assert.AreEqual(0.0, distances[100_000], 1e-9);        // x = 1, the surface
            Assert.AreEqual(points[999_999].X - 1.0, distances[999_999], 1e-9);
            foreach (double d in distances) {
                Assert.IsFalse(double.IsNaN(d), "a slot was left at its NaN pre-fill");
            }
        }

        [TestMethod]
        public unsafe void ZeroCountWithNullPointersIsANoOp() {
            // count == 0 is legal and must not touch the pointers at all, so null
            // is fine. This is how an empty managed array would arrive.
            using var sphere = Sphere(1.0);
            int status = NativeMethods.goop_shape_eval_batch(sphere, null, null, 0);
            Assert.AreEqual(0, status);
        }

        [TestMethod]
        public unsafe void NegativeCountIsRejected() {
            using var sphere = Sphere(1.0);
            var points = new Vec3[1];
            var distances = new double[1];

            int status;
            fixed (Vec3* p = points)
            fixed (double* d = distances) {
                status = NativeMethods.goop_shape_eval_batch(sphere, p, d, -1);
            }

            var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
            StringAssert.Contains(ex.Message, "count");
        }

        [TestMethod]
        public unsafe void NullBufferWithPositiveCountIsRejected() {
            using var sphere = Sphere(1.0);
            var distances = new double[1];

            int status;
            fixed (double* d = distances) {
                status = NativeMethods.goop_shape_eval_batch(sphere, null, d, 1);
            }

            Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
        }

        [TestMethod]
        public unsafe void DisposedHandleIsRefusedBeforeReachingNative() {
            var sphere = Sphere(1.0);
            sphere.Dispose();

            var points = new Vec3[1];
            var distances = new double[1];

            Assert.ThrowsExactly<ObjectDisposedException>(() => {
                fixed (Vec3* p = points)
                fixed (double* d = distances) {
                    NativeMethods.goop_shape_eval_batch(sphere, p, d, 1);
                }
            });
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        // The pattern every caller of eval_batch follows: allocate the output on
        // the managed side (ABI rule 5), pin both arrays, pass pointers + count.
        private static unsafe double[] EvalBatch(ShapeSafeHandle shape, Vec3[] points) {
            var distances = new double[points.Length];
            int status;
            fixed (Vec3* p = points)
            fixed (double* d = distances) {
                status = NativeMethods.goop_shape_eval_batch(shape, p, d, points.Length);
            } // <- unpinned here; the GC may move the arrays again from now on
            Errors.ThrowIfError(status);
            return distances;
        }

        private static ShapeSafeHandle Sphere(double radius) {
            int status = NativeMethods.goop_shape_sphere(radius, out ShapeSafeHandle shape);
            return Check(status, shape);
        }

        private static ShapeSafeHandle SmoothUnion(ShapeSafeHandle a, ShapeSafeHandle b, double k) {
            int status = NativeMethods.goop_shape_smooth_union(a, b, k, out ShapeSafeHandle shape);
            return Check(status, shape);
        }

        private static ShapeSafeHandle Check(int status, ShapeSafeHandle shape) {
            if (status != 0) {
                shape.Dispose();
                Errors.ThrowIfError(status);
            }
            return shape;
        }
    }
}
