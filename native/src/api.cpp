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
// Layout of this file:
//   * anonymous namespace - INTERNAL helpers. No extern "C", no GOOP_API. They
//     have internal linkage, so they are invisible outside this file and never
//     appear in the DLL's export table.
//   * everything after it - the EXPORTED ABI. Every function there is
//     extern "C" GOOP_API and declared in goop.h. Nothing else is exported.
//
// Check with: dumpbin /exports goop_native.dll - the list must match goop.h.
// ===========================================================================

#include <goop/goop.h>

#include "mesh.hpp"
#include "shape.hpp"

#include <exception>
#include <limits>
#include <new>
#include <stdexcept>
#include <string>

namespace {

// ---------------------------------------------------------------------------
// Error reporting
// ---------------------------------------------------------------------------

// Last failure message for the CALLING thread. thread_local so two threads
// failing at once never overwrite each other's diagnosis.
thread_local std::string tls_lastError;

void set_last_error(const char* message) {
    tls_lastError = message ? message : "";
}

// Runs body() and turns any C++ exception into a goop_status plus a message.
// Every fallible export wraps its work in this, so exception handling is
// written once. noexcept: if set_last_error itself throws inside a catch
// block, the process terminates here rather than unwinding into the caller.
template <typename Body> int32_t guard(Body&& body) noexcept {
    try {
        return body();
    } catch (const std::invalid_argument& e) {
        set_last_error(e.what());
        return GOOP_ERROR_INVALID_ARGUMENT;
    } catch (const std::bad_alloc&) {
        set_last_error("out of memory");
        return GOOP_ERROR_OUT_OF_MEMORY;
    } catch (const std::exception& e) {
        set_last_error(e.what());
        return GOOP_ERROR_INTERNAL;
    } catch (...) {
        set_last_error("unknown error");
        return GOOP_ERROR_INTERNAL;
    }
}

// ---------------------------------------------------------------------------
// Handle <-> object conversion
//
// Every goop_shape* that ever leaves this file was produced by as_handle() from
// a real goop::Shape*. That invariant is what makes these casts safe, and
// keeping them in this one pair means a search for reinterpret_cast finds
// exactly two lines.
//
// No null check: reinterpret_cast of nullptr is already nullptr. Null checks
// belong in the exported functions, where they can set an error message.
// ---------------------------------------------------------------------------

goop::Shape* as_shape(goop_shape* h) noexcept {
    return reinterpret_cast<goop::Shape*>(h);
}

goop_shape* as_handle(goop::Shape* s) noexcept {
    return reinterpret_cast<goop_shape*>(s);
}

// ---------------------------------------------------------------------------
// Shared argument checks for the CSG operators
// ---------------------------------------------------------------------------

// Validates out_shape and clears it, then validates both operands. Returns
// GOOP_OK if the caller may go ahead, or the status to return if not.
int32_t check_binary_args(goop_shape* a, goop_shape* b, goop_shape** out_shape) {
    if (out_shape == nullptr) {
        set_last_error("out_shape must not be null");
        return GOOP_ERROR_INVALID_ARGUMENT;
    }

    // Clear first, so a failed call can never leave a stale handle behind.
    *out_shape = nullptr;

    if (a == nullptr || b == nullptr) {
        set_last_error("operand must not be null");
        return GOOP_ERROR_NULL_HANDLE;
    }

    return GOOP_OK;
}

// Builds any two-child node. Union, Intersect and Subtract differ ONLY in which
// class gets constructed, so that is the only thing each export specifies; the
// template stamps out one copy of this function per Node type.
//
// Ownership: the Node's constructor (BinaryShape) retains a and b. The new node
// starts with a refcount of 1, which is the reference handed to the caller.
template <typename Node> int32_t make_binary(goop_shape* a, goop_shape* b, goop_shape** out_shape) {
    const int32_t status = check_binary_args(a, b, out_shape);
    if (status != GOOP_OK) {
        return status;
    }

    return guard([&]() -> int32_t {
        *out_shape = as_handle(new Node(as_shape(a), as_shape(b)));
        return GOOP_OK;
    });
}

} // namespace

// ===========================================================================
// EXPORTED ABI - everything below is extern "C" GOOP_API and declared in goop.h
// ===========================================================================

// ---------------------------------------------------------------------------
// Version and layout
// ---------------------------------------------------------------------------

