using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    /// <summary>
    /// Meshes through the public API. Mesher quality is tested in depth in
    /// test_core.cpp; this checks the same properties survive the copy-out.
    /// </summary>
    [TestClass]
    public class MeshTests {
        private static readonly Vec3 Min = new Vec3(-1.5, -1.5, -1.5);
        private static readonly Vec3 Max = new Vec3(1.5, 1.5, 1.5);

        [TestMethod]
        public void SphereMeshIsAGoodClosedMeshAfterCopyOut() {
            // Counts, copy-out, index range, watertightness, outward winding, and
            // a round trip of every vertex back through batch evaluation.
            using var sphere = Sdf.Sphere(1.0);
            using var mesh = sphere.ToMesh(Min, Max, 32);
            MeshOracle.AssertGoodClosedMesh(mesh, sphere, 3.0 / 32);
        }

        [TestMethod]
        public void MeshOutlivesTheShapeItCameFrom() {
            // A mesh is a snapshot: it holds no reference to the shape.
            Mesh mesh;
            using (var sphere = Sdf.Sphere(1.0)) {
                mesh = sphere.ToMesh(Min, Max, 16);
            }
            using (mesh) {
                Assert.IsTrue(mesh.GetVertices().Length > 0);
            }
        }
    }
}
