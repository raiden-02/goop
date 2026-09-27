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

// TODO: recursive teardown. ~BinaryShape and ~UnaryShape release their children, which may
//       release theirs, and so on: a very deeply nested expression can overflow
//       the stack. Either bound the nesting or drive teardown from an explicit
//       worklist instead of the call stack.

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

// ---------------------------------------------------------------------------
// Transforms
// ---------------------------------------------------------------------------

// Move the question, not the shape. A sphere shifted +3 along x is, at point p,
// exactly as far away as the ORIGINAL sphere is from p shifted -3 along x.
//
// Translation is an isometry (it preserves distances), so the result is still
// an exact distance field - no correction is needed on the way out. Scale will
// be the first transform that needs one.
double Translate::eval(const Vec3& p) const {
    return m_child->eval(Vec3{p.x - m_offset.x, p.y - m_offset.y, p.z - m_offset.z});
}

} // namespace goop
