using System;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests that shapes, CSG and evaluation survive the trip across the C ABI.
    /// </summary>
    /// <remarks>
    /// The MATH is tested in native/tests/test_core.cpp against the real C++
    /// classes. These tests are about the BOUNDARY: that ownership, struct
    /// passing, error mapping and handle lifetime all behave when a foreign
    /// caller drives them through P/Invoke.
    /// </remarks>
    [TestClass]
    public class ShapeInteropTests {
        private const double Eps = 1e-12;

        // -------------------------------------------------------------------
        // Ownership across the boundary
        // -------------------------------------------------------------------

        [TestMethod]
        public void UnionKeepsOperandsAliveAfterTheyAreDisposed() {
            // THE retain test. If the native union did not retain its operands,
            // disposing inner and outer would free them, and evaluating the union
            // would read freed memory.
            ShapeSafeHandle union;
            using (var inner = Sphere(1.0))
            using (var outer = Sphere(2.0)) {
                union = Union(inner, outer);
            }

            using (union) {
                Assert.AreEqual(-2.0, Eval(union, 0, 0, 0), Eps);
            }
        }

        [TestMethod]
        public void SameShapeCanBeBothOperands() {
            using var a = Sphere(1.0);
            using var u = Union(a, a);
            Assert.AreEqual(-1.0, Eval(u, 0, 0, 0), Eps);
        }

        [TestMethod]
        public void DisposedHandleCannotBePassedToNative() {
            // The marshaller refuses a closed SafeHandle instead of sending a
            // dangling pointer into C++. This is the hazard raw IntPtr had.
            var shape = Sphere(1.0);
            shape.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(
                () => NativeMethods.goop_shape_eval(shape, new Vec3(0, 0, 0), out _));
        }

        // -------------------------------------------------------------------
        // Struct passing
        // -------------------------------------------------------------------

        [TestMethod]
        public void Vec3CrossesByValueWithFieldsInOrder() {
            using var s = Sphere(1.0);

            Assert.AreEqual(1.0, Eval(s, 2, 0, 0), Eps);
            Assert.AreEqual(1.0, Eval(s, 0, 2, 0), Eps);
            Assert.AreEqual(1.0, Eval(s, 0, 0, 2), Eps);

            // Asymmetric in all three components: (3,4,0) is 5 from the origin.
            // A transposed or mis-sized struct would not produce exactly 4.
            Assert.AreEqual(4.0, Eval(s, 3, 4, 0), Eps);
        }

        // -------------------------------------------------------------------
        // Every operator is reachable through the ABI
        // -------------------------------------------------------------------

        [TestMethod]
        public void CsgOperatorsReturnExpectedDistances() {
            // Concentric spheres: at the origin inner = -1, outer = -2.
            using var inner = Sphere(1.0);
            using var outer = Sphere(2.0);

            using (var u = Union(inner, outer)) {
                Assert.AreEqual(-2.0, Eval(u, 0, 0, 0), Eps);
            }
            using (var i = Check(NativeMethods.goop_shape_intersect(inner, outer, out ShapeSafeHandle h1), h1)) {
                Assert.AreEqual(-1.0, Eval(i, 0, 0, 0), Eps);
            }
            using (var shell = Check(NativeMethods.goop_shape_subtract(outer, inner, out ShapeSafeHandle h2), h2)) {
                Assert.AreEqual(1.0, Eval(shell, 0, 0, 0), Eps);    // hollow middle
                Assert.AreEqual(-0.5, Eval(shell, 1.5, 0, 0), Eps); // in the wall
            }
            using (var smooth = Check(NativeMethods.goop_shape_smooth_union(inner, outer, 2.0, out ShapeSafeHandle h3), h3)) {
                Assert.AreEqual(-2.125, Eval(smooth, 0, 0, 0), Eps);
            }
        }

        // -------------------------------------------------------------------
        // Errors
        // -------------------------------------------------------------------

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        [DataRow(double.NaN)]
        public void SmoothUnionRejectsNonPositiveK(double k) {
            using var a = Sphere(1.0);
            using var b = Sphere(2.0);

            int status = NativeMethods.goop_shape_smooth_union(a, b, k, out ShapeSafeHandle result);
            using (result) {
                Assert.IsTrue(result.IsInvalid, "a failed call must hand back no handle");
                var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
                StringAssert.Contains(ex.Message, "k must be positive");
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static ShapeSafeHandle Sphere(double radius) {
            int status = NativeMethods.goop_shape_sphere(radius, out ShapeSafeHandle shape);
            return Check(status, shape);
        }

        private static ShapeSafeHandle Union(ShapeSafeHandle a, ShapeSafeHandle b) {
            int status = NativeMethods.goop_shape_union(a, b, out ShapeSafeHandle shape);
            return Check(status, shape);
        }

        private static double Eval(ShapeSafeHandle shape, double x, double y, double z) {
            Errors.ThrowIfError(NativeMethods.goop_shape_eval(shape, new Vec3(x, y, z), out double distance));
            return distance;
        }

        // Throws on failure, disposing the (invalid) handle first so nothing leaks.
        private static ShapeSafeHandle Check(int status, ShapeSafeHandle shape) {
            if (status != 0) {
                shape.Dispose();
                Errors.ThrowIfError(status);
            }
            return shape;
        }
    }
}
