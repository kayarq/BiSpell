# BiSpell Windows tree — top-level CMake
#
# Always configured (Linux + Windows + macOS hosts with a C++17 toolchain):
#   bispell_core         — STATIC portable C++17 spell library
#   bispell_core_shared  — SHARED optional (BISPELL_BUILD_SHARED; default ON)
#   bispell_core_tests   — aggregate of core unit tests
#   test_*               — individual ctest executables
#
# NOT built by CMake (documented only):
#   windows/app/         — C# WinUI 3 shell (BiSpell.sln / dotnet). If a future
#                          native C++/WinRT app subdirectory is added, gate it
#                          with if(WIN32) here; the C# app stays MSBuild/dotnet.
#
# Dictionaries: SoT is Sources/BiSpellCore/Resources/Dictionaries/ (see tests/
# CMakeLists and docs/WINDOWS_PHASES.md). Do not hand-edit a second master copy.
#
# Example (clean clone, Linux):
#   cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build windows/build --target bispell_core_tests
#   cd windows/build && ctest --output-on-failure
#
# CI sketch: .github/workflows/windows-core.yml (Linux core tests only).

cmake_minimum_required(VERSION 3.20)
project(BiSpellWindows LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Export compile commands for tooling when available.
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

enable_testing()

# Core library + unit tests: always (portable; no WinUI deps).
add_subdirectory(core)
add_subdirectory(tests)

# ---------------------------------------------------------------------------
# App shell
# ---------------------------------------------------------------------------
# The product UI is C# WinUI 3 under windows/app/ (not a CMake subdirectory).
# Build the native DLL for P/Invoke, then open BiSpell.sln / use dotnet:
#   cmake --build <build> --target bispell_core_shared
#   windows/app/scripts/build-native.ps1   (Windows Developer PowerShell)
# See windows/README.md and docs/WINDOWS_PHASES.md.
if(WIN32)
    message(STATUS
        "BiSpell Windows: C# WinUI app is under windows/app "
        "(BiSpell.sln / dotnet — not added via add_subdirectory)")
    # Placeholder for a future native UI target only, e.g.:
    #   if(BISPELL_BUILD_NATIVE_APP)
    #     add_subdirectory(app/native)   # would be WIN32-only
    #   endif()
endif()

message(STATUS
    "BiSpell Windows: bispell_core + tests configured "
    "(shared: BISPELL_BUILD_SHARED=${BISPELL_BUILD_SHARED})")
