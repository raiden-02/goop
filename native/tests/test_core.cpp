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

#include "shape.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

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

// TODO: transform correctness (M-later). Translating a sphere by t and evaluating
//       at p + t must equal evaluating the original at p. Rotation must not
//       change the distance at the origin. Uniform scale by s must scale
//       distances by s. Twist yields a distance BOUND, so only assert that it
//       never overestimates.

// TODO: mesher output (M4). For a single sphere at a known resolution: the
//       triangle count is nonzero, indices.size() % 3 == 0, every index is in
//       range, no triangle has two identical vertices, and every vertex sits
//       within about one cell width of the true surface. Then: is the result
//       watertight, and does a twisted or thin shape break that? The C#
//       MeshOracle asserts the same properties from the other side of the ABI.

// TODO: cancellation (M6). A progress callback that returns nonzero on its first
//       call must abort the mesh promptly and leak nothing.
