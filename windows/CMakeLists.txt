# BiSpell Windows tree — top-level CMake (scaffold)
#
# Targets (added in later units):
#   bispell_core        — portable C++17 spell library (Linux + MSVC)
#   bispell_core_tests  — ctest-friendly unit tests
#   app/                — WinUI 3 shell (WIN32 only; guarded later)
#
# Example (core + tests once implemented):
#   cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build windows/build --target bispell_core_tests
#   cd windows/build && ctest --output-on-failure

cmake_minimum_required(VERSION 3.20)
project(BiSpellWindows LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Placeholder: subdirectories arrive with core/tests/app implementations.
# add_subdirectory(core)
# add_subdirectory(tests)
# if(WIN32)
#   add_subdirectory(app)
# endif()

message(STATUS "BiSpell Windows scaffold: add core/tests/app when those units land.")
