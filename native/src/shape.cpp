// ===========================================================================
// shape.cpp - implementation of the SDF node graph declared in shape.hpp.
//
// Nothing here is exported from goop.dll. From outside the core, the only file
// that knows this code exists is api.cpp.
// ===========================================================================

#include "shape.hpp"

#include <algorithm>
#include <cmath>

namespace goop {

// TODO: Vec3 operations - or keep them inline in the header if they turn out to
//       be small enough that call overhead dominates the arithmetic.

// TODO: primitive distance functions still to add: box, torus, cylinder.
//       Sphere::eval is below. Same sign rule as the sphere tests: negative
//       inside, zero on the surface, positive outside.

// TODO: transform nodes - the point warp on the way in, and whatever
//       correction the returned distance needs on the way out.

// TODO: recursive teardown. ~BinaryShape releases its children, which may
//       release theirs, and so on: a very deeply nested expression can overflow
//       the stack. Either bound the nesting or drive teardown from an explicit
//       worklist instead of the call stack.

// TODO: bounding box propagation up through combinators and transforms.

// ---------------------------------------------------------------------------
// Primitives
// ---------------------------------------------------------------------------

double Sphere::eval(const Vec3& p) const {
    return std::sqrt(p.x * p.x + p.y * p.y + p.z * p.z) - m_radius;
}

// ---------------------------------------------------------------------------
// CSG combinators. Inside is negative, so:
//   union     - inside EITHER   -> min(a, b)
//   intersect - inside BOTH     -> max(a, b)
//   subtract  - inside a, NOT b -> max(a, -b)   (-b flips b's inside/outside)
// ---------------------------------------------------------------------------

double Union::eval(const Vec3& p) const {
    return std::min(m_a->eval(p), m_b->eval(p));
}

double Intersect::eval(const Vec3& p) const {
    return std::max(m_a->eval(p), m_b->eval(p));
}

double Subtract::eval(const Vec3& p) const {
    return std::max(m_a->eval(p), -m_b->eval(p));
}

// Polynomial smooth minimum (Inigo Quilez). Within distance k of the crease
// where da == db, the two fields are blended and a small bump is subtracted, so
// the surfaces bulge into each other. Outside that band (|da - db| >= k) h is
// clamped to 0 or 1 and the result is EXACTLY min(da, db).
double SmoothUnion::eval(const Vec3& p) const {
    const double da = m_a->eval(p);
    const double db = m_b->eval(p);
    const double h = std::clamp(0.5 + 0.5 * (db - da) / m_k, 0.0, 1.0);
    const double mixed = db + (da - db) * h; // mix(db, da, h)
    return mixed - m_k * h * (1.0 - h);
}

} // namespace goop
