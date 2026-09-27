// ===========================================================================
// test_core.cpp - Catch2 tests for the C++ core.
//
// These link goop_core (the STATIC library) directly, NOT goop.dll: the point is
// to test real C++ types with full access to the private headers. The C ABI is a
// separate concern, tested from C# in Goop.Tests, because the only honest test
// of an ABI is a foreign caller.
//
// Split of responsibilities:
//   * here (C++): the MATH and the REFCOUNT, tested on the real classes.
//   * Goop.Tests (C#): that the same things survive the trip across the ABI.
//
// One test per risk. Each TEST_CASE below guards something that, if broken,
// would silently produce wrong geometry or corrupt memory.
// ===========================================================================

#include "mesh.hpp"
#include "shape.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <cstdint>
#include <set>
#include <utility>

using Catch::Matchers::WithinAbs;

namespace {

constexpr double kEps = 1e-12;
constexpr double kPi = 3.14159265358979323846;

// Holds one reference and releases it on scope exit, so a failing REQUIRE (which
// unwinds out of the test) cannot leak a shape.
class Ref {
  public:
    explicit Ref(goop::Shape* p) : m_p(p) {}

    ~Ref() {
        reset();
    }

    Ref(const Ref&) = delete;
    Ref& operator=(const Ref&) = delete;

    goop::Shape* get() const {
        return m_p;
    }

    goop::Shape* operator->() const {
        return m_p;
    }

    void reset() {
        if (m_p != nullptr) {
            m_p->release();
            m_p = nullptr;
        }
    }

  private:
    goop::Shape* m_p;
};

double at(const Ref& s, double x, double y, double z) {
    return s->eval(goop::Vec3{x, y, z});
}

// Counts live instances, so a test can SEE the moment a refcount hits zero.
class Probe final : public goop::Shape {
  public:
    static int alive;

    Probe() {
        ++alive;
    }

    ~Probe() override {
        --alive;
    }

    double eval(const goop::Vec3&) const override {
        return 0.0;
    }
};

int Probe::alive = 0;

goop::Mesh mesh_of(const Ref& s, double half, int resolution) {
    goop::Mesh m;
    const auto result = goop::mesh_surface_nets(*s.get(),
                                                goop::Vec3{-half, -half, -half},
                                                goop::Vec3{half, half, half},
                                                resolution,
                                                nullptr,
                                                m);
    REQUIRE(result == goop::MeshResult::Completed);
    return m;
}

// Volume enclosed by a closed mesh (divergence theorem). Positive only if the
// triangles face OUTWARD, so it checks the shape and the winding at once.
double signed_volume(const goop::Mesh& m) {
    double total = 0.0;
    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const goop::Vec3& a = m.vertices[m.indices[3 * t + 0]];
        const goop::Vec3& b = m.vertices[m.indices[3 * t + 1]];
        const goop::Vec3& c = m.vertices[m.indices[3 * t + 2]];
        total += (a.x * (b.y * c.z - b.z * c.y) + a.y * (b.z * c.x - b.x * c.z) +
                  a.z * (b.x * c.y - b.y * c.x)) /
                 6.0;
    }
    return total;
}

// Directed-edge check: on a closed, consistently wound surface every edge a->b
// appears exactly once and its reverse b->a also appears. Returns the number of
// violations (holes, flipped or non-manifold triangles). 0 is perfect.
std::size_t bad_edge_count(const goop::Mesh& m) {
    std::set<std::pair<uint32_t, uint32_t>> directed;
    std::size_t bad = 0;
    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const uint32_t v[3] = {m.indices[3 * t], m.indices[3 * t + 1], m.indices[3 * t + 2]};
        for (int e = 0; e < 3; ++e) {
            if (!directed.insert({v[e], v[(e + 1) % 3]}).second) {
                ++bad;
            }
        }
    }
    for (const auto& edge : directed) {
        if (directed.count({edge.second, edge.first}) == 0) {
            ++bad;
        }
    }
    return bad;
}

} // namespace

// ---------------------------------------------------------------------------
// Distance maths
// ---------------------------------------------------------------------------

TEST_CASE("sphere: signed, and a true Euclidean distance") {
    Ref s{new goop::Sphere(1.0)};
    CHECK_THAT(at(s, 0, 0, 0), WithinAbs(-1.0, kEps)); // inside
    CHECK_THAT(at(s, 1, 0, 0), WithinAbs(0.0, kEps));  // on the surface
    CHECK_THAT(at(s, 3, 4, 0), WithinAbs(4.0, kEps));  // |(3,4,0)| = 5, minus radius
}

TEST_CASE("csg: union = min, intersect = max, subtract = max(a, -b)") {
    // Concentric spheres: at the origin inner = -1, outer = -2.
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref u{new goop::Union(inner.get(), outer.get())};
    Ref i{new goop::Intersect(inner.get(), outer.get())};
    Ref shell{new goop::Subtract(outer.get(), inner.get())};

    CHECK_THAT(at(u, 0, 0, 0), WithinAbs(-2.0, kEps));
    CHECK_THAT(at(i, 0, 0, 0), WithinAbs(-1.0, kEps));
    CHECK_THAT(at(shell, 0, 0, 0), WithinAbs(1.0, kEps));    // hollow middle
    CHECK_THAT(at(shell, 1.5, 0, 0), WithinAbs(-0.5, kEps)); // inside the wall
}

