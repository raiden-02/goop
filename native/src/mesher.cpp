// ===========================================================================
// mesher.cpp - turns a distance field into triangles using surface nets.
//
// Surface nets (naive dual contouring) rather than marching cubes, because the
// algorithm is smaller and the output is nicer:
//
//   1. Sample the SDF on a regular grid covering the shape's bounding box.
//   2. Find every cell whose eight corner samples do not all share a sign -
//      those are the cells the surface passes through.
//   3. Place ONE vertex per straddling cell, positioned from the sign changes
//      along that cell's edges. Averaging the zero crossings is the cheap
//      version and is good enough to start with.
//   4. For each grid edge that changes sign, emit a quad joining the vertices
//      of the four cells around that edge, then split the quad into triangles.
//
// Marching cubes puts vertices on edges and needs a 256-entry case table;
// surface nets puts one vertex per cell and needs no table at all. The tradeoff
// is that surface nets can produce non-manifold output on thin features - which
// is exactly the sort of thing the mesh oracle in Goop.Tests should catch.
// ===========================================================================

#include "mesh.hpp"
#include "shape.hpp"

namespace goop
{

// TODO: choose the sampling domain. Query the shape's bounding box, pad it by
//       at least one cell so the surface is never clipped by the grid boundary,
//       and derive the cell size from the requested resolution.

// TODO: step 1 - sample the field into a scalar grid. This is the expensive
//       part, and the natural place both to report progress (one callback per
//       completed z-slice is a reasonable granularity) and to check for
//       cancellation.

// TODO: steps 2 and 3 - for each cell, gather the eight corner signs, skip
//       uniform cells, and place one vertex from the edge zero crossings. Keep
//       a cell-index -> vertex-index map; a flat vector sized to the grid with
//       a sentinel value is simpler and faster than a hash map here.

// TODO: step 4 - walk the grid edges and emit quads, then triangle pairs. Try
//       to get the winding consistent with the sign convention on the first
//       attempt; debugging inverted normals through an STL viewer is tedious.

// TODO: progress and cancellation. Report a fraction in [0, 1]; if the callback
//       returns nonzero, abandon the work and report the cancelled status. The
//       callback is allowed to be null - check that once, not per sample.

// TODO: once it works, revisit the obvious waste: the interior of a large shape
//       gets sampled densely for no reason. Narrow-band or octree sampling is
//       the natural follow-up, but only after the naive version is correct and
//       covered by tests.

} // namespace goop
