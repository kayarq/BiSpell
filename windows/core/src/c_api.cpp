#include "bispell/c_api.h"
#include "bispell/spell_engine.hpp"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <memory>
#include <new>
#include <string>
#include <unordered_set>
#include <vector>

namespace {

thread_local std::string g_last_error;

void set_error(const std::string& msg) { g_last_error = msg; }
void clear_error() { g_last_error.clear(); }

char* dup_cstr(const std::string& s) {
    char* p = static_cast<char*>(std::malloc(s.size() + 1));
    if (!p) return nullptr;
    std::memcpy(p, s.data(), s.size());
    p[s.size()] = '\0';
    return p;
}

bispell::AppSettings from_c_settings(const bispell_settings* s) {
    bispell::AppSettings out = bispell::AppSettings::defaults();
    if (!s) return out;
    out.is_enabled = s->is_enabled != 0;
    out.turkish_enabled = s->turkish_enabled != 0;
    out.english_enabled = s->english_enabled != 0;
    if (s->max_suggestions > 0) out.max_suggestions = s->max_suggestions;
    if (s->min_word_length > 0) out.min_word_length = s->min_word_length;
    if (s->debounce_milliseconds > 0) out.debounce_milliseconds = s->debounce_milliseconds;
    return out;
}

int to_c_lang(bispell::SpellLanguage l) {
    switch (l) {
    case bispell::SpellLanguage::Turkish: return BISPELL_LANG_TURKISH;
    case bispell::SpellLanguage::English: return BISPELL_LANG_ENGLISH;
    case bispell::SpellLanguage::Unknown: return BISPELL_LANG_UNKNOWN;
    }
    return BISPELL_LANG_UNKNOWN;
}

bispell::SpellLanguage from_c_lang(int l) {
    switch (l) {
    case BISPELL_LANG_TURKISH: return bispell::SpellLanguage::Turkish;
    case BISPELL_LANG_ENGLISH: return bispell::SpellLanguage::English;
    default: return bispell::SpellLanguage::Unknown;
    }
}

struct EngineImpl {
    std::unique_ptr<bispell::SpellEngine> engine;
};

bispell_check_result* make_check_result(const bispell::SpellCheckResult& r) {
    auto* out = static_cast<bispell_check_result*>(std::calloc(1, sizeof(bispell_check_result)));
    if (!out) return nullptr;
    out->source_text = dup_cstr(r.source_text);
    out->count = r.misspellings.size();
    if (out->count == 0) {
        out->items = nullptr;
        return out;
    }
    out->items =
        static_cast<bispell_misspelling*>(std::calloc(out->count, sizeof(bispell_misspelling)));
    if (!out->items) {
        bispell_check_result_free(out);
        return nullptr;
    }
    for (size_t i = 0; i < out->count; ++i) {
        const auto& m = r.misspellings[i];
        auto& dst = out->items[i];
        dst.word = dup_cstr(m.word);
        dst.utf16_location = m.utf16_range.location;
        dst.utf16_length = m.utf16_range.length;
        dst.language = to_c_lang(m.language);
        dst.suggestion_count = m.suggestions.size();
        if (dst.suggestion_count > 0) {
            char** mut = static_cast<char**>(std::calloc(dst.suggestion_count, sizeof(char*)));
            for (size_t s = 0; s < dst.suggestion_count; ++s) {
                mut[s] = dup_cstr(m.suggestions[s]);
            }
            dst.suggestions = mut; // const char* const* view of owned char**
        } else {
            dst.suggestions = nullptr;
        }
    }
    return out;
}

/// Copy a set of words into a sorted malloc'd C string list (suggestions style).
/// Empty set → *out_list=NULL, *out_count=0, return 0.
int export_sorted_string_list(const std::unordered_set<std::string>& words,
                              char*** out_list,
                              size_t* out_count) {
    *out_list = nullptr;
    *out_count = 0;
    if (words.empty()) {
        return 0;
    }
    std::vector<std::string> sorted(words.begin(), words.end());
    std::sort(sorted.begin(), sorted.end());
    char** arr = static_cast<char**>(std::calloc(sorted.size(), sizeof(char*)));
    if (!arr) {
        set_error("out of memory");
        return 2;
    }
    for (size_t i = 0; i < sorted.size(); ++i) {
        arr[i] = dup_cstr(sorted[i]);
        if (!arr[i]) {
            bispell_string_list_free(arr, i);
            set_error("out of memory");
            return 2;
        }
    }
    *out_list = arr;
    *out_count = sorted.size();
    return 0;
}

int check_impl(bispell_engine* engine,
               const char* text_utf8,
               int32_t caret_utf16,
               int near_caret_only,
               int window_radius,
               bool strict,
               bispell_check_result** out_result) {
    clear_error();
    if (!engine || !out_result) {
        set_error("null argument");
        return 1;
    }
    if (!text_utf8) {
        set_error("text_utf8 is null");
        return 1;
    }
    *out_result = nullptr;
    auto* impl = reinterpret_cast<EngineImpl*>(engine);
    try {
        bispell::CheckOptions opt;
        if (caret_utf16 >= 0) {
            opt.caret_utf16 = static_cast<std::uint32_t>(caret_utf16);
        }
        opt.near_caret_only = near_caret_only != 0;
        opt.window_radius = window_radius > 0 ? window_radius : 120;
        opt.strict_utf8 = strict;
        auto result = impl->engine->check(text_utf8, opt);
        *out_result = make_check_result(result);
        if (!*out_result) {
            set_error("out of memory");
            return 2;
        }
        return 0;
    } catch (const bispell::Error& e) {
        set_error(e.what());
        return 3;
    } catch (const std::exception& e) {
        set_error(e.what());
        return 4;
    } catch (...) {
        set_error("unknown error");
        return 5;
    }
}

} // namespace