TEST_CASE("smooth union: exact min far from the seam, blended and never outside near it") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref sharp{new goop::SmoothUnion(inner.get(), outer.get(), 0.5)};
    Ref soft{new goop::SmoothUnion(inner.get(), outer.get(), 2.0)};
    Ref plain{new goop::Union(inner.get(), outer.get())};

    // Fields differ by 1 at the origin. k = 0.5 < 1: no blending, exactly min.
    CHECK_THAT(at(sharp, 0, 0, 0), WithinAbs(-2.0, kEps));
    // k = 2 > 1: h = 0.25, mix = -1.75, bump = 2*0.25*0.75 = 0.375 -> -2.125.
    CHECK_THAT(at(soft, 0, 0, 0), WithinAbs(-2.125, kEps));
    // The blend only ever pulls the surface outward.
    for (double x = -3.0; x <= 3.0; x += 0.25) {
        CHECK(at(soft, x, 0, 0) <= at(plain, x, 0, 0) + kEps);
    }
}

TEST_CASE("translate: moved(p + t) == original(p)") {
    Ref s{new goop::Sphere(1.0)};
    const goop::Vec3 t{0.5, -2.0, 1.25};
    Ref moved{new goop::Translate(s.get(), t)};

    for (double x = -2.0; x <= 2.0; x += 0.5) {
        for (double y = -2.0; y <= 2.0; y += 0.5) {
            CHECK_THAT(at(moved, x + t.x, y + t.y, 0.3 + t.z), WithinAbs(at(s, x, y, 0.3), kEps));
        }
    }
}

TEST_CASE("goop: smooth union fills the gap between separated spheres; union does not") {
    // Unit spheres at x = -1.2 and +1.2 leave a 0.4 gap; each reads 0.2 at the
    // origin. Smooth union with k = 1: 0.2 - 1 * 0.25 = -0.05, i.e. INSIDE.
    Ref base{new goop::Sphere(1.0)};
    Ref left{new goop::Translate(base.get(), goop::Vec3{-1.2, 0, 0})};
    Ref right{new goop::Translate(base.get(), goop::Vec3{1.2, 0, 0})};
    Ref plain{new goop::Union(left.get(), right.get())};
    Ref blob{new goop::SmoothUnion(left.get(), right.get(), 1.0)};

    CHECK_THAT(at(plain, 0, 0, 0), WithinAbs(0.2, kEps));
    CHECK_THAT(at(blob, 0, 0, 0), WithinAbs(-0.05, kEps));
}

// ---------------------------------------------------------------------------
// Ownership
// ---------------------------------------------------------------------------

TEST_CASE("refcount: a child lives exactly as long as some parent holds it") {
    Probe::alive = 0;
    {
        Ref a{new Probe};
        Ref b{new Probe};
        Ref u{new goop::Union(a.get(), b.get())};                 // BinaryShape
        Ref t{new goop::Translate(a.get(), goop::Vec3{1, 0, 0})}; // UnaryShape; a is shared
        a.reset();
        b.reset();
        CHECK(Probe::alive == 2); // parents still hold both

        u.reset();
        CHECK(Probe::alive == 1); // b freed; a still held by t

        t.reset();
        CHECK(Probe::alive == 0);
    }
}

// ---------------------------------------------------------------------------
// Mesher
// ---------------------------------------------------------------------------

TEST_CASE("mesher: a sphere mesh is well formed, closed, outward, and on the surface") {
    Ref s{new goop::Sphere(1.0)};
    const double half = 1.5;
    const int resolution = 48;
    const double cell = 2.0 * half / resolution;
    const goop::Mesh m = mesh_of(s, half, resolution);

    REQUIRE(m.triangle_count() > 0);
    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const uint32_t a = m.indices[3 * t], b = m.indices[3 * t + 1], c = m.indices[3 * t + 2];
        REQUIRE((a < m.vertices.size() && b < m.vertices.size() && c < m.vertices.size()));
        REQUIRE((a != b && b != c && c != a));
    }
    for (const goop::Vec3& v : m.vertices) {
        REQUIRE(std::abs(s->eval(v)) <= cell);
    }
    CHECK(bad_edge_count(m) == 0);

    const double expected = 4.0 / 3.0 * kPi;
    const double volume = signed_volume(m);
    CHECK(volume > 0.0);
    CHECK_THAT(volume, WithinAbs(expected, 0.05 * expected));
}

TEST_CASE("mesher: the melted blob is one closed surface, bigger than the spheres apart") {
    Ref base{new goop::Sphere(1.0)};
    Ref left{new goop::Translate(base.get(), goop::Vec3{-1.2, 0, 0})};
    Ref right{new goop::Translate(base.get(), goop::Vec3{1.2, 0, 0})};
    Ref plain{new goop::Union(left.get(), right.get())};
    Ref blob{new goop::SmoothUnion(left.get(), right.get(), 1.0)};

    const goop::Mesh blobMesh = mesh_of(blob, 2.6, 64);
    CHECK(bad_edge_count(blobMesh) == 0);
    CHECK(signed_volume(blobMesh) > signed_volume(mesh_of(plain, 2.6, 64)));
}

TEST_CASE("mesher: returning false from progress cancels at once and leaves the mesh empty") {
    Ref s{new goop::Sphere(1.0)};
    int calls = 0;
    goop::Mesh m;

    const auto result = goop::mesh_surface_nets(
        *s.get(),
        goop::Vec3{-1.5, -1.5, -1.5},
        goop::Vec3{1.5, 1.5, 1.5},
        64,
        [&calls](double) {
            ++calls;
            return false;
        },
        m);

    CHECK(result == goop::MeshResult::Cancelled);
    CHECK(calls == 1);
    CHECK(m.vertices.empty());
    CHECK(m.indices.empty());
}
