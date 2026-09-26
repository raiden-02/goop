using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Goop.Tests {
    // ======================================================================
    // MeshOracle - shared assertions about whether a generated mesh is any good.
    //
    // "The mesher ran without crashing" is not a test. These helpers encode what
    // it actually means for surface-nets output to be correct, so that every
    // meshing test can state its expectations in one line and so that the
    // definition of "correct" lives in exactly one place.
    //
    // An oracle in the testing sense: something that can judge an output as
    // right or wrong without knowing how it was produced. That matters here
    // because there is no reference mesh to diff against - the properties are
    // all we have.
    //
    // These are the C# twins of the checks in native/tests/test_core.cpp. The
    // C++ ones test the mesher directly; these test what actually arrives on
    // the far side of the ABI, after the count-then-copy round trip.
    // ======================================================================

    // TODO: AssertNoDegenerateTriangles by AREA, not just repeated indices. Two
    //       cell vertices can land almost on top of each other, giving a sliver
    //       with three distinct indices but near-zero area. The tolerance should
    //       be relative to the cell size, not absolute.

    // TODO: a helper to dump a failing mesh to gallery/output/ as STL, so a
    //       failure can be looked at rather than only read about.

    /// <summary>Reusable mesh-quality assertions.</summary>
    internal static class MeshOracle {

        /// <summary>
        /// The cheap structural checks. Run these first: if they fail, the rest
        /// report nonsense.
        /// </summary>
        public static void AssertWellFormed(Vec3[] vertices, int[] indices) {
            Assert.AreEqual(0, indices.Length % 3, "index count must be a multiple of 3");

            for (int i = 0; i < vertices.Length; i++) {
                Vec3 v = vertices[i];
                Assert.IsTrue(IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z),
                    "vertex " + i + " is not finite: " + v);
            }

            for (int t = 0; t < indices.Length / 3; t++) {
                int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
                foreach (int index in new[] { a, b, c }) {
                    Assert.IsTrue(index >= 0 && index < vertices.Length,
                        "triangle " + t + " refers to vertex " + index + ", but there are only " + vertices.Length);
                }
                Assert.IsTrue(a != b && b != c && c != a,
                    "triangle " + t + " reuses a vertex: (" + a + ", " + b + ", " + c + ")");
            }
        }

        /// <summary>
        /// Closed AND consistently oriented, via directed edges. Every triangle
        /// (a, b, c) contributes a-&gt;b, b-&gt;c, c-&gt;a. On a closed surface with
        /// every triangle facing the same way, each directed edge appears exactly
        /// once and its reverse also appears. A duplicate means flipped or
        /// non-manifold triangles; a missing reverse means a hole.
        /// </summary>
        public static void AssertWatertight(int[] indices) {
            var directed = new HashSet<long>();
            for (int t = 0; t < indices.Length / 3; t++) {
                for (int e = 0; e < 3; e++) {
                    int from = indices[3 * t + e];
                    int to = indices[3 * t + (e + 1) % 3];
                    Assert.IsTrue(directed.Add(Key(from, to)),
                        "edge " + from + "->" + to + " is used twice (triangle " + t + "): flipped or non-manifold");
                }
            }
            foreach (long key in directed) {
                int from = (int)(key >> 32), to = (int)(key & 0xFFFFFFFF);
                Assert.IsTrue(directed.Contains(Key(to, from)),
                    "edge " + from + "->" + to + " has no neighbour on the other side: the mesh has a hole");
            }
        }

        /// <summary>
        /// The volume the mesh encloses, from the divergence theorem. Positive
        /// when triangles face outward, negative when they are all wound
        /// backwards. Checks shape and winding with one number.
        /// </summary>
        public static double SignedVolume(Vec3[] vertices, int[] indices) {
            double total = 0;
            for (int t = 0; t < indices.Length / 3; t++) {
                Vec3 a = vertices[indices[3 * t]];
                Vec3 b = vertices[indices[3 * t + 1]];
                Vec3 c = vertices[indices[3 * t + 2]];
                total += (a.X * (b.Y * c.Z - b.Z * c.Y)
                        + a.Y * (b.Z * c.X - b.X * c.Z)
                        + a.Z * (b.X * c.Y - b.Y * c.X)) / 6.0;
            }
            return total;
        }

        /// <summary>Triangles face outward: the enclosed volume is positive.</summary>
        public static void AssertOutward(Vec3[] vertices, int[] indices) {
            double volume = SignedVolume(vertices, indices);
            Assert.IsTrue(volume > 0, "signed volume is " + volume + ": the triangles face INWARD");
        }

        /// <summary>
        /// Every vertex is within <paramref name="tolerance"/> of the shape's
        /// surface. Uses batch evaluation, so it is also an end-to-end check that
        /// vertices survive the trip out of native memory and back in again.
        /// </summary>
        public static void AssertOnSurface(Vec3[] vertices, Shape shape, double tolerance) {
            var distances = new double[vertices.Length];
            shape.Evaluate(vertices, distances);
            for (int i = 0; i < distances.Length; i++) {
                Assert.IsTrue(Math.Abs(distances[i]) <= tolerance,
                    "vertex " + i + " at " + vertices[i] + " is " + distances[i] + " from the surface");
            }
        }

        /// <summary>All of the above, for the common case of a closed shape.</summary>
        public static void AssertGoodClosedMesh(Mesh mesh, Shape shape, double cellSize) {
            Vec3[] vertices = mesh.GetVertices();
            int[] indices = mesh.GetIndices();

            Assert.IsTrue(indices.Length > 0, "the mesh is empty");
            AssertWellFormed(vertices, indices);
            AssertWatertight(indices);
            AssertOutward(vertices, indices);
            AssertOnSurface(vertices, shape, cellSize);
        }

        private static long Key(int from, int to) => ((long)from << 32) | (uint)to;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
