using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests for <see cref="Shape.Translate(Vec3)"/>, through the public API.
    /// </summary>
    /// <remarks>
    /// The translation maths is tested exhaustively in test_core.cpp. These tests
    /// check that it survives the boundary: the offset crosses as a struct by
    /// value, the original is left untouched, ownership holds, and bad input is
    /// rejected with a precise .NET exception.
    /// </remarks>
    [TestClass]
    public class TransformTests {
        private const double Eps = 1e-12;
        private static readonly Vec3 Origin = new Vec3(0, 0, 0);

        [TestMethod]
        public void TranslateMovesTheCentre() {
            using var sphere = Sdf.Sphere(1.0);
            using var moved = sphere.Translate(3, 0, 0);

            Assert.AreEqual(-1.0, moved.Evaluate(new Vec3(3, 0, 0)), Eps); // new centre
            Assert.AreEqual(2.0, moved.Evaluate(Origin), Eps);             // old centre, now outside
        }

        [TestMethod]
        public void OffsetCrossesTheBoundaryWithAllThreeComponents() {
            // Different value on every axis: a dropped or swapped component would
            // put the centre somewhere else, and the centre would not read -1.
            using var sphere = Sdf.Sphere(1.0);
            using var moved = sphere.Translate(new Vec3(1, -2, 3));

            Assert.AreEqual(-1.0, moved.Evaluate(new Vec3(1, -2, 3)), Eps);
        }

        [TestMethod]
        public void TranslateLeavesTheOriginalWhereItWas() {
            using var sphere = Sdf.Sphere(1.0);
            using var moved = sphere.Translate(5, 0, 0);

            Assert.AreEqual(-1.0, sphere.Evaluate(Origin), Eps); // still centred at the origin
        }

        [TestMethod]
        public void TranslatingTwiceAddsTheOffsets() {
            using var shape = Sdf.Sphere(1.0).Translate(1, 0, 0).Translate(0, 2, 0);
            Assert.AreEqual(-1.0, shape.Evaluate(new Vec3(1, 2, 0)), Eps);
        }

        [TestMethod]
        public void MovedShapeOutlivesTheDisposedOriginal() {
            Shape moved;
            using (var sphere = Sdf.Sphere(1.0)) {
                moved = sphere.Translate(3, 0, 0);
            } // original disposed here

            using (moved) {
                Assert.AreEqual(-1.0, moved.Evaluate(new Vec3(3, 0, 0)), Eps);
            }
        }

        [TestMethod]
        [DataRow(double.NaN, 0.0, 0.0)]
        [DataRow(0.0, double.PositiveInfinity, 0.0)]
        [DataRow(0.0, 0.0, double.NegativeInfinity)]
        public void NonFiniteOffsetIsRejected(double x, double y, double z) {
            using var sphere = Sdf.Sphere(1.0);

            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sphere.Translate(x, y, z));
            Assert.AreEqual("offset", ex.ParamName);
        }

        [TestMethod]
        public void TranslatingADisposedShapeThrows() {
            var sphere = Sdf.Sphere(1.0);
            sphere.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => sphere.Translate(1, 0, 0));
        }

        [TestMethod]
        public void TwoSeparatedSpheresMeltTogetherWithSmoothUnion() {
            // THE goop test. Two unit spheres centred at x = -1.2 and x = +1.2
            // leave a 0.4-wide gap around the origin.
            using var sphere = Sdf.Sphere(1.0);
            using var left = sphere.Translate(-1.2, 0, 0);
            using var right = sphere.Translate(1.2, 0, 0);

            using var plain = left.Union(right);
            using var blob = left.SmoothUnion(right, 1.0);

            Assert.IsTrue(plain.Evaluate(Origin) > 0, "plain union: the origin is in the gap");
            Assert.IsTrue(blob.Evaluate(Origin) < 0, "smooth union: the gap is filled - the spheres melted");
            Assert.AreEqual(-0.05, blob.Evaluate(Origin), Eps);
        }

        [TestMethod]
        public void BatchEvaluationWorksOnTranslatedShapes() {
            using var moved = Sdf.Sphere(1.0).Translate(0, 0, 10);
            var points = new[] { new Vec3(0, 0, 10), new Vec3(0, 0, 11), Origin };
            var distances = new double[points.Length];

            moved.Evaluate(points, distances);

            Assert.AreEqual(-1.0, distances[0], Eps);
            Assert.AreEqual(0.0, distances[1], Eps);
            Assert.AreEqual(9.0, distances[2], Eps);
        }
    }
}
