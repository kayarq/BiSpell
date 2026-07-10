# Portable BiSpell C++17 core library (no WinRT / Win32 UI deps).

add_library(bispell_core STATIC
    src/encoding.cpp
    src/case_fold.cpp
    src/tokenizer.cpp
    src/dictionary.cpp
)

add_library(bispell::core ALIAS bispell_core)

target_include_directories(bispell_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_compile_features(bispell_core PUBLIC cxx_std_17)

if(MSVC)
    target_compile_options(bispell_core PRIVATE /W4 /permissive-)
else()
    target_compile_options(bispell_core PRIVATE -Wall -Wextra -Wpedantic)
endif()

# filesystem lib for older GCC/Clang (libstdc++fs)
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU" AND CMAKE_CXX_COMPILER_VERSION VERSION_LESS 9.1)
    target_link_libraries(bispell_core PUBLIC stdc++fs)
endif()
