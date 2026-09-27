# ---------------------------------------------------------------------------
# Goop - top-level build
#
# This file deliberately does almost nothing except set repo-wide conventions
# and delegate to native/. Everything C++ lives under native/.
#
# The Visual Studio generator is NOT hardcoded here: CMake picks the newest
# installed Visual Studio by itself, and scripts/build.ps1 only passes the
# architecture (-A x64). That keeps the tree working on VS 2022 and later.
# ---------------------------------------------------------------------------
cmake_minimum_required(VERSION 3.20)

project(goop LANGUAGES CXX)

option(GOOP_BUILD_TESTS "Build the C++ core unit tests (Catch2 v3)" ON)

# All binaries land in one predictable place so that the .NET side can copy the
# native DLL with a single well-known path. Multi-config generators (Visual
# Studio) append the configuration name automatically, so the DLL ends up at:
#
#     build/bin/<Config>/goop_native.dll     e.g. build/bin/Debug/goop_native.dll
#
# dotnet/Directory.Build.props points GoopNativeBinDir at exactly that path.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

if(GOOP_BUILD_TESTS)
    enable_testing()
endif()

add_subdirectory(native)
