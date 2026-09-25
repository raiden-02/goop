using System;
using Goop.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Tests that meshes cross the boundary correctly: created natively, then
    /// read out with the count-then-copy pattern (ABI rule 5).
    /// </summary>
    /// <remarks>
    /// Mesh QUALITY (watertight, outward, right volume) is tested in
    /// test_core.cpp against the C++ mesher directly. These tests are about the
    /// ABI: handles, counts, caller-allocated buffers, and error mapping.
    /// </remarks>
    [TestClass]
    public class MeshInteropTests {
        private const double Half = 1.5;
        private const int Resolution = 32;
        private static readonly Vec3 Min = new Vec3(-Half, -Half, -Half);
        private static readonly Vec3 Max = new Vec3(Half, Half, Half);

        [TestMethod]
        public void SphereMeshRoundTripsThroughCopyOut() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = MakeMesh(sphere);

            (Vec3[] vertices, uint[] indices) = CopyOut(mesh);

            Assert.IsTrue(vertices.Length > 0, "a sphere must produce vertices");
            Assert.AreEqual(0, indices.Length % 3, "3 indices per triangle");
            foreach (uint index in indices) {
                Assert.IsTrue(index < (uint)vertices.Length, "index " + index + " is out of range");
            }
        }

        [TestMethod]
        public void CopiedVerticesLieOnTheSurface() {
            // End to end: mesh natively, copy the vertices out into C#, then send
            // them BACK in through batch evaluation. Every one should be within
            // about one grid cell of the surface.
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = MakeMesh(sphere);
            (Vec3[] vertices, _) = CopyOut(mesh);

            var distances = new double[vertices.Length];
            sphere.Evaluate(vertices, distances);

            double cell = 2 * Half / Resolution;
            for (int i = 0; i < distances.Length; i++) {
                Assert.IsTrue(Math.Abs(distances[i]) <= cell, "vertex " + i + " is " + distances[i] + " from the surface");
            }
        }

        [TestMethod]
        public void MeshOutlivesItsShape() {
            // A mesh is a SNAPSHOT: it holds no reference to the shape.
            MeshSafeHandle mesh;
            using (var sphere = Sdf.Sphere(1.0)) {
                mesh = MakeMesh(sphere);
            } // shape disposed here

            using (mesh) {
                (Vec3[] vertices, _) = CopyOut(mesh);
                Assert.IsTrue(vertices.Length > 0);
            }
        }

        [TestMethod]
        public unsafe void TooSmallBufferIsRejectedAndLeftUntouched() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = MakeMesh(sphere);
            Errors.ThrowIfError(NativeMethods.goop_mesh_vertex_count(mesh, out long count));

            // One slot short, filled with a marker value.
            var tooSmall = new Vec3[count - 1];
            var marker = new Vec3(-7, -7, -7);
            for (int i = 0; i < tooSmall.Length; i++) {
                tooSmall[i] = marker;
            }

            int status;
            fixed (Vec3* p = tooSmall) {
                status = NativeMethods.goop_mesh_copy_vertices(mesh, p, tooSmall.Length);
            }

            var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
            StringAssert.Contains(ex.Message, "too small");
            // Native checked the size BEFORE writing anything.
            foreach (Vec3 v in tooSmall) {
                Assert.AreEqual(marker.X, v.X);
            }
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(0)]
        [DataRow(-5)]
        [DataRow(513)]
        public void ResolutionOutsideTheLimitsIsRejected(int resolution) {
            using var sphere = Sdf.Sphere(1.0);

            int status = NativeMethods.goop_shape_to_mesh(sphere.Handle, Min, Max, resolution,
                IntPtr.Zero, IntPtr.Zero, out MeshSafeHandle mesh);
            using (mesh) {
                Assert.IsTrue(mesh.IsInvalid, "a failed call must hand back no mesh");
                var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
                StringAssert.Contains(ex.Message, "resolution");
            }
        }

        [TestMethod]
        public void InvertedBoundsAreRejected() {
            using var sphere = Sdf.Sphere(1.0);

            int status = NativeMethods.goop_shape_to_mesh(sphere.Handle, Max, Min, Resolution,
                IntPtr.Zero, IntPtr.Zero, out MeshSafeHandle mesh);
            using (mesh) {
                Assert.IsTrue(mesh.IsInvalid);
                var ex = Assert.ThrowsExactly<ArgumentException>(() => Errors.ThrowIfError(status));
                StringAssert.Contains(ex.Message, "bounds");
            }
        }

        [TestMethod]
        public void BoundsThatMissTheShapeGiveAnEmptyMeshNotAnError() {
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = MakeMesh(sphere, new Vec3(10, 10, 10), new Vec3(12, 12, 12));

            (Vec3[] vertices, uint[] indices) = CopyOut(mesh);
            Assert.AreEqual(0, vertices.Length);
            Assert.AreEqual(0, indices.Length);
        }

        [TestMethod]
        public void DisposedMeshIsRefusedBeforeReachingNative() {
            using var sphere = Sdf.Sphere(1.0);
            var mesh = MakeMesh(sphere);
            mesh.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => NativeMethods.goop_mesh_vertex_count(mesh, out _));
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static MeshSafeHandle MakeMesh(Shape shape) => MakeMesh(shape, Min, Max);

        private static MeshSafeHandle MakeMesh(Shape shape, Vec3 min, Vec3 max) {
            int status = NativeMethods.goop_shape_to_mesh(shape.Handle, min, max, Resolution,
                IntPtr.Zero, IntPtr.Zero, out MeshSafeHandle mesh);
            if (status != 0) {
                mesh.Dispose();
                Errors.ThrowIfError(status);
            }
            return mesh;
        }

        // The count-then-copy pattern, exactly as Mesh.cs will do it in M4c:
        //   1. ask how big   2. allocate in C#   3. pin and let C++ fill it
        private static unsafe (Vec3[] Vertices, uint[] Indices) CopyOut(MeshSafeHandle mesh) {
            Errors.ThrowIfError(NativeMethods.goop_mesh_vertex_count(mesh, out long vertexCount));
            Errors.ThrowIfError(NativeMethods.goop_mesh_triangle_count(mesh, out long triangleCount));

            var vertices = new Vec3[vertexCount];
            var indices = new uint[triangleCount * 3];

            int status;
            fixed (Vec3* v = vertices) {
                status = NativeMethods.goop_mesh_copy_vertices(mesh, v, vertices.Length);
            }
            Errors.ThrowIfError(status);

            fixed (uint* i = indices) {
                status = NativeMethods.goop_mesh_copy_indices(mesh, i, indices.Length);
            }
            Errors.ThrowIfError(status);

            return (vertices, indices);
        }
    }
}
