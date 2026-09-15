// ===========================================================================
// api.cpp - the extern "C" layer. The only file in the project that is allowed
// to export anything from goop.dll.
//
// This file has exactly three jobs and no others:
//
//   1. TRANSLATE TYPES. Opaque goop_shape* / goop_mesh* handles into and out of
//      real goop::Shape / goop::Mesh objects, and goop_vec3 into and out of
//      goop::Vec3. The layouts are chosen to match so that this stays a cast
//      rather than a conversion.
//
//   2. SWALLOW EXCEPTIONS. Every exported function body is wrapped in
//      try { ... } catch (...) { ... } with nothing allowed to escape - an
//      exception unwinding into the CLR is undefined behaviour, not a stack
//      trace. Each catch clause maps onto a goop_status code: std::bad_alloc to
//      out-of-memory, std::invalid_argument to invalid-argument, and the
//      catch-all to an internal error. Argument validation lives here too: null
//      handles and nonsense counts are rejected before the core ever sees them.
//
//   3. REMEMBER WHY. On failure, store a human-readable message in
//      THREAD-LOCAL storage so that goop_last_error_message can return it.
//      Thread local rather than global, because two managed threads meshing two
//      shapes must not overwrite each other's diagnosis. The string is owned by
//      the DLL, stays valid until the next failing call on that thread, and is
//      never freed by the caller.
//
// A useful shape for this, once the functions start appearing, is one private
// helper that takes a lambda, runs it inside the try/catch, and returns the
// status - so the error handling gets written once instead of per function.
//
// TODO: everything. No functions are defined yet, so goop.dll currently exports
//       nothing at all. That is the correct state for milestone M0.
// ===========================================================================

#include <goop/goop.h>

#include "mesh.hpp"
#include "shape.hpp"

// TODO: a thread_local std::string for the last error message, plus a small
//       set_last_error(...) helper that writes into it.

// TODO: the try/catch wrapper helper described above.

// TODO: the extern "C" GOOP_API definitions mirroring goop.h, one milestone at
//       a time. Start with goop_get_version - it is the cheapest possible proof
//       that P/Invoke is finding the DLL at all.
