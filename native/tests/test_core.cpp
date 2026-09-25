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
// ===========================================================================

#include "mesh.hpp"
#include "shape.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include <cmath>
#include <cstdint>
#include <set>
#include <stdexcept>
#include <utility>
#include <vector>

using Catch::Matchers::WithinAbs;

namespace {

constexpr double kEps = 1e-12;

// Holds one reference and releases it on scope exit, so a failing REQUIRE (which
// unwinds out of the test) cannot leak a shape. RAII for the refcount, the same
// idea BinaryShape uses internally.
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

    // Drop our reference early, e.g. to prove a parent keeps the child alive.
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

// A shape that counts how many instances are alive. Lets a test observe the
// moment the refcount reaches zero and the destructor actually runs, which is
// otherwise invisible.
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

} // namespace

// ---------------------------------------------------------------------------
// Sphere
// ---------------------------------------------------------------------------

TEST_CASE("sphere: negative inside, zero on the surface, positive outside") {
    Ref s{new goop::Sphere(1.0)};

    CHECK_THAT(at(s, 0, 0, 0), WithinAbs(-1.0, kEps)); // centre
    CHECK_THAT(at(s, 1, 0, 0), WithinAbs(0.0, kEps));  // on the surface
    CHECK_THAT(at(s, 2, 0, 0), WithinAbs(1.0, kEps));  // outside
}

TEST_CASE("sphere: value is the true Euclidean distance, not just the right sign") {
    Ref s{new goop::Sphere(1.0)};

    // (3,4,0) is 5 from the origin, so 4 from a unit sphere's surface.
    CHECK_THAT(at(s, 3, 4, 0), WithinAbs(4.0, kEps));
    // Same distance along every axis: no axis is special.
    CHECK_THAT(at(s, 0, 0, 2), WithinAbs(at(s, 2, 0, 0), kEps));
}

// ---------------------------------------------------------------------------
// CSG. Two concentric spheres, radius 1 (inner) and 2 (outer). At the origin
// they evaluate to -1 and -2, which makes every operator's answer easy to
// predict by hand.
// ---------------------------------------------------------------------------

TEST_CASE("union takes the smaller distance") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref u{new goop::Union(inner.get(), outer.get())};

    CHECK_THAT(at(u, 0, 0, 0), WithinAbs(-2.0, kEps)); // min(-1, -2)
}

TEST_CASE("intersect takes the larger distance") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref i{new goop::Intersect(inner.get(), outer.get())};

    CHECK_THAT(at(i, 0, 0, 0), WithinAbs(-1.0, kEps)); // max(-1, -2)
}

TEST_CASE("subtract carves b out of a, leaving a hollow shell") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref shell{new goop::Subtract(outer.get(), inner.get())}; // outer minus inner

    CHECK(at(shell, 0, 0, 0) > 0.0);                         // the hole is outside
    CHECK_THAT(at(shell, 0, 0, 0), WithinAbs(1.0, kEps));    // max(-2, +1)
    CHECK_THAT(at(shell, 1.5, 0, 0), WithinAbs(-0.5, kEps)); // inside the shell wall
    CHECK_THAT(at(shell, 3, 0, 0), WithinAbs(1.0, kEps));    // outside everything
}

TEST_CASE("subtract is not symmetric") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref ab{new goop::Subtract(outer.get(), inner.get())};
    Ref ba{new goop::Subtract(inner.get(), outer.get())};

    // inner minus outer removes everything: nothing is left inside anywhere.
    CHECK(at(ba, 0, 0, 0) > 0.0);
    CHECK(at(ab, 1.5, 0, 0) < 0.0);
    CHECK(at(ba, 1.5, 0, 0) > 0.0);
}

// ---------------------------------------------------------------------------
// Smooth union
// ---------------------------------------------------------------------------

TEST_CASE("smooth union equals plain union when the fields differ by at least k") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    // At the origin the fields are -1 and -2: they differ by 1, and k = 0.5 < 1,
    // so h clamps to 0 and there is no blending at all.
    Ref s{new goop::SmoothUnion(inner.get(), outer.get(), 0.5)};

    CHECK_THAT(at(s, 0, 0, 0), WithinAbs(-2.0, kEps));
}

