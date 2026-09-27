using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// The public shape API across the boundary. The distance MATH is tested in
    /// test_core.cpp; these check that it arrives intact and that lifetimes hold.
    /// </summary>
    [TestClass]
    public class ShapeTests {
        private const double Eps = 1e-12;
        private static readonly Vec3 Origin = new Vec3(0, 0, 0);

        [TestMethod]
        public void FluentChainEvaluates() {
            // The README usage. The inner spheres are undisposed temporaries; the
            // union retains them, so this is safe.
            using var shape = Sdf.Sphere(1.0).Union(Sdf.Sphere(2.0));
            Assert.AreEqual(-2.0, shape.Evaluate(Origin), Eps);
        }

        [TestMethod]
        public void ResultOutlivesItsDisposedOperands() {
            // THE retain test: without native retain, evaluating the union would
            // read freed memory.
            Shape union;
            using (var a = Sdf.Sphere(1.0))
            using (var b = Sdf.Sphere(2.0)) {
                union = a.Union(b);
            }
            using (union) {
                Assert.AreEqual(-2.0, union.Evaluate(Origin), Eps);
            }
        }

        [TestMethod]
        public void UsingADisposedShapeThrows() {
            var sphere = Sdf.Sphere(1.0);
            sphere.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => sphere.Evaluate(Origin));
        }

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        [DataRow(double.NaN)]
        public void BadArgumentsAreRejectedWithTheParameterName(double radius) {
            // Representative of all argument checks: NaN in particular must never
            // reach native code, where it would poison every distance.
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Sdf.Sphere(radius));
            Assert.AreEqual("radius", ex.ParamName);
        }

        [TestMethod]
        public void TranslateOffsetCrossesWithAllThreeComponents() {
            // A different value per axis: a dropped or swapped component would move
            // the centre elsewhere, and it would not read -1.
            using var moved = Sdf.Sphere(1.0).Translate(1, -2, 3);
            Assert.AreEqual(-1.0, moved.Evaluate(new Vec3(1, -2, 3)), Eps);
        }

        [TestMethod]
        public void BatchEvaluateMatchesSingleEvaluate() {
            using var shape = Sdf.Sphere(1.0).SmoothUnion(Sdf.Sphere(2.0), 1.5);
            var points = new[] { Origin, new Vec3(1.5, 0, 0), new Vec3(0, 3, 0), new Vec3(3, 4, 0) };
            var distances = new double[points.Length];

            shape.Evaluate(points, distances);

            for (int i = 0; i < points.Length; i++) {
                Assert.AreEqual(shape.Evaluate(points[i]), distances[i], 0.0, "point " + i);
            }
        }

        [TestMethod]
        public void EmptyBatchIsANoOp() {
            // An empty span pins to a NULL pointer; native must accept it with count 0.
            using var sphere = Sdf.Sphere(1.0);
            sphere.Evaluate(ReadOnlySpan<Vec3>.Empty, Span<double>.Empty);
        }
    }
}
