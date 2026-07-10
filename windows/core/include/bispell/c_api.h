/**
 * @file c_api.h
 * Stable C ABI for BiSpell core — suitable for C# P/Invoke / C++/CLI hosts.
 *
 * Encoding: all strings are UTF-8 (NUL-terminated).
 * Ranges: UTF-16 code unit location + length (NSRange / WinUI parity).
 *
 * Thread-safety: a single `bispell_engine` must not be used concurrently
 * without an external mutex (same contract as C++ `SpellEngine`).
 *
 * Ownership: create/free pairs; check results and string lists must be freed
 * with the matching free functions.
 */

#ifndef BISPELL_C_API_H
#define BISPELL_C_API_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(BISPELL_DLL)
#  if defined(BISPELL_BUILD)
#    define BISPELL_API __declspec(dllexport)
#  else
#    define BISPELL_API __declspec(dllimport)
#  endif
#else
#  define BISPELL_API
#endif

/** Opaque engine handle. */
typedef struct bispell_engine bispell_engine;

/** Language codes matching C++ `SpellLanguage` order. */
enum bispell_language {
    BISPELL_LANG_TURKISH = 0,
    BISPELL_LANG_ENGLISH = 1,
    BISPELL_LANG_UNKNOWN = 2
};

/** Spell settings (pass NULL to create for defaults). */
typedef struct bispell_settings {
    int is_enabled;              /* 1/0 */
    int turkish_enabled;         /* 1/0 */
    int english_enabled;         /* 1/0 */
    int max_suggestions;
    int min_word_length;
    int debounce_milliseconds;
} bispell_settings;

typedef struct bispell_misspelling {
    const char* word;            /* UTF-8; owned by parent result */
    uint32_t utf16_location;
    uint32_t utf16_length;
    int language;                /* bispell_language */
    char** suggestions;            /* may be NULL if empty; owned by result */
    size_t suggestion_count;
} bispell_misspelling;

typedef struct bispell_check_result {
    bispell_misspelling* items;
    size_t count;
    char* source_text;           /* UTF-8 copy; owned by result */
} bispell_check_result;

/** Fill settings with defaults (enabled TR+EN, max 5, min len 2, debounce 250). */
BISPELL_API void bispell_settings_default(bispell_settings* out);

/**
 * Create engine from dictionary directory (must contain tr.dic + en_US.dic).
 * @param dict_dir       Required UTF-8 path.
 * @param lexicon_path   Optional; NULL or "" = memory-only lexicon (hermetic tests).
 * @param settings       Optional; NULL = defaults.
 * @return NULL on failure; call bispell_last_error().
 */
BISPELL_API bispell_engine* bispell_engine_create(const char* dict_dir,
                                                  const char* lexicon_path,
                                                  const bispell_settings* settings);

BISPELL_API void bispell_engine_free(bispell_engine* engine);

/**
 * Spell-check UTF-8 text.
 * @param caret_utf16     UTF-16 caret; pass -1 if none.
 * @param near_caret_only 1 to restrict to window around caret.
 * @param window_radius   UTF-16 units (default 120 in C++ API).
 * @param out_result      Receives owned result; free with bispell_check_result_free.
 * @return 0 on success, non-zero on error.
 */
BISPELL_API int bispell_engine_check(bispell_engine* engine,
                                     const char* text_utf8,
                                     int32_t caret_utf16,
                                     int near_caret_only,
                                     int window_radius,
                                     bispell_check_result** out_result);

/**
 * Strict UTF-8 check: fails with error if text is not well-formed UTF-8.
 */
BISPELL_API int bispell_engine_check_strict(bispell_engine* engine,
                                            const char* text_utf8,
                                            int32_t caret_utf16,
                                            int near_caret_only,
                                            int window_radius,
                                            bispell_check_result** out_result);

BISPELL_API void bispell_check_result_free(bispell_check_result* result);

/**
 * Suggestions for a word. On success *out_list is an array of *out_count
 * malloc'd UTF-8 strings; free with bispell_string_list_free.
 */
BISPELL_API int bispell_engine_suggestions(bispell_engine* engine,
                                           const char* word_utf8,
                                           int language,
                                           char*** out_list,
                                           size_t* out_count);

BISPELL_API void bispell_string_list_free(char** list, size_t count);

BISPELL_API int bispell_engine_add_to_dictionary(bispell_engine* engine, const char* word);
BISPELL_API int bispell_engine_ignore_word(bispell_engine* engine, const char* word);
BISPELL_API int bispell_engine_remove_from_dictionary(bispell_engine* engine, const char* word);
BISPELL_API int bispell_engine_update_settings(bispell_engine* engine,
                                               const bispell_settings* settings);

/** Thread-local last error message (empty string if none). */
BISPELL_API const char* bispell_last_error(void);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* BISPELL_C_API_H */
