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

namespace goop {

// TODO: a Vec3 value type - three doubles, with the handful of operations the
//       distance functions actually need: +, -, scalar *, dot, length, abs,
//       componentwise max/min. Layout must match goop_vec3 exactly so api.cpp
//       can reinterpret rather than convert.

// TODO: the node base type. Something like an abstract Shape with a virtual
//       double eval(const Vec3& p) const, plus an intrusive reference count.
//       Decide (and write down in docs/design.md) whether that refcount is
//       plain or atomic - that decision is really about the threading model.

// TODO: primitive nodes: Sphere(radius), Box(half extents), Torus(major,
//       minor), Cylinder(radius, height). Inigo Quilez's distance-function
//       articles are the reference for the formulae; note which of them give an
//       exact distance and which only a lower bound, because the mesher's
//       step-size assumptions depend on the difference.

// TODO: combinator nodes: Union = min(a, b), Intersect = max(a, b),
//       Subtract = max(a, -b), and SmoothUnion with a blend radius k. The
//       smooth one is the whole point of the project: a polynomial blend (the
//       usual quadratic h = clamp(0.5 + 0.5*(b-a)/k, 0, 1) form) that stays
//       C1-continuous where min() has a crease.

// TODO: transform nodes. Each stores a child and warps the query point:
//       Translate subtracts an offset, Rotate applies the inverse rotation,
//       Scale divides in and multiplies the result out (uniform scale only, or
//       the field stops being a true distance), Twist rotates about an axis by
//       an amount proportional to the coordinate along it. Note that Twist is
//       a non-isometric warp, so its result is a distance BOUND, not an exact
//       distance - the mesher has to tolerate that.

// TODO: an axis-aligned bounding box query per node, so the mesher can pick a
//       grid that contains the surface instead of guessing.

// TODO: the batch evaluation entry point behind goop_shape_eval_batch.

} // namespace goop

#endif // GOOP_SHAPE_HPP