TEST_CASE("smooth union blends when the fields are within k of each other") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    // k = 2 > 1, so it blends. Worked by hand:
    //   h     = clamp(0.5 + 0.5 * (-2 - -1) / 2) = 0.25
    //   mixed = -2 + (-1 - -2) * 0.25           = -1.75
    //   bump  = 2 * 0.25 * 0.75                  = 0.375
    //   total = -1.75 - 0.375                    = -2.125
    Ref s{new goop::SmoothUnion(inner.get(), outer.get(), 2.0)};

    CHECK_THAT(at(s, 0, 0, 0), WithinAbs(-2.125, kEps));
}

TEST_CASE("smooth union is never outside the plain union") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref plain{new goop::Union(inner.get(), outer.get())};
    Ref smooth{new goop::SmoothUnion(inner.get(), outer.get(), 1.5)};

    // The blend only ever pulls the surface OUTWARD (more negative), so the
    // smooth result is <= the plain one everywhere.
    for (double x = -3.0; x <= 3.0; x += 0.25) {
        INFO("x = " << x);
        CHECK(at(smooth, x, 0, 0) <= at(plain, x, 0, 0) + kEps);
    }
}

TEST_CASE("smooth union is symmetric in its operands") {
    Ref inner{new goop::Sphere(1.0)};
    Ref outer{new goop::Sphere(2.0)};
    Ref ab{new goop::SmoothUnion(inner.get(), outer.get(), 2.0)};
    Ref ba{new goop::SmoothUnion(outer.get(), inner.get(), 2.0)};

    for (double x = -3.0; x <= 3.0; x += 0.5) {
        INFO("x = " << x);
        CHECK_THAT(at(ab, x, 0, 0), WithinAbs(at(ba, x, 0, 0), kEps));
    }
}

// ---------------------------------------------------------------------------
// Reference counting
// ---------------------------------------------------------------------------

TEST_CASE("a combinator keeps its children alive, then frees them when it dies") {
    Probe::alive = 0;
    {
        Ref a{new Probe};
        Ref b{new Probe};
        Ref u{new goop::Union(a.get(), b.get())};
        REQUIRE(Probe::alive == 2);

        // Drop our own references. The union still holds one each.
        a.reset();
        b.reset();
        CHECK(Probe::alive == 2);

        // Drop the union: it releases both children, whose counts reach zero.
        u.reset();
        CHECK(Probe::alive == 0);
    }
    CHECK(Probe::alive == 0);
}

TEST_CASE("the same child can be shared by two parents (the graph is a DAG)") {
    Probe::alive = 0;
    {
        Ref shared{new Probe};
        Ref other{new Probe};
        Ref u{new goop::Union(shared.get(), other.get())};
        Ref i{new goop::Intersect(shared.get(), other.get())};
        shared.reset();
        other.reset();

        u.reset();
        CHECK(Probe::alive == 2); // i still holds both

        i.reset();
        CHECK(Probe::alive == 0);
    }
}

TEST_CASE("using the same shape as both operands is safe") {
    Probe::alive = 0;
    {
        Ref a{new Probe};
        Ref u{new goop::Union(a.get(), a.get())}; // retained twice
        a.reset();
        CHECK(Probe::alive == 1);
        u.reset(); // released twice
        CHECK(Probe::alive == 0);
    }
}

// ---------------------------------------------------------------------------
// Translate
// ---------------------------------------------------------------------------

TEST_CASE("translate moves the shape: its centre is now at the offset") {
    Ref s{new goop::Sphere(1.0)};
    Ref moved{new goop::Translate(s.get(), goop::Vec3{3, 0, 0})};

    CHECK_THAT(at(moved, 3, 0, 0), WithinAbs(-1.0, kEps)); // new centre
    CHECK_THAT(at(moved, 4, 0, 0), WithinAbs(0.0, kEps));  // new surface
    CHECK_THAT(at(moved, 0, 0, 0), WithinAbs(2.0, kEps));  // old centre is now outside
}

