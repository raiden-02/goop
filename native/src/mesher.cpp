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
//
// Vocabulary used below:
//   sample  - a grid CORNER point, where the field is evaluated.
//   cell    - a small cube whose eight corners are samples.
//   edge    - the line between two neighbouring samples.
//   inside  - field < 0. Exactly 0 counts as OUTSIDE, consistently everywhere.
// ===========================================================================

#include "mesh.hpp"
#include "shape.hpp"

#include <algorithm>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <string>

namespace goop {

// TODO: narrow-band sampling. Every sample in the grid is evaluated, including
//       the deep interior and far exterior, where nothing interesting happens.
//       Only cells near the surface matter; an octree or a narrow band around
//       it would make high resolutions affordable.

// TODO: sharper vertex placement. Averaging edge crossings rounds off sharp
//       corners (a CSG box edge comes out bevelled). Proper dual contouring
//       solves a small least-squares problem per cell using surface normals
//       (the "QEF") to put the vertex ON the corner instead.

namespace {

// ---------------------------------------------------------------------------
// Tiny Vec3 helpers, local to the mesher. goop::Vec3 is deliberately a plain
// struct with no operators; these keep the maths below readable.
// ---------------------------------------------------------------------------

Vec3 add(const Vec3& a, const Vec3& b) {
    return Vec3{a.x + b.x, a.y + b.y, a.z + b.z};
}

Vec3 sub(const Vec3& a, const Vec3& b) {
    return Vec3{a.x - b.x, a.y - b.y, a.z - b.z};
}

Vec3 scale(const Vec3& a, double s) {
    return Vec3{a.x * s, a.y * s, a.z * s};
}

bool is_finite(const Vec3& v) {
    return std::isfinite(v.x) && std::isfinite(v.y) && std::isfinite(v.z);
}

// ---------------------------------------------------------------------------
// The grid: nx * ny * nz cubic CELLS of side h, starting at `origin`. There is
// one more SAMPLE than cells along each axis, because samples sit on corners.
// ---------------------------------------------------------------------------
struct Grid {
    Vec3 origin{};
    double h = 0.0;
    std::size_t nx = 0, ny = 0, nz = 0; // cells per axis

    // Flatten (i, j, k) into one array index, with i varying fastest. Samples
    // and cells have different widths, hence two functions.
    std::size_t sample_index(std::size_t i, std::size_t j, std::size_t k) const {
        return i + (nx + 1) * (j + (ny + 1) * k);
    }

    std::size_t cell_index(std::size_t i, std::size_t j, std::size_t k) const {
        return i + nx * (j + ny * k);
    }

