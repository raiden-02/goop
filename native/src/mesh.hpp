// ===========================================================================
// mesh.hpp - plain mesh storage. Deliberately dumb.
//
// This is the output of the mesher and the input to the copy-out functions in
// api.cpp: a flat vertex array plus a flat index array, three indices per
// triangle. No normals, no attributes, no adjacency, no cleverness.
//
// Anything smarter - adjacency, vertex welding, normal generation - belongs
// either in the mesher (before the mesh is handed over) or on the managed side.
// This type exists so that goop_mesh has something to point at.
// ===========================================================================

#ifndef GOOP_MESH_HPP
#define GOOP_MESH_HPP

#include "shape.hpp" // Vec3, Shape

#include <cstddef>
#include <cstdint>
#include <functional>
#include <vector>

namespace goop {

// ---------------------------------------------------------------------------
// Mesh
//
// WINDING ORDER - the one convention everything downstream depends on:
//
//   Triangles are COUNTER-CLOCKWISE when viewed from OUTSIDE the shape.
//   Equivalently: the normal (v1 - v0) x (v2 - v0) points OUTWARD, towards
//   increasing distance.
//
// The STL writer relies on this to write outward normals, and the signed-volume
// test relies on it to come out positive. Get it backwards and every viewer
// renders the model inside-out.
// ---------------------------------------------------------------------------
struct Mesh {
    std::vector<Vec3> vertices;
    std::vector<uint32_t> indices; // 3 per triangle, into vertices

    std::size_t triangle_count() const {
        return indices.size() / 3;
    }
};

// ---------------------------------------------------------------------------
// The mesher
// ---------------------------------------------------------------------------

// Called as meshing proceeds, with a fraction in [0, 1] that only ever grows
// and ends at exactly 1. Return true to carry on, false to cancel.
using ProgressFn = std::function<bool(double fraction)>;

enum class MeshResult {
    Completed,
    Cancelled
};

// Grid resolution limits. The mesher samples the whole grid, so memory grows
// with the CUBE of the resolution: 512 is about 1 GB of samples.
constexpr int kMinResolution = 2;
constexpr int kMaxResolution = 512;

// Meshes the zero surface of `shape` inside the box [boundsMin, boundsMax]
// using surface nets. Defined in mesher.cpp.
//
//   resolution  number of grid cells along the LONGEST side of the box. Cells
//               are cubes, so shorter sides get proportionally fewer.
//   progress    may be empty, in which case meshing cannot be cancelled.
//   out         replaced with the result. Empty if cancelled.
//
// The box is padded by one cell on every side, so a surface that stays inside
// the box is never clipped. A surface that crosses the box's edge WILL be
// clipped, leaving a hole - pass bounds that contain the whole shape.
//
// Throws std::invalid_argument for a resolution outside the limits above, a
// non-finite corner, or a box that is empty along any axis.
MeshResult mesh_surface_nets(const Shape& shape,
                             const Vec3& boundsMin,
                             const Vec3& boundsMax,
                             int resolution,
                             const ProgressFn& progress,
                             Mesh& out);

} // namespace goop

#endif // GOOP_MESH_HPP