TEST_CASE("translate: moved shape at p+t equals original at p, for any p") {
    // The defining property. If this holds everywhere, Translate is correct.
    Ref s{new goop::Sphere(1.0)};
    const goop::Vec3 t{0.5, -2.0, 1.25};
    Ref moved{new goop::Translate(s.get(), t)};

    for (double x = -2.0; x <= 2.0; x += 0.5) {
        for (double y = -2.0; y <= 2.0; y += 0.5) {
            INFO("p = (" << x << ", " << y << ", 0.3)");
            CHECK_THAT(at(moved, x + t.x, y + t.y, 0.3 + t.z), WithinAbs(at(s, x, y, 0.3), kEps));
        }
    }
}

TEST_CASE("translate by zero changes nothing") {
    Ref s{new goop::Sphere(1.0)};
    Ref same{new goop::Translate(s.get(), goop::Vec3{0, 0, 0})};

    CHECK_THAT(at(same, 0, 0, 0), WithinAbs(at(s, 0, 0, 0), kEps));
    CHECK_THAT(at(same, 3, 4, 0), WithinAbs(at(s, 3, 4, 0), kEps));
}

TEST_CASE("translating twice adds the offsets") {
    Ref s{new goop::Sphere(1.0)};
    Ref once{new goop::Translate(s.get(), goop::Vec3{1, 0, 0})};
    Ref twice{new goop::Translate(once.get(), goop::Vec3{0, 2, 0})};

    CHECK_THAT(at(twice, 1, 2, 0), WithinAbs(-1.0, kEps)); // centre at (1, 2, 0)
}

TEST_CASE("translate keeps its child alive, then frees it when it dies") {
    Probe::alive = 0;
    {
        Ref child{new Probe};
        Ref moved{new goop::Translate(child.get(), goop::Vec3{1, 0, 0})};

        child.reset();
        CHECK(Probe::alive == 1); // Translate still holds it

        moved.reset();
        CHECK(Probe::alive == 0);
    }
}

// ---------------------------------------------------------------------------
// The goop effect: two shapes APART, melted together
// ---------------------------------------------------------------------------

TEST_CASE("smooth union fills the gap between separated shapes; plain union does not") {
    // Two unit spheres at x = -1.2 and x = +1.2. Their surfaces stop at x = -0.2
    // and x = +0.2, so there is a 0.4-wide gap around the origin.
    Ref base{new goop::Sphere(1.0)};
    Ref left{new goop::Translate(base.get(), goop::Vec3{-1.2, 0, 0})};
    Ref right{new goop::Translate(base.get(), goop::Vec3{1.2, 0, 0})};

    Ref plain{new goop::Union(left.get(), right.get())};
    Ref blob{new goop::SmoothUnion(left.get(), right.get(), 1.0)};

    // Plain union: the origin is in the gap, 0.2 from either surface.
    CHECK_THAT(at(plain, 0, 0, 0), WithinAbs(0.2, kEps));

    // Smooth union: both fields are 0.2 there, a tie, so h = 0.5 and the bump
    // is k * 0.25 = 0.25. Result 0.2 - 0.25 = -0.05: NEGATIVE, i.e. inside. The
    // two spheres have melted into one blob with a neck across the gap.
    CHECK_THAT(at(blob, 0, 0, 0), WithinAbs(-0.05, kEps));
}

// TODO: the remaining transforms. Rotation must not change the distance at the
//       origin. Uniform scale by s must scale distances by s. Twist yields a
//       distance BOUND, so only assert that it never overestimates.

// ===========================================================================
// Mesher
//
// There is no "correct mesh" to compare against, so these tests check
// PROPERTIES any good mesh of a closed shape must have: well-formed indices,
// vertices on the surface, no holes, consistent winding, and the right volume.
// ===========================================================================

