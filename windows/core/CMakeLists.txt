# Portable BiSpell C++17 core library (no WinRT / Win32 UI deps).
#
# Targets:
#   bispell_core        — STATIC always; Linux/MSVC ctest and optional C++ hosts
#   bispell_core_shared — SHARED optional (BISPELL_BUILD_SHARED); DLL/.so for C# P/Invoke
#
# Shared is additive: enabling it must never replace the static lib or break
# bispell_add_test(...) which always links bispell_core (static).

set(BISPELL_CORE_SOURCES
    src/encoding.cpp
    src/case_fold.cpp
    src/tokenizer.cpp
    src/dictionary.cpp
    src/paths.cpp
    src/user_lexicon.cpp
    src/language_tagger.cpp
    src/spell_engine.cpp
    src/c_api.cpp
)

add_library(bispell_core STATIC ${BISPELL_CORE_SOURCES})
add_library(bispell::core ALIAS bispell_core)

target_include_directories(bispell_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_compile_features(bispell_core PUBLIC cxx_std_17)

if(MSVC)
    # /utf-8: source + execution charset UTF-8 (required for any non-ASCII literals)
    target_compile_options(bispell_core PRIVATE /W4 /permissive- /utf-8)
    target_compile_definitions(bispell_core PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
    target_compile_options(bispell_core PRIVATE -Wall -Wextra -Wpedantic)
endif()

# filesystem lib for older GCC/Clang (libstdc++fs)
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU" AND CMAKE_CXX_COMPILER_VERSION VERSION_LESS 9.1)
    target_link_libraries(bispell_core PUBLIC stdc++fs)
endif()

# pthread for std::mutex on some platforms (SpellResultCache)
find_package(Threads REQUIRED)
target_link_libraries(bispell_core PUBLIC Threads::Threads)

# ---------------------------------------------------------------------------
# Shared library (bispell_core.dll / libbispell_core.so) for P/Invoke hosts
# ---------------------------------------------------------------------------
option(BISPELL_BUILD_SHARED "Build shared bispell_core library for P/Invoke" ON)

if(BISPELL_BUILD_SHARED)
    add_library(bispell_core_shared SHARED ${BISPELL_CORE_SOURCES})
    add_library(bispell::core_shared ALIAS bispell_core_shared)

    target_include_directories(bispell_core_shared
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<INSTALL_INTERFACE:include>
    )
    target_compile_features(bispell_core_shared PUBLIC cxx_std_17)

    # Export C ABI symbols on Windows (see c_api.h BISPELL_DLL / BISPELL_BUILD).
    target_compile_definitions(bispell_core_shared
        PRIVATE BISPELL_BUILD
        PUBLIC  BISPELL_DLL
    )

    set_target_properties(bispell_core_shared PROPERTIES
        OUTPUT_NAME bispell_core
        WINDOWS_EXPORT_ALL_SYMBOLS OFF
        POSITION_INDEPENDENT_CODE ON
    )

    if(MSVC)
        target_compile_options(bispell_core_shared PRIVATE /W4 /permissive- /utf-8)
        target_compile_definitions(bispell_core_shared PRIVATE _CRT_SECURE_NO_WARNINGS)
        # Static CRT so end users do not need VCRUNTIME140.dll next to the app.
        set_property(TARGET bispell_core_shared PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    else()
        target_compile_options(bispell_core_shared PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU" AND CMAKE_CXX_COMPILER_VERSION VERSION_LESS 9.1)
        target_link_libraries(bispell_core_shared PUBLIC stdc++fs)
    endif()
    target_link_libraries(bispell_core_shared PUBLIC Threads::Threads)
endif()
