// ===========================================================================
// shape.hpp - the SDF node graph. Private to the core; never seen by consumers.
//
// A "shape" here is a node in a directed acyclic graph that can answer one
// question: given a point in space, how far is it to my surface? Negative
// inside, zero on the surface, positive outside.
//
// Everything in Goop is built from three kinds of node:
//   * primitives  - a sphere, box, torus or cylinder, evaluated analytically
//   * combinators - CSG union/subtract/intersect, plus the smooth variants
//   * transforms  - translate/rotate/scale/twist, which warp the query point
//                   on the way in rather than moving any geometry
//
// The graph is a DAG rather than a tree because a subexpression can be reused
// in more than one place, which is why nodes are reference counted (see the
// retain/release pair in goop.h) instead of deep copied.
//
// This header is C++ and may use anything C++17 offers. The ABI restrictions
// documented in goop.h stop at api.cpp.
// ===========================================================================

#ifndef GOOP_SHAPE_HPP
#define GOOP_SHAPE_HPP

#include <atomic>

namespace goop {

// TODO: Vec3 operations the distance functions actually need: +, -, scalar *,
//       dot, length, abs, componentwise max/min. The three-double layout is
//       already here and must stay identical to goop_vec3.

// TODO: primitive nodes still to add: Box(half extents), Torus(major, minor),
//       Cylinder(radius, height). Sphere is already declared below.
//       Inigo Quilez's distance-function
//       articles are the reference for the formulae; note which of them give an
//       exact distance and which only a lower bound, because the mesher's
//       step-size assumptions depend on the difference.

// TODO: the remaining transform nodes, each a UnaryShape like Translate below:
//       Rotate applies the inverse rotation to the query point, Scale divides
//       in and multiplies the result out (uniform scale only, or the field
//       stops being a true distance), Twist rotates about an axis by an amount
//       proportional to the coordinate along it. Note that Twist is a
//       non-isometric warp, so its result is a distance BOUND, not an exact
//       distance - the mesher has to tolerate that.

// TODO: an axis-aligned bounding box query per node, so the mesher can pick a
//       grid that contains the surface instead of guessing.

struct Vec3 {
    double x, y, z;
};

// ---------------------------------------------------------------------------
// Shape - the base of every node. Owns the reference count.
// ---------------------------------------------------------------------------
class Shape {
  public:
    virtual ~Shape() = default;

    // = 0 means that the function is pure virtual and must be implemented by the derived class.
    virtual double eval(const Vec3& p) const = 0;

    void retain() noexcept {
        ++m_refCount;
    }

    void release() noexcept {
        if (--m_refCount == 0) {
            delete this;
        }
    }

    // NO COPYING, for any shape, ever.
    //
    // A shape is a heap object shared by POINTER, with a reference count that
    // says how many owners it has. Copying one makes no sense: the copy would
    // start with its own count, and for combinators it would copy the child
    // pointers WITHOUT calling retain() on them. Then two destructors would each
    // release children that were only retained once - a double free.
    //
    // Declaring these here, on the base, makes EVERY derived class non-copyable
    // too: a derived class cannot be copied if its base cannot. So BinaryShape,
    // Union, Sphere etc. need no deletes of their own.
    //
    // (Strictly, std::atomic<int> below already makes Shape non-copyable, since
    // std::atomic itself deletes its copy operations. Relying on that would mean
    // the rule silently disappears the day someone changes the refcount type, so
    // it is stated explicitly.)
    //
    // Deleting the copy operations also suppresses the implicit MOVE operations,
    // so moving is forbidden too - which is what we want. Nodes never move; they
    // are created with new and destroyed by release().
    Shape(const Shape&) = delete;
    Shape& operator=(const Shape&) = delete;

  protected:
    // Protected: only derived classes construct a Shape.
    Shape() = default;

  private:
    // std::atomic is used to ensure that the reference count is updated atomically i.e
    // when multiple threads are accessing the same Shape object. Concretely: the
    // .NET finalizer thread can call release() while a test thread calls retain().
    std::atomic<int> m_refCount{1}; // born with one reference, owned by the creator
};

// ---------------------------------------------------------------------------
// Primitives
//
// Every eval() body lives in shape.cpp. They are virtual and called through a
// Shape*, so the compiler could not inline them from here anyway; keeping them
// out of the header keeps this file a list of WHAT each shape is, and means a
// formula change only recompiles shape.cpp.
// ---------------------------------------------------------------------------
class Sphere final : public Shape {
  public:
    // explicit is used to prevent implicit conversion from double to Sphere.
    explicit Sphere(double radius) : m_radius(radius) {}

