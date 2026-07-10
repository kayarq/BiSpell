# Core unit tests (Linux + Windows; no UI).

include(CTest)

set(BISPELL_DICT_SOURCE_DIR
    "${CMAKE_SOURCE_DIR}/../Sources/BiSpellCore/Resources/Dictionaries"
    CACHE PATH "Source of truth for .dic files")

# Stage dictionaries next to the test binary for hermetic runs.
set(BISPELL_DICT_STAGE_DIR "${CMAKE_CURRENT_BINARY_DIR}/Dictionaries")
file(MAKE_DIRECTORY "${BISPELL_DICT_STAGE_DIR}")

foreach(_dict en_US.dic en_US.aff tr.dic tr.aff)
    if(EXISTS "${BISPELL_DICT_SOURCE_DIR}/${_dict}")
        configure_file(
            "${BISPELL_DICT_SOURCE_DIR}/${_dict}"
            "${BISPELL_DICT_STAGE_DIR}/${_dict}"
            COPYONLY
        )
    endif()
endforeach()

# Also mirror into windows/assets/Dictionaries for packaging consumers.
set(BISPELL_ASSETS_DICT_DIR "${CMAKE_SOURCE_DIR}/assets/Dictionaries")
file(MAKE_DIRECTORY "${BISPELL_ASSETS_DICT_DIR}")
foreach(_dict en_US.dic tr.dic)
    if(EXISTS "${BISPELL_DICT_SOURCE_DIR}/${_dict}")
        configure_file(
            "${BISPELL_DICT_SOURCE_DIR}/${_dict}"
            "${BISPELL_ASSETS_DICT_DIR}/${_dict}"
            COPYONLY
        )
    endif()
endforeach()

function(bispell_add_test name)
    add_executable(${name} ${ARGN})
    target_link_libraries(${name} PRIVATE bispell_core)
    target_compile_definitions(${name} PRIVATE
        "BISPELL_DICT_DIR=\"${BISPELL_DICT_STAGE_DIR}\""
    )
    if(NOT MSVC)
        target_compile_options(${name} PRIVATE -Wall -Wextra -Wpedantic)
    endif()
    add_test(NAME ${name} COMMAND ${name})
    set_tests_properties(${name} PROPERTIES
        WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
    )
endfunction()

bispell_add_test(test_encoding test_encoding.cpp)
bispell_add_test(test_tokenizer test_tokenizer.cpp)
bispell_add_test(test_dictionary test_dictionary.cpp)

# Aggregate target used by the plan acceptance command line.
add_custom_target(bispell_core_tests
    DEPENDS test_encoding test_tokenizer test_dictionary
)
