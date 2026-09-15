// ===========================================================================
// test_core.cpp - Catch2 tests for the C++ core.
//
// These link goop_core (the STATIC library) directly, NOT goop.dll: the point is
// to test real C++ types with full access to the private headers. The C ABI is a
// separate concern, tested from C# in Goop.Tests, because the only honest test
// of an ABI is a foreign caller.
// ===========================================================================

#include <catch2/catch_test_macros.hpp>

TEST_CASE("scaffold builds")
{
    REQUIRE(true);
}

// TODO: sphere distance sign convention. A unit sphere at the origin must
//       evaluate to exactly -1 at the centre, ~0 on the surface (use Catch2's
//       WithinAbs matcher, not ==), and +1 at distance 2. Also check a point far
//       away: the value must be the true Euclidean distance, not merely the
//       right sign.

// TODO: smooth union continuity. The whole reason smooth_union exists is that
//       plain min() leaves a crease. Sample the field along a line crossing the
//       seam between two spheres and assert the numerical first derivative has
//       no jump larger than some tolerance - then assert that plain union DOES
//       have one, so the test proves it is measuring the right thing. Also check
//       that the k -> 0 limit converges to plain union.

// TODO: transform correctness. Translating a sphere by t and evaluating at
//       p + t must equal evaluating the original at p. Rotation must not change
//       the distance at the origin. Uniform scale by s must scale distances by
//       s. Twist yields a distance BOUND, so only assert that it never
//       overestimates.

// TODO: mesher output. For a single sphere at a known resolution: the triangle
//       count is nonzero, indices.size() % 3 == 0, every index is in range, no
//       triangle has two identical vertices, and every vertex sits within about
//       one cell width of the true surface. Then the interesting ones: is the
//       result watertight (every edge shared by exactly two triangles), and does
//       a twisted or thin shape break that? The C# MeshOracle asserts the same
//       properties from the other side of the ABI.

// TODO: cancellation. A progress callback that returns nonzero on its first call
//       must abort the mesh promptly and leak nothing.