namespace {

constexpr double kPi = 3.14159265358979323846;

// Meshes s over the cube [-half, half]^3 and requires it to complete.
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

// Volume enclosed by a closed mesh, from the divergence theorem: sum, over
// every triangle, of the signed volume of the tetrahedron it forms with the
// origin. Outward-facing triangles add, inward-facing ones subtract.
//
// This single number checks TWO things at once:
//   * magnitude close to the true volume -> the shape is right
//   * sign positive                       -> the winding is outward
// If every triangle were wound backwards it would come out NEGATIVE.
double signed_volume(const goop::Mesh& m) {
    double total = 0.0;
    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const goop::Vec3& a = m.vertices[m.indices[3 * t + 0]];
        const goop::Vec3& b = m.vertices[m.indices[3 * t + 1]];
        const goop::Vec3& c = m.vertices[m.indices[3 * t + 2]];
        // a . (b x c) / 6
        const double cx = b.y * c.z - b.z * c.y;
        const double cy = b.z * c.x - b.x * c.z;
        const double cz = b.x * c.y - b.y * c.x;
        total += (a.x * cx + a.y * cy + a.z * cz) / 6.0;
    }
    return total;
}

// Watertight AND consistently oriented, checked through DIRECTED edges.
//
// Every triangle (a, b, c) contributes the directed edges a->b, b->c, c->a.
// On a closed surface where every triangle faces outward:
//   * each directed edge appears exactly ONCE (twice would mean two triangles
//     facing opposite ways across it, or three triangles on one edge), and
//   * its reverse b->a also appears (otherwise the edge is on a hole).
// Returns the number of directed edges that break either rule: 0 is perfect.
std::size_t bad_edge_count(const goop::Mesh& m) {
    std::set<std::pair<uint32_t, uint32_t>> directed;
    std::size_t duplicates = 0;
    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const uint32_t v[3] = {m.indices[3 * t], m.indices[3 * t + 1], m.indices[3 * t + 2]};
        for (int e = 0; e < 3; ++e) {
            if (!directed.insert({v[e], v[(e + 1) % 3]}).second) {
                ++duplicates;
            }
        }
    }
    std::size_t unmatched = 0;
    for (const auto& edge : directed) {
        if (directed.count({edge.second, edge.first}) == 0) {
            ++unmatched;
        }
    }
    return duplicates + unmatched;
}

} // namespace

TEST_CASE("mesher: a sphere produces a non-empty, well-formed mesh") {
    Ref s{new goop::Sphere(1.0)};
    const goop::Mesh m = mesh_of(s, 1.5, 32);

    REQUIRE(m.triangle_count() > 0);
    CHECK(m.indices.size() % 3 == 0);

    for (std::size_t t = 0; t < m.triangle_count(); ++t) {
        const uint32_t a = m.indices[3 * t], b = m.indices[3 * t + 1], c = m.indices[3 * t + 2];
        INFO("triangle " << t);
        REQUIRE(a < m.vertices.size());
        REQUIRE(b < m.vertices.size());
        REQUIRE(c < m.vertices.size());
        // Surface nets joins four DIFFERENT cells, so a triangle can never
        // reuse a vertex. If it did, it would be a degenerate sliver.
        CHECK(a != b);
        CHECK(b != c);
        CHECK(c != a);
    }
}

TEST_CASE("mesher: every vertex lies within one cell of the true surface") {
    Ref s{new goop::Sphere(1.0)};
    const double half = 1.5;
    const int resolution = 32;
    const goop::Mesh m = mesh_of(s, half, resolution);
    const double cell = 2.0 * half / resolution;

    for (std::size_t i = 0; i < m.vertices.size(); ++i) {
        INFO("vertex " << i);
        CHECK(std::abs(s->eval(m.vertices[i])) <= cell);
    }
}

TEST_CASE("mesher: a sphere mesh is watertight and consistently oriented") {
    Ref s{new goop::Sphere(1.0)};
    const goop::Mesh m = mesh_of(s, 1.5, 32);

    CHECK(bad_edge_count(m) == 0);
}

