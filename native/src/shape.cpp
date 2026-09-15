// ===========================================================================
// shape.cpp - implementation of the SDF node graph declared in shape.hpp.
//
// Nothing here is exported from goop.dll. From outside the core, the only file
// that knows this code exists is api.cpp.
// ===========================================================================

#include "shape.hpp"

namespace goop {

// TODO: Vec3 operations - or keep them inline in the header if they turn out to
//       be small enough that call overhead dominates the arithmetic.

// TODO: primitive distance functions: sphere, box, torus, cylinder. Mind the
//       sign convention and test it - eval(centre) of a unit sphere must be
//       exactly -1, eval on the surface ~0, and outside strictly positive.

// TODO: CSG combinators, including the smooth_union polynomial blend.

// TODO: transform nodes - the point warp on the way in, and whatever
//       correction the returned distance needs on the way out.

// TODO: reference counting: retain/release plus recursive teardown. Mind the
//       depth: a deeply nested expression released recursively can overflow the
//       stack, so either bound the nesting or drive teardown from an explicit
//       worklist instead of the call stack.

// TODO: bounding box propagation up through combinators and transforms.

} // namespace goop