    Vec3 sample_position(std::size_t i, std::size_t j, std::size_t k) const {
        return Vec3{origin.x + static_cast<double>(i) * h,
                    origin.y + static_cast<double>(j) * h,
                    origin.z + static_cast<double>(k) * h};
    }
};

Grid make_grid(const Vec3& lo, const Vec3& hi, int resolution) {
    const double ex = hi.x - lo.x;
    const double ey = hi.y - lo.y;
    const double ez = hi.z - lo.z;

    // Cubic cells, sized so that `resolution` of them span the longest side.
    Grid g;
    g.h = std::max({ex, ey, ez}) / resolution;

    // Pad by one cell on every side: the outermost samples are then guaranteed
    // to be outside any shape that fits in the box, so no surface is clipped.
    g.origin = Vec3{lo.x - g.h, lo.y - g.h, lo.z - g.h};

    // Enough cells to cover extent + one padding cell at each end. The small
    // epsilon stops ex / h = 7.0000000001 from becoming 8 cells.
    const auto cells = [&g](double extent) {
        return static_cast<std::size_t>(std::ceil(extent / g.h - 1e-9)) + 2;
    };
    g.nx = cells(ex);
    g.ny = cells(ey);
    g.nz = cells(ez);
    return g;
}

void validate(const Vec3& lo, const Vec3& hi, int resolution) {
    if (resolution < kMinResolution || resolution > kMaxResolution) {
        throw std::invalid_argument("resolution must be between " + std::to_string(kMinResolution) +
                                    " and " + std::to_string(kMaxResolution));
    }
    if (!is_finite(lo) || !is_finite(hi)) {
        throw std::invalid_argument("bounds must be finite");
    }
    // !(a > b) rather than (a <= b), so NaN would be rejected too.
    if (!(hi.x > lo.x) || !(hi.y > lo.y) || !(hi.z > lo.z)) {
        throw std::invalid_argument("bounds_max must be greater than bounds_min on every axis");
    }
}

} // namespace

MeshResult mesh_surface_nets(const Shape& shape,
                             const Vec3& boundsMin,
                             const Vec3& boundsMax,
                             int resolution,
                             const ProgressFn& progress,
                             Mesh& out) {
    out.vertices.clear();
    out.indices.clear();
    validate(boundsMin, boundsMax, resolution);

    const Grid g = make_grid(boundsMin, boundsMax, resolution);
    const std::size_t sx = g.nx + 1, sy = g.ny + 1, sz = g.nz + 1; // samples per axis

    // Progress: sampling is by far the most expensive step, so it gets the
    // first 90%; building the mesh gets the rest.
    constexpr double kSamplingShare = 0.9;

    // -----------------------------------------------------------------------
    // Step 1: sample the field at every grid corner.
    // -----------------------------------------------------------------------
    std::vector<double> field(sx * sy * sz);

    for (std::size_t k = 0; k < sz; ++k) {
        for (std::size_t j = 0; j < sy; ++j) {
            for (std::size_t i = 0; i < sx; ++i) {
                field[g.sample_index(i, j, k)] = shape.eval(g.sample_position(i, j, k));
            }
        }

        // One report per z-slice: often enough for a smooth progress bar,
        // rarely enough that the callback costs nothing. Also where we notice
        // a cancel request.
        if (progress) {
            const double fraction =
                kSamplingShare * static_cast<double>(k + 1) / static_cast<double>(sz);
            if (!progress(fraction)) {
                return MeshResult::Cancelled; // out is still empty
            }
        }
    }

    // -----------------------------------------------------------------------
    // Step 2 + 3: one vertex per cell that the surface passes through.
    //
    // cellVertex maps each cell to the index of its vertex in out.vertices, or
    // kNoVertex. A flat array sized to the grid is simpler and faster than a
    // hash map, at the cost of memory proportional to the grid.
    // -----------------------------------------------------------------------
    constexpr uint32_t kNoVertex = std::numeric_limits<uint32_t>::max();
    std::vector<uint32_t> cellVertex(g.nx * g.ny * g.nz, kNoVertex);

    for (std::size_t k = 0; k < g.nz; ++k) {
        for (std::size_t j = 0; j < g.ny; ++j) {
            for (std::size_t i = 0; i < g.nx; ++i) {
                // Gather the cell's eight corners. Corner n has offset
                // (n & 1, (n >> 1) & 1, (n >> 2) & 1), i.e. bit 0 = +x, bit 1 = +y,
                // bit 2 = +z.
                double value[8];
                Vec3 position[8];
                unsigned insideMask = 0;
                for (unsigned n = 0; n < 8; ++n) {
                    const std::size_t ci = i + (n & 1u);
                    const std::size_t cj = j + ((n >> 1) & 1u);
                    const std::size_t ck = k + ((n >> 2) & 1u);
                    value[n] = field[g.sample_index(ci, cj, ck)];
                    position[n] = g.sample_position(ci, cj, ck);
                    if (value[n] < 0.0) {
                        insideMask |= 1u << n;
                    }
                }

                // All eight inside, or all eight outside: the surface does not
                // pass through this cell. By far the most common case.
                if (insideMask == 0u || insideMask == 0xFFu) {
                    continue;
                }

                // Visit the cell's 12 edges. An edge joins two corners that
                // differ in exactly one bit, so for each corner n and each axis
                // bit b not already set in n, (n, n | b) is an edge - which
                // enumerates each of the 12 exactly once.
                Vec3 sum{0.0, 0.0, 0.0};
                int crossings = 0;
                for (unsigned n = 0; n < 8; ++n) {
                    for (unsigned bit = 1; bit <= 4; bit <<= 1) {
                        if ((n & bit) != 0u) {
                            continue;
                        }
                        const unsigned m = n | bit;
                        const bool nInside = value[n] < 0.0;
                        const bool mInside = value[m] < 0.0;
                        if (nInside == mInside) {
                            continue; // no sign change along this edge
                        }
                        // Where along the edge does the field hit zero? Assume
                        // it varies linearly between the two corners. One value
                        // is < 0 and the other >= 0, so the divisor is never 0.
                        const double t = value[n] / (value[n] - value[m]);
                        sum = add(sum, add(position[n], scale(sub(position[m], position[n]), t)));
                        ++crossings;
                    }
                }

                if (out.vertices.size() >= kNoVertex) {
                    throw std::length_error("mesh has too many vertices for 32-bit indices");
                }
                cellVertex[g.cell_index(i, j, k)] = static_cast<uint32_t>(out.vertices.size());
                // The vertex is the average of the crossing points.
                out.vertices.push_back(scale(sum, 1.0 / crossings));
            }
        }
    }

    // -----------------------------------------------------------------------
    // Step 4: one quad per grid edge that changes sign.
    //
    // The four cells sharing an edge each contain it, so each is a surface cell
    // and each has a vertex. Join those four vertices into a quad, then split
    // the quad into two triangles.
    //
    // Winding: each quad below is listed in the order whose normal points
    // along +axis (worked out with the right-hand rule, e.g. for an x-edge the
    // quad lies in the y-z plane and y x z = +x). That is outward exactly when
    // the field INCREASES along +axis, i.e. when the edge starts inside. If it
    // starts outside, the order is flipped. This is what makes every triangle
    // counter-clockwise from outside, as mesh.hpp promises.
    // -----------------------------------------------------------------------
    const auto emit_quad =
        [&](std::size_t a, std::size_t b, std::size_t c, std::size_t d, bool flip) {
            const uint32_t va = cellVertex[a], vb = cellVertex[b], vc = cellVertex[c],
                           vd = cellVertex[d];
            if (!flip) {
                out.indices.insert(out.indices.end(), {va, vb, vc, va, vc, vd});
            } else {
                out.indices.insert(out.indices.end(), {va, vc, vb, va, vd, vc});
            }
        };

    for (std::size_t k = 0; k < sz; ++k) {
        for (std::size_t j = 0; j < sy; ++j) {
            for (std::size_t i = 0; i < sx; ++i) {
                const bool startInside = field[g.sample_index(i, j, k)] < 0.0;
                const bool flip = !startInside;

                // Edge along +x, from sample (i,j,k) to (i+1,j,k). Its four cells
                // are at (j-1..j, k-1..k), so it needs 1 <= j < ny and
                // 1 <= k < nz. Edges on the grid boundary are skipped - the
                // padding guarantees none of them changes sign.
                if (i < g.nx && j >= 1 && j < g.ny && k >= 1 && k < g.nz &&
                    startInside != (field[g.sample_index(i + 1, j, k)] < 0.0)) {
                    emit_quad(g.cell_index(i, j - 1, k - 1),
                              g.cell_index(i, j, k - 1),
                              g.cell_index(i, j, k),
                              g.cell_index(i, j - 1, k),
                              flip);
                }

                // Edge along +y. Quad in the z-x plane: z x x = +y.
                if (j < g.ny && i >= 1 && i < g.nx && k >= 1 && k < g.nz &&
                    startInside != (field[g.sample_index(i, j + 1, k)] < 0.0)) {
                    emit_quad(g.cell_index(i - 1, j, k - 1),
                              g.cell_index(i - 1, j, k),
                              g.cell_index(i, j, k),
                              g.cell_index(i, j, k - 1),
                              flip);
                }

                // Edge along +z. Quad in the x-y plane: x x y = +z.
                if (k < g.nz && i >= 1 && i < g.nx && j >= 1 && j < g.ny &&
                    startInside != (field[g.sample_index(i, j, k + 1)] < 0.0)) {
                    emit_quad(g.cell_index(i - 1, j - 1, k),
                              g.cell_index(i, j - 1, k),
                              g.cell_index(i, j, k),
                              g.cell_index(i - 1, j, k),
                              flip);
                }
            }
        }
    }

    // Final report. The work is already done, so a cancel request here is
    // ignored rather than throwing away a finished mesh.
    if (progress) {
        progress(1.0);
    }
    return MeshResult::Completed;
}

} // namespace goop
