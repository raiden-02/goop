# ---------------------------------------------------------------------------
# Goop native build - three targets, and the split between them is the point.
#
#   goop_core        STATIC  the real C++17 implementation (SDF graph, mesher)
#   goop             SHARED  goop.dll - nothing but the extern "C" ABI shim
#   goop_core_tests  EXE     Catch2 tests that link the static core directly
#
# WHY IS THE CORE A SEPARATE STATIC LIBRARY?
#
#   Because the two things we want to test are tested from two different
#   languages, and each needs a different kind of access.
#
#   * The C++ core is tested from C++. Catch2 wants to poke at real C++ types -
#     goop::Shape nodes, distance evaluation, mesher internals - including
#     things that are never exposed across the ABI. Linking goop_core straight
#     into the test executable gives the tests full access to internal headers
#     with no export macros, no handles, and no marshalling in the way.
#
#   * The C ABI is tested from C#. The only honest test of "does this DLL work
#     as a stable C interface" is to load it the way a real consumer does:
#     P/Invoke from Goop.Tests. Testing the ABI from C++ would prove nothing,
#     because C++ can cheat.
#
#   Keeping them apart also means api.cpp stays tiny and auditable: it is the
#   ONLY file allowed to contain extern "C", exception-to-status-code
#   translation, and handle lifetime management. If a symbol is exported from
#   goop.dll, it was written in api.cpp. Nothing else can leak out, which is
#   enforced by CXX_VISIBILITY_PRESET hidden plus the generated GOOP_API macro.
#
# MSVC RUNTIME: we intentionally use the DEFAULT dynamic CRT (/MD, /MDd) rather
# than static linking. A DLL that hands out heap-allocated objects and a .NET
# process that loads it must agree on one CRT instance; the dynamic runtime is
# also what every other .NET-adjacent native dependency assumes. No
# MSVC_RUNTIME_LIBRARY override is set anywhere on purpose.
# ---------------------------------------------------------------------------

# Warnings are ON and LOUD but never fatal: a warning you can read and triage
# is more useful than a build that refuses to finish.
if(MSVC)
    # /permissive- turns off MSVC's non-conforming extensions, /utf-8 makes
    # both source and execution charsets UTF-8 regardless of system codepage.
    set(GOOP_WARNING_FLAGS /W4 /permissive- /utf-8)
else()
    set(GOOP_WARNING_FLAGS -Wall -Wextra -Wpedantic)
endif()

# ---------------------------------------------------------------------------
# 1. goop_core - private C++17 implementation
# ---------------------------------------------------------------------------
add_library(goop_core STATIC
    src/shape.cpp
    src/mesher.cpp
)

# src/ is PRIVATE: these headers are implementation detail. Nothing outside
# this directory (and the test target, which opts in explicitly) may see them.
target_include_directories(goop_core PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")

set_target_properties(goop_core PROPERTIES
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
    CXX_EXTENSIONS OFF
    POSITION_INDEPENDENT_CODE ON
)

target_compile_options(goop_core PRIVATE ${GOOP_WARNING_FLAGS})

# ---------------------------------------------------------------------------
# 2. goop - the shared library / C ABI surface (goop.dll)
# ---------------------------------------------------------------------------
add_library(goop SHARED
    src/api.cpp
)

target_link_libraries(goop PRIVATE goop_core)

target_include_directories(goop
    PUBLIC
        # The public C header: #include <goop/goop.h>
        "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
        # The generated export header: #include "goop_export.h"
        "$<BUILD_INTERFACE:${CMAKE_CURRENT_BINARY_DIR}/generated/goop>"
    PRIVATE
        # api.cpp needs the private core headers to do its translation work.
        "${CMAKE_CURRENT_SOURCE_DIR}/src"
)

set_target_properties(goop PROPERTIES
    OUTPUT_NAME "goop_native"            # -> goop.dll (no lib prefix on Windows)
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
    CXX_EXTENSIONS OFF
    POSITION_INDEPENDENT_CODE ON
    # Everything is hidden unless explicitly tagged GOOP_API. On MSVC this is a
    # no-op (nothing is exported by default anyway); on gcc/clang it is what
    # makes the export list match the header instead of dumping every symbol.
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
)

target_compile_options(goop PRIVATE ${GOOP_WARNING_FLAGS})

# Generates goop_export.h defining GOOP_API as dllexport while building this
# target (CMake defines goop_EXPORTS for us) and dllimport for consumers.
include(GenerateExportHeader)
generate_export_header(goop
    BASE_NAME GOOP
    EXPORT_MACRO_NAME GOOP_API
    EXPORT_FILE_NAME "${CMAKE_CURRENT_BINARY_DIR}/generated/goop/goop_export.h"
)

# ---------------------------------------------------------------------------
# 3. goop_core_tests - Catch2 v3 over the static core
# ---------------------------------------------------------------------------
if(GOOP_BUILD_TESTS)
    include(FetchContent)

    # Pinned to a concrete release TAG, never a branch: a moving dependency
    # turns "my code broke" and "my dependency moved" into the same symptom.
    FetchContent_Declare(Catch2
        GIT_REPOSITORY https://github.com/catchorg/Catch2.git
        GIT_TAG        v3.9.1
        GIT_SHALLOW    TRUE
    )
    FetchContent_MakeAvailable(Catch2)

    add_executable(goop_core_tests
        tests/test_core.cpp
    )

    # Tests link the STATIC core, not goop.dll - see the comment at the top.
    target_link_libraries(goop_core_tests PRIVATE
        goop_core
        Catch2::Catch2WithMain
    )

    target_include_directories(goop_core_tests PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src"
    )

    set_target_properties(goop_core_tests PROPERTIES
        CXX_STANDARD 17
        CXX_STANDARD_REQUIRED ON
        CXX_EXTENSIONS OFF
    )

    target_compile_options(goop_core_tests PRIVATE ${GOOP_WARNING_FLAGS})

    # Catch.cmake ships in Catch2's extras/ and gives us catch_discover_tests,
    # which registers each TEST_CASE with CTest individually.
    list(APPEND CMAKE_MODULE_PATH "${catch2_SOURCE_DIR}/extras")
    include(Catch)
    catch_discover_tests(goop_core_tests)
endif()
