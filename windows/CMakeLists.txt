# BiSpell Windows tree — top-level CMake
#
# Targets:
#   bispell_core        — portable C++17 spell library (Linux + MSVC)
#   bispell_core_tests  — aggregate of core unit tests
#   test_*              — individual ctest executables
#   app/                — WinUI 3 shell (WIN32 only; later units)
#
# Example:
#   cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build windows/build --target bispell_core_tests
#   cd windows/build && ctest --output-on-failure

cmake_minimum_required(VERSION 3.20)
project(BiSpellWindows LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Export compile commands for tooling when available.
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

enable_testing()

add_subdirectory(core)
add_subdirectory(tests)

# WinUI shell is Windows-only and arrives in later units.
if(WIN32)
    # add_subdirectory(app)
endif()

message(STATUS "BiSpell Windows: bispell_core + tests configured")