extern "C" GOOP_API int32_t goop_get_version(void) {
    return GOOP_ABI_VERSION;
}

static_assert(sizeof(goop_vec3) == 24, "goop_vec3 is not 24 bytes");
static_assert(sizeof(goop::Vec3) == sizeof(goop_vec3), "goop::Vec3 and goop_vec3 must match");

extern "C" GOOP_API int32_t goop_vec3_size(void) {
    return (int32_t)sizeof(goop_vec3);
}

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

extern "C" GOOP_API const char* goop_last_error_message(void) {
    // Never NULL, as documented in goop.h: empty string if nothing has failed.
    return tls_lastError.c_str();
}

// ---------------------------------------------------------------------------
// Shape lifetime
// ---------------------------------------------------------------------------

extern "C" GOOP_API int32_t goop_shape_sphere(double radius, goop_shape** out_shape) {
    if (out_shape == nullptr) {
        set_last_error("out_shape must not be null");
        return GOOP_ERROR_INVALID_ARGUMENT;
    }

    // Clear first, so a failed call can never leave a stale handle behind.
    *out_shape = nullptr;

    return guard([&]() -> int32_t {
        // !(x > 0) rather than (x <= 0), so NaN is rejected too.
        if (!(radius > 0.0)) {
            throw std::invalid_argument("radius must be positive");
        }
        *out_shape = as_handle(new goop::Sphere(radius));
        return GOOP_OK;
    });
}

extern "C" GOOP_API void goop_shape_retain(goop_shape* shape) {
    if (shape == nullptr) {
        return;
    }
    as_shape(shape)->retain();
}

extern "C" GOOP_API void goop_shape_release(goop_shape* shape) {
    if (shape == nullptr) {
        return;
    }
    as_shape(shape)->release();
}

// ---------------------------------------------------------------------------
// CSG operators
// ---------------------------------------------------------------------------

extern "C" GOOP_API int32_t goop_shape_union(goop_shape* a, goop_shape* b, goop_shape** out_shape) {
    return make_binary<goop::Union>(a, b, out_shape);
}

extern "C" GOOP_API int32_t goop_shape_intersect(goop_shape* a,
                                                 goop_shape* b,
                                                 goop_shape** out_shape) {
    return make_binary<goop::Intersect>(a, b, out_shape);
}

extern "C" GOOP_API int32_t goop_shape_subtract(goop_shape* a,
                                                goop_shape* b,
                                                goop_shape** out_shape) {
    return make_binary<goop::Subtract>(a, b, out_shape);
}

// Not built with make_binary: it takes the extra k, and k needs validating.
// Same steps otherwise: check args, clear out_shape, then build inside guard.
extern "C" GOOP_API int32_t goop_shape_smooth_union(goop_shape* a,
                                                    goop_shape* b,
                                                    double k,
                                                    goop_shape** out_shape) {
    const int32_t status = check_binary_args(a, b, out_shape);
    if (status != GOOP_OK) {
        return status;
    }

    return guard([&]() -> int32_t {
        // k is a divisor in the blend; !(k > 0) also rejects NaN.
        if (!(k > 0.0)) {
            throw std::invalid_argument("k must be positive");
        }
        *out_shape = as_handle(new goop::SmoothUnion(as_shape(a), as_shape(b), k));
        return GOOP_OK;
    });
}

// ---------------------------------------------------------------------------
// Evaluation
// ---------------------------------------------------------------------------

extern "C" GOOP_API int32_t goop_shape_eval(goop_shape* shape, goop_vec3 p, double* out_distance) {
    if (out_distance == nullptr) {
        set_last_error("out_distance must not be null");
        return GOOP_ERROR_INVALID_ARGUMENT;
    }

    // Clear to NaN, not 0: zero is a real answer ("on the surface"), so a
    // caller that ignores a failed status must not be able to mistake it for one.
    *out_distance = std::numeric_limits<double>::quiet_NaN();

    if (shape == nullptr) {
        set_last_error("shape must not be null");
        return GOOP_ERROR_NULL_HANDLE;
    }

    return guard([&]() -> int32_t {
        // goop_vec3 (C) and goop::Vec3 (C++) have the same layout but are
        // different types, so build one from the other field by field. The
        // compiler turns this into a plain 24-byte copy.
        *out_distance = as_shape(shape)->eval(goop::Vec3{p.x, p.y, p.z});
        return GOOP_OK;
    });
}
