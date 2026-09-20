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
// A useful shape for this, once more functions appear, is one private
// helper that takes a lambda, runs it inside the try/catch, and returns the
// status - so the error handling gets written once instead of per function.
// ===========================================================================

#include <goop/goop.h>

#include "mesh.hpp"
#include "shape.hpp"

#include <exception>
#include <new>
#include <stdexcept>
#include <string>

namespace {

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

} // namespace

extern "C" GOOP_API int32_t goop_get_version(void) {
    return GOOP_ABI_VERSION;
}

static_assert(sizeof(goop_vec3) == 24, "goop_vec3 is not 24 bytes");

extern "C" GOOP_API int32_t goop_vec3_size(void) {
    return (int32_t)sizeof(goop_vec3);
}

extern "C" GOOP_API int32_t goop_shape_sphere(double radius, goop_shape** out_shape) {
    if (out_shape == nullptr) {
        set_last_error("out_shape must not be null");
        return GOOP_ERROR_INVALID_ARGUMENT;
    }

    // Clear first, so a failed call can never leave a stale handle behind.
    *out_shape = nullptr;

    return guard([&]() -> int32_t {
        // also rejects NaN
        if (!(radius > 0.0)) {
            throw std::invalid_argument("radius must be positive");
        }
        *out_shape = reinterpret_cast<goop_shape*>(new goop::Sphere(radius));
        return GOOP_OK;
    });
}

extern "C" GOOP_API void goop_shape_release(goop_shape* shape) {
    if (shape == nullptr) {
        return;
    }
    reinterpret_cast<goop::Shape*>(shape)->release();
}

extern "C" GOOP_API const char* goop_last_error_message(void) {
    // Never NULL, as documented in goop.h: empty string if nothing has failed.
    return tls_lastError.c_str();
}
