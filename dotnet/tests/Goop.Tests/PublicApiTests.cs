using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests the API a user actually sees: <see cref="Sdf"/> and <see cref="Shape"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here touches NativeMethods, handles or pointers. If a user could not
    /// write it, it does not belong in this file.
    /// </remarks>
    [TestClass]
    public class PublicApiTests {
        private const double Eps = 1e-12;
        private static readonly Vec3 Origin = new Vec3(0, 0, 0);

        // -------------------------------------------------------------------
        // The fluent API works end to end
        // -------------------------------------------------------------------

        [TestMethod]
        public void FluentChainEvaluates() {
            // Exactly how the README says to use it. The two inner spheres are
            // temporaries nobody disposes; the union keeps them alive.
            using var shape = Sdf.Sphere(1.0).Union(Sdf.Sphere(2.0));
            Assert.AreEqual(-2.0, shape.Evaluate(Origin), Eps);
        }

        [TestMethod]
        public void EveryCombinatorGivesTheExpectedDistance() {
            using var inner = Sdf.Sphere(1.0);
            using var outer = Sdf.Sphere(2.0);

            using var union = inner.Union(outer);
            using var intersect = inner.Intersect(outer);
            using var shell = outer.Subtract(inner);
            using var smooth = inner.SmoothUnion(outer, 2.0);

            Assert.AreEqual(-2.0, union.Evaluate(Origin), Eps);
            Assert.AreEqual(-1.0, intersect.Evaluate(Origin), Eps);
            Assert.AreEqual(1.0, shell.Evaluate(Origin), Eps);   // hollow middle
            Assert.AreEqual(-2.125, smooth.Evaluate(Origin), Eps);
        }

        [TestMethod]
        public void SubtractOrderMatters() {
            using var inner = Sdf.Sphere(1.0);
            using var outer = Sdf.Sphere(2.0);
            using var outerMinusInner = outer.Subtract(inner);
            using var innerMinusOuter = inner.Subtract(outer);

            var wall = new Vec3(1.5, 0, 0);
            Assert.IsTrue(outerMinusInner.Evaluate(wall) < 0, "a shell has a wall at r = 1.5");
            Assert.IsTrue(innerMinusOuter.Evaluate(wall) > 0, "inner minus outer is empty");
        }

        // -------------------------------------------------------------------
        // Ownership: operands are shared, not consumed
        // -------------------------------------------------------------------

        [TestMethod]
        public void OperandsStayUsableAfterCombining() {
            using var a = Sdf.Sphere(1.0);
            using var b = Sdf.Sphere(2.0);
            using var u = a.Union(b);

            // Combining did not use up a or b.
            Assert.AreEqual(-1.0, a.Evaluate(Origin), Eps);
            Assert.AreEqual(-2.0, b.Evaluate(Origin), Eps);

            // The same shape can go into a second expression.
            using var i = a.Intersect(b);
            Assert.AreEqual(-1.0, i.Evaluate(Origin), Eps);
        }

        [TestMethod]
        public void ResultOutlivesDisposedOperands() {
            Shape union;
            using (var a = Sdf.Sphere(1.0))
            using (var b = Sdf.Sphere(2.0)) {
                union = a.Union(b);
            } // a and b disposed here

            using (union) {
                Assert.AreEqual(-2.0, union.Evaluate(Origin), Eps);
            }
        }

        // -------------------------------------------------------------------
        // Disposal
        // -------------------------------------------------------------------

        [TestMethod]
        public void UsingADisposedShapeThrowsNamingShape() {
            var s = Sdf.Sphere(1.0);
            s.Dispose();

            var ex = Assert.ThrowsExactly<ObjectDisposedException>(() => s.Evaluate(Origin));
            Assert.AreEqual("Shape", ex.ObjectName);
        }

        [TestMethod]
        public void CombiningWithADisposedOperandThrows() {
            using var a = Sdf.Sphere(1.0);
            var b = Sdf.Sphere(2.0);
            b.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => a.Union(b));
            Assert.ThrowsExactly<ObjectDisposedException>(() => a.SmoothUnion(b, 0.5));
        }

        [TestMethod]
        public void DisposingTwiceIsHarmless() {
            var s = Sdf.Sphere(1.0);
            s.Dispose();
            s.Dispose();
        }

        // -------------------------------------------------------------------
        // Bad arguments produce precise .NET exceptions
        // -------------------------------------------------------------------

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        [DataRow(double.NaN)]
        public void SphereRejectsBadRadius(double radius) {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Sdf.Sphere(radius));
            Assert.AreEqual("radius", ex.ParamName);
        }

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(-0.5)]
        [DataRow(double.NaN)]
        public void SmoothUnionRejectsBadBlendRadius(double blendRadius) {
            using var a = Sdf.Sphere(1.0);
            using var b = Sdf.Sphere(2.0);

            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => a.SmoothUnion(b, blendRadius));
            Assert.AreEqual("blendRadius", ex.ParamName);
        }

        [TestMethod]
        public void CombiningWithNullThrows() {
            using var a = Sdf.Sphere(1.0);

            Assert.AreEqual("other", Assert.ThrowsExactly<ArgumentNullException>(() => a.Union(null!)).ParamName);
            Assert.AreEqual("other", Assert.ThrowsExactly<ArgumentNullException>(() => a.Intersect(null!)).ParamName);
            Assert.AreEqual("other", Assert.ThrowsExactly<ArgumentNullException>(() => a.Subtract(null!)).ParamName);
            Assert.AreEqual("other", Assert.ThrowsExactly<ArgumentNullException>(() => a.SmoothUnion(null!, 1.0)).ParamName);
        }

        // -------------------------------------------------------------------
        // Batch evaluation through spans
        // -------------------------------------------------------------------

        [TestMethod]
        public void SpanEvaluateMatchesSingleEvaluate() {
            using var shape = Sdf.Sphere(1.0).SmoothUnion(Sdf.Sphere(2.0), 1.5);

            var points = new[] {
                new Vec3(0, 0, 0), new Vec3(1.5, 0, 0), new Vec3(0, 3, 0), new Vec3(3, 4, 0),
            };
            var distances = new double[points.Length];

            shape.Evaluate(points, distances); // arrays convert to spans implicitly

            for (int i = 0; i < points.Length; i++) {
                Assert.AreEqual(shape.Evaluate(points[i]), distances[i], 0.0, "point " + i);
            }
        }

        [TestMethod]
        public void SpanEvaluateWorksOnPartOfAnArray() {
            // A span can be a slice: evaluate only the middle two of four points,
            // writing into the middle two slots, without copying anything.
            using var sphere = Sdf.Sphere(1.0);
            var points = new[] { new Vec3(9, 0, 0), new Vec3(2, 0, 0), new Vec3(3, 0, 0), new Vec3(9, 0, 0) };
            var distances = new double[] { -99, -99, -99, -99 };

            sphere.Evaluate(points.AsSpan(1, 2), distances.AsSpan(1, 2));

            Assert.AreEqual(-99, distances[0]); // untouched
            Assert.AreEqual(1.0, distances[1], Eps);
            Assert.AreEqual(2.0, distances[2], Eps);
            Assert.AreEqual(-99, distances[3]); // untouched
        }

        [TestMethod]
        public void SpanLengthMismatchThrows() {
            using var sphere = Sdf.Sphere(1.0);
            var points = new Vec3[3];
            var distances = new double[2];

            var ex = Assert.ThrowsExactly<ArgumentException>(() => sphere.Evaluate(points, distances));
            Assert.AreEqual("distances", ex.ParamName);
        }

        [TestMethod]
        public void EmptySpansAreANoOp() {
            using var sphere = Sdf.Sphere(1.0);
            sphere.Evaluate(ReadOnlySpan<Vec3>.Empty, Span<double>.Empty);
        }
    }
}
