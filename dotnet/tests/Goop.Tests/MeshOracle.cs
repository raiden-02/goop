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
    // ======================================================================

    // TODO: an internal static class MeshOracle with the assertions below. Each
    //       should report WHICH triangle or edge failed, not just that something
    //       did; a bare "mesh is not watertight" on 40,000 triangles is
    //       unactionable.

    // TODO: AssertWellFormed(Mesh) - the cheap structural checks that should run
    //       before anything else, because if these fail the rest are noise:
    //         * index count is a multiple of 3
    //         * every index is within [0, VertexCount)
    //         * VertexCount and TriangleCount agree with the arrays returned by
    //           the copy-out functions
    //         * no NaN or infinity in any vertex coordinate

    // TODO: AssertNoDegenerateTriangles(Mesh) - no triangle may have two indices
    //       that are equal, and no triangle may have an area below a tolerance.
    //       Degenerate triangles are the classic surface-nets artifact when two
    //       cell vertices land on top of each other, they slip through most
    //       viewers unnoticed, and they make downstream tools (slicers,
    //       booleans) behave strangely. Tolerance should be relative to the cell
    //       size, not absolute.

    // TODO: AssertManifold(Mesh) - build the edge -> triangle map and assert
    //       every edge is shared by exactly TWO triangles. More than two is a
    //       non-manifold edge; exactly one is a boundary edge, which a closed
    //       surface must not have. Also worth checking: no two triangles share
    //       the same three vertices (duplicate faces).
    //
    //       Note this needs an edge key that treats (a,b) and (b,a) as the same
    //       edge, and it needs WELDED vertices - if the mesher emits duplicate
    //       vertex positions rather than shared indices, every edge looks like a
    //       boundary and the check reports nonsense. Decide whether the mesher
    //       guarantees welding or whether the oracle welds by position first.

    // TODO: AssertWatertight(Mesh) - manifold plus consistently oriented plus
    //       closed. The rigorous check: sum the signed volumes of the
    //       tetrahedra formed by each triangle and the origin; for a closed,
    //       consistently wound surface this converges to the enclosed volume,
    //       and for a mesh with holes or flipped faces it does not. Pairs
    //       nicely with a test that compares the computed volume against the
    //       analytic volume of a sphere, which catches winding errors that pure
    //       topology checks miss entirely.

    // TODO: AssertOnSurface(Mesh, Shape, double tolerance) - the accuracy check
    //       rather than a topology check. Evaluate the SDF at every vertex and
    //       assert the distance is near zero, with the tolerance scaled to the
    //       cell size (surface nets places vertices approximately, so an exact
    //       zero is the wrong expectation). This is also a nice end-to-end use
    //       of the batch evaluation API.

    // TODO: AssertBoundsWithin(Mesh, expected bounding box) - catches the
    //       embarrassing failure where the whole mesh is correct but scaled or
    //       translated wrongly.

    // TODO: a small helper to dump a failing mesh to gallery/output/ as STL, so
    //       a failure can be looked at rather than only read about.
}