TEST_CASE("mesher: triangles face outward, and the enclosed volume is right") {
    Ref s{new goop::Sphere(1.0)};
    const goop::Mesh m = mesh_of(s, 1.5, 48);

    const double expected = 4.0 / 3.0 * kPi; // volume of a unit sphere
    const double actual = signed_volume(m);

    CHECK(actual > 0.0); // positive = outward winding
    // Averaged vertices shave the surface slightly, so allow a few percent.
    CHECK_THAT(actual, WithinAbs(expected, 0.05 * expected));
}

TEST_CASE("mesher: two melted spheres make one closed blob, bigger than the two apart") {
    Ref base{new goop::Sphere(1.0)};
    Ref left{new goop::Translate(base.get(), goop::Vec3{-1.2, 0, 0})};
    Ref right{new goop::Translate(base.get(), goop::Vec3{1.2, 0, 0})};
    Ref plain{new goop::Union(left.get(), right.get())};
    Ref blob{new goop::SmoothUnion(left.get(), right.get(), 1.0)};

    const goop::Mesh plainMesh = mesh_of(plain, 2.6, 64);
    const goop::Mesh blobMesh = mesh_of(blob, 2.6, 64);

    // The neck is extra surface, but the blob must still be one closed skin.
    CHECK(bad_edge_count(blobMesh) == 0);
    // Filling the gap adds material, so the blob encloses MORE than the two
    // separate spheres do.
    CHECK(signed_volume(blobMesh) > signed_volume(plainMesh));
}

TEST_CASE("mesher: bounds that miss the shape give an empty mesh, not an error") {
    Ref s{new goop::Sphere(1.0)};
    goop::Mesh m;
    const auto result = goop::mesh_surface_nets(
        *s.get(), goop::Vec3{10, 10, 10}, goop::Vec3{12, 12, 12}, 16, nullptr, m);

    CHECK(result == goop::MeshResult::Completed);
    CHECK(m.vertices.empty());
    CHECK(m.indices.empty());
}

TEST_CASE("mesher: rejects a bad resolution or bad bounds") {
    Ref s{new goop::Sphere(1.0)};
    goop::Mesh m;
    const goop::Vec3 lo{-1, -1, -1}, hi{1, 1, 1};
    const double nan = std::nan("");

    CHECK_THROWS_AS(goop::mesh_surface_nets(*s.get(), lo, hi, 1, nullptr, m),
                    std::invalid_argument);
    CHECK_THROWS_AS(goop::mesh_surface_nets(*s.get(), lo, hi, 513, nullptr, m),
                    std::invalid_argument);
    CHECK_THROWS_AS(goop::mesh_surface_nets(*s.get(), hi, lo, 16, nullptr, m),
                    std::invalid_argument);
    CHECK_THROWS_AS(goop::mesh_surface_nets(*s.get(), lo, goop::Vec3{1, 1, -1}, 16, nullptr, m),
                    std::invalid_argument);
    CHECK_THROWS_AS(goop::mesh_surface_nets(*s.get(), goop::Vec3{nan, -1, -1}, hi, 16, nullptr, m),
                    std::invalid_argument);
}

TEST_CASE("mesher: progress only ever increases and ends at exactly 1") {
    Ref s{new goop::Sphere(1.0)};
    std::vector<double> reports;
    goop::Mesh m;

    const auto result = goop::mesh_surface_nets(
        *s.get(),
        goop::Vec3{-1.5, -1.5, -1.5},
        goop::Vec3{1.5, 1.5, 1.5},
        16,
        [&reports](double f) {
            reports.push_back(f);
            return true;
        },
        m);

    REQUIRE(result == goop::MeshResult::Completed);
    REQUIRE(reports.size() > 2);
    for (std::size_t i = 1; i < reports.size(); ++i) {
        INFO("report " << i);
        CHECK(reports[i] > reports[i - 1]);
    }
    CHECK(reports.back() == 1.0);
}

TEST_CASE("mesher: returning false from progress cancels and leaves the mesh empty") {
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
            return false; // cancel at the very first report
        },
        m);

    CHECK(result == goop::MeshResult::Cancelled);
    CHECK(calls == 1); // it stopped promptly, not after doing all the work
    CHECK(m.vertices.empty());
    CHECK(m.indices.empty());
}