    double eval(const Vec3& p) const override;

  private:
    double m_radius;
};

// ---------------------------------------------------------------------------
// BinaryShape - base for every node with two children (all the CSG ops).
//
// Ownership rule, and the reason this class exists: a combinator holds ONE
// reference to each child for as long as it lives. It takes that reference in
// the constructor (retain) and gives it back in the destructor (release). This
// is RAII applied to the reference count - the object's lifetime IS its
// ownership, so the retain/release pair can never be forgotten or unbalanced.
//
// Copying is already forbidden by Shape, see above. That is exactly what keeps
// this class correct: a copy would duplicate m_a/m_b without retaining them.
// ---------------------------------------------------------------------------
class BinaryShape : public Shape {
  protected:
    // Protected: BinaryShape is not a real shape on its own (it has no eval),
    // only a base for Union, Intersect, etc.
    BinaryShape(Shape* a, Shape* b) : m_a(a), m_b(b) {
        m_a->retain();
        m_b->retain();
    }

    ~BinaryShape() override {
        m_a->release();
        m_b->release();
    }

    // Protected, not private: the derived classes' eval() must read them.
    Shape* m_a;
    Shape* m_b;
};

// ---------------------------------------------------------------------------
// CSG combinators. Inside is negative, so:
//   union     - inside EITHER   -> min(a, b)
//   intersect - inside BOTH     -> max(a, b)
//   subtract  - inside a, NOT b -> max(a, -b)   (-b flips b's inside/outside)
// ---------------------------------------------------------------------------

// "public BinaryShape", not just "BinaryShape": with the class keyword the
// default is PRIVATE inheritance, which would hide the fact that a Union IS a
// Shape. api.cpp could then not convert a Union* to a Shape* to hand it out.
class Union final : public BinaryShape {
  public:
    Union(Shape* a, Shape* b) : BinaryShape(a, b) {}

    double eval(const Vec3& p) const override;
};

class Intersect final : public BinaryShape {
  public:
    Intersect(Shape* a, Shape* b) : BinaryShape(a, b) {}

    double eval(const Vec3& p) const override;
};

class Subtract final : public BinaryShape {
  public:
    // a minus b.
    Subtract(Shape* a, Shape* b) : BinaryShape(a, b) {}

    double eval(const Vec3& p) const override;
};

// Polynomial smooth minimum. See SmoothUnion::eval in shape.cpp.
// k must be > 0 (it is a divisor). api.cpp validates that before constructing.
class SmoothUnion final : public BinaryShape {
  public:
    SmoothUnion(Shape* a, Shape* b, double k) : BinaryShape(a, b), m_k(k) {}

    double eval(const Vec3& p) const override;

  private:
    double m_k;
};

// ---------------------------------------------------------------------------
// UnaryShape - base for every node with ONE child (all the transforms).
//
// Exactly the same ownership rule as BinaryShape, for one child instead of
// two: retain in the constructor, release in the destructor. Copying is
// forbidden by Shape, which keeps this correct.
// ---------------------------------------------------------------------------
class UnaryShape : public Shape {
  protected:
    explicit UnaryShape(Shape* child) : m_child(child) {
        m_child->retain();
    }

    ~UnaryShape() override {
        m_child->release();
    }

    Shape* m_child;
};

// ---------------------------------------------------------------------------
// Transforms. They never move geometry. They move the QUESTION: to ask how far
// p is from a shape shifted by some offset, ask the ORIGINAL shape about the
// point shifted the opposite way. See Translate::eval in shape.cpp.
// ---------------------------------------------------------------------------

class Translate final : public UnaryShape {
  public:
    Translate(Shape* child, const Vec3& offset) : UnaryShape(child), m_offset(offset) {}

    double eval(const Vec3& p) const override;

  private:
    Vec3 m_offset;
};

} // namespace goop

#endif // GOOP_SHAPE_HPP