extern "C" {

void bispell_settings_default(bispell_settings* out) {
    if (!out) return;
    out->is_enabled = 1;
    out->turkish_enabled = 1;
    out->english_enabled = 1;
    out->max_suggestions = 5;
    out->min_word_length = 2;
    out->debounce_milliseconds = 250;
}

bispell_engine* bispell_engine_create(const char* dict_dir,
                                      const char* lexicon_path,
                                      const bispell_settings* settings) {
    clear_error();
    if (!dict_dir || !dict_dir[0]) {
        set_error("dict_dir is required");
        return nullptr;
    }
    try {
        bispell::UserLexiconStore store =
            (lexicon_path && lexicon_path[0])
                ? bispell::UserLexiconStore(std::filesystem::path(lexicon_path), true)
                : bispell::UserLexiconStore({}, false); // memory-only
        auto app = from_c_settings(settings);
        auto eng = bispell::SpellEngine::bundled(std::filesystem::path(dict_dir), app,
                                                 std::move(store));
        auto* impl = new EngineImpl();
        impl->engine = std::make_unique<bispell::SpellEngine>(std::move(eng));
        return reinterpret_cast<bispell_engine*>(impl);
    } catch (const bispell::Error& e) {
        set_error(e.what());
        return nullptr;
    } catch (const std::exception& e) {
        set_error(e.what());
        return nullptr;
    } catch (...) {
        set_error("unknown error creating engine");
        return nullptr;
    }
}

void bispell_engine_free(bispell_engine* engine) {
    if (!engine) return;
    auto* impl = reinterpret_cast<EngineImpl*>(engine);
    delete impl;
}

int bispell_engine_check(bispell_engine* engine,
                         const char* text_utf8,
                         int32_t caret_utf16,
                         int near_caret_only,
                         int window_radius,
                         bispell_check_result** out_result) {
    return check_impl(engine, text_utf8, caret_utf16, near_caret_only, window_radius, false,
                      out_result);
}

int bispell_engine_check_strict(bispell_engine* engine,
                                const char* text_utf8,
                                int32_t caret_utf16,
                                int near_caret_only,
                                int window_radius,
                                bispell_check_result** out_result) {
    return check_impl(engine, text_utf8, caret_utf16, near_caret_only, window_radius, true,
                      out_result);
}

void bispell_check_result_free(bispell_check_result* result) {
    if (!result) return;
    if (result->items) {
        for (size_t i = 0; i < result->count; ++i) {
            auto& m = result->items[i];
            std::free(const_cast<char*>(m.word));
            if (m.suggestions) {
                for (size_t s = 0; s < m.suggestion_count; ++s) {
                    std::free(m.suggestions[s]);
                }
                std::free(m.suggestions);
            }
        }
        std::free(result->items);
    }
    std::free(result->source_text);
    std::free(result);
}

int bispell_engine_suggestions(bispell_engine* engine,
                               const char* word_utf8,
                               int language,
                               char*** out_list,
                               size_t* out_count) {
    clear_error();
    if (!engine || !word_utf8 || !out_list || !out_count) {
        set_error("null argument");
        return 1;
    }
    *out_list = nullptr;
    *out_count = 0;
    auto* impl = reinterpret_cast<EngineImpl*>(engine);
    try {
        auto list = impl->engine->suggestions(word_utf8, from_c_lang(language));
        if (list.empty()) {
            return 0;
        }
        char** arr = static_cast<char**>(std::calloc(list.size(), sizeof(char*)));
        if (!arr) {
            set_error("out of memory");
            return 2;
        }
        for (size_t i = 0; i < list.size(); ++i) {
            arr[i] = dup_cstr(list[i]);
            if (!arr[i]) {
                bispell_string_list_free(arr, i);
                set_error("out of memory");
                return 2;
            }
        }
        *out_list = arr;
        *out_count = list.size();
        return 0;
    } catch (const std::exception& e) {
        set_error(e.what());
        return 3;
    }
}

void bispell_string_list_free(char** list, size_t count) {
    if (!list) return;
    for (size_t i = 0; i < count; ++i) {
        std::free(list[i]);
    }
    std::free(list);
}

int bispell_engine_add_to_dictionary(bispell_engine* engine, const char* word) {
    clear_error();
    if (!engine || !word) {
        set_error("null argument");
        return 1;
    }
    reinterpret_cast<EngineImpl*>(engine)->engine->add_to_dictionary(word);
    return 0;
}

int bispell_engine_ignore_word(bispell_engine* engine, const char* word) {
    clear_error();
    if (!engine || !word) {
        set_error("null argument");
        return 1;
    }
    reinterpret_cast<EngineImpl*>(engine)->engine->ignore_word(word);
    return 0;
}

int bispell_engine_remove_from_dictionary(bispell_engine* engine, const char* word) {
    clear_error();
    if (!engine || !word) {
        set_error("null argument");
        return 1;
    }
    reinterpret_cast<EngineImpl*>(engine)->engine->remove_from_dictionary(word);
    return 0;
}

int bispell_engine_unignore_word(bispell_engine* engine, const char* word) {
    clear_error();
    if (!engine || !word) {
        set_error("null argument");
        return 1;
    }
    reinterpret_cast<EngineImpl*>(engine)->engine->unignore_word(word);
    return 0;
}

int bispell_engine_list_added_words(bispell_engine* engine, char*** out_list, size_t* out_count) {
    clear_error();
    if (!engine || !out_list || !out_count) {
        set_error("null argument");
        return 1;
    }
    *out_list = nullptr;
    *out_count = 0;
    auto* impl = reinterpret_cast<EngineImpl*>(engine);
    try {
        const auto lex = impl->engine->lexicon();
        return export_sorted_string_list(lex.added_words, out_list, out_count);
    } catch (const std::exception& e) {
        set_error(e.what());
        return 3;
    }
}

int bispell_engine_list_ignored_words(bispell_engine* engine, char*** out_list, size_t* out_count) {
    clear_error();
    if (!engine || !out_list || !out_count) {
        set_error("null argument");
        return 1;
    }
    *out_list = nullptr;
    *out_count = 0;
    auto* impl = reinterpret_cast<EngineImpl*>(engine);
    try {
        const auto lex = impl->engine->lexicon();
        return export_sorted_string_list(lex.ignored_words, out_list, out_count);
    } catch (const std::exception& e) {
        set_error(e.what());
        return 3;
    }
}

int bispell_engine_update_settings(bispell_engine* engine, const bispell_settings* settings) {
    clear_error();
    if (!engine || !settings) {
        set_error("null argument");
        return 1;
    }
    reinterpret_cast<EngineImpl*>(engine)->engine->update_settings(from_c_settings(settings));
    return 0;
}

const char* bispell_last_error(void) {
    return g_last_error.c_str();
}

} // extern "C"
