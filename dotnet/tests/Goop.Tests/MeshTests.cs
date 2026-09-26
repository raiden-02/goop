using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests for <see cref="Shape.ToMesh"/> and <see cref="Mesh"/>, through the public API.
    /// </summary>
    [TestClass]
    public class MeshTests {
        private const int Resolution = 48;

        // A box that contains a unit sphere with some room: 3 units a side.
        private static readonly Vec3 Min = new Vec3(-1.5, -1.5, -1.5);
        private static readonly Vec3 Max = new Vec3(1.5, 1.5, 1.5);
        private static readonly double Cell = 3.0 / Resolution;

        // -------------------------------------------------------------------
        // Quality: the oracle does the judging
        // -------------------------------------------------------------------

        [TestMethod]
        public void SphereMeshIsAGoodClosedMesh() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, Resolution);

            MeshOracle.AssertGoodClosedMesh(mesh, sphere, Cell);
        }

        [TestMethod]
        public void SphereMeshEnclosesTheRightVolume() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, Resolution);

            double volume = MeshOracle.SignedVolume(mesh.GetVertices(), mesh.GetIndices());
            double expected = 4.0 / 3.0 * Math.PI;
            Assert.AreEqual(expected, volume, 0.05 * expected);
        }

        [TestMethod]
        public void MeltedBlobIsAGoodClosedMeshAndBiggerThanTheSeparateSpheres() {
            // The README's promise, end to end through the public API.
            using var sphere = Sdf.Sphere(1.0);
            using var left = sphere.Translate(-1.2, 0, 0);
            using var right = sphere.Translate(1.2, 0, 0);
            using var apart = left.Union(right);
            using var blob = left.SmoothUnion(right, 1.0);

            var min = new Vec3(-2.6, -1.6, -1.6);
            var max = new Vec3(2.6, 1.6, 1.6);
            const int resolution = 64;
            double cell = 5.2 / resolution;

            using var apartMesh = apart.ToMesh(min, max, resolution);
            using var blobMesh = blob.ToMesh(min, max, resolution);

            MeshOracle.AssertGoodClosedMesh(blobMesh, blob, cell);
            Assert.IsTrue(
                MeshOracle.SignedVolume(blobMesh.GetVertices(), blobMesh.GetIndices()) >
                MeshOracle.SignedVolume(apartMesh.GetVertices(), apartMesh.GetIndices()),
                "melting the spheres together must add material in the gap");
        }

        // -------------------------------------------------------------------
        // The Mesh object
        // -------------------------------------------------------------------

        [TestMethod]
        public void CountsMatchTheArraysHandedOut() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, Resolution);

            Assert.IsTrue(mesh.TriangleCount > 0);
            Assert.AreEqual(mesh.VertexCount, mesh.GetVertices().Length);
            Assert.AreEqual(mesh.TriangleCount * 3, mesh.IndexCount);
            Assert.AreEqual(mesh.IndexCount, mesh.GetIndices().Length);
        }

        [TestMethod]
        public void SpanCopiesMatchTheArrayCopies() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, 16);

            var vertices = new Vec3[mesh.VertexCount];
            var indices = new int[mesh.IndexCount];
            mesh.CopyVertices(vertices);
            mesh.CopyIndices(indices);

            CollectionAssert.AreEqual(mesh.GetVertices(), vertices);
            CollectionAssert.AreEqual(mesh.GetIndices(), indices);
        }

        [TestMethod]
        public void MeshOutlivesTheShapeItCameFrom() {
            Mesh mesh;
            using (var sphere = Sdf.Sphere(1.0)) {
                mesh = sphere.ToMesh(Min, Max, 16);
            } // shape disposed

            using (mesh) {
                Assert.IsTrue(mesh.GetVertices().Length > 0);
            }
        }

        [TestMethod]
        public void BoundsThatMissTheShapeGiveAnEmptyMesh() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(new Vec3(10, 10, 10), new Vec3(12, 12, 12), 16);

            Assert.AreEqual(0, mesh.VertexCount);
            Assert.AreEqual(0, mesh.TriangleCount);
            Assert.AreEqual(0, mesh.GetVertices().Length);
            Assert.AreEqual(0, mesh.GetIndices().Length);
        }

        // -------------------------------------------------------------------
        // Errors
        // -------------------------------------------------------------------

        [TestMethod]
        [DataRow(1)]
        [DataRow(0)]
        [DataRow(-8)]
        [DataRow(513)]
        public void ResolutionOutOfRangeIsRejected(int resolution) {
            using var sphere = Sdf.Sphere(1.0);
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sphere.ToMesh(Min, Max, resolution));
            Assert.AreEqual("resolution", ex.ParamName);
        }

        [TestMethod]
        public void InvertedBoundsAreRejected() {
            using var sphere = Sdf.Sphere(1.0);
            var ex = Assert.ThrowsExactly<ArgumentException>(() => sphere.ToMesh(Max, Min, 16));
            Assert.AreEqual("max", ex.ParamName);
        }

        [TestMethod]
        public void NonFiniteBoundsAreRejected() {
            using var sphere = Sdf.Sphere(1.0);
            var bad = new Vec3(double.NaN, 1, 1);
            Assert.ThrowsExactly<ArgumentException>(() => sphere.ToMesh(Min, bad, 16));
        }

        [TestMethod]
        public void MeshingADisposedShapeThrows() {
            var sphere = Sdf.Sphere(1.0);
            sphere.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => sphere.ToMesh(Min, Max, 16));
        }

        [TestMethod]
        public void ReadingADisposedMeshThrowsNamingMesh() {
            using var sphere = Sdf.Sphere(1.0);
            var mesh = sphere.ToMesh(Min, Max, 16);
            mesh.Dispose();

            var ex = Assert.ThrowsExactly<ObjectDisposedException>(() => mesh.GetVertices());
            Assert.AreEqual("Mesh", ex.ObjectName);
        }

        [TestMethod]
        public void CopyIntoWrongSizedSpanIsRejected() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, 16);

            var ex = Assert.ThrowsExactly<ArgumentException>(() => mesh.CopyVertices(new Vec3[mesh.VertexCount + 1]));
            Assert.AreEqual("destination", ex.ParamName);
        }
    }
}
