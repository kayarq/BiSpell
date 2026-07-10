#pragma once

/// @file engine.hpp
/// RAII C++ host wrapper over the stable C ABI (`c_api.h`).
///
/// Prefer `SpellEngine` when linking C++ directly into the same binary.
/// Use `bispell::Engine` when you want the C ABI ownership model (create/free)
/// with exception-free boundaries and automatic free on destruction — useful
/// for mixed hosts and for documenting the P/Invoke contract in C++ tests.
///
/// Thread-safety: same as `SpellEngine` / C ABI — external mutex required.

#include "bispell/c_api.h"
#include "bispell/types.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace bispell {

class EngineError : public std::runtime_error {
public:
    explicit EngineError(const char* msg)
        : std::runtime_error(msg && msg[0] ? msg : "bispell engine error") {}
};

struct EngineSettings {
    bool is_enabled = true;
    bool turkish_enabled = true;
    bool english_enabled = true;
    int max_suggestions = 5;
    int min_word_length = 2;
    int debounce_milliseconds = 250;

    bispell_settings to_c() const noexcept {
        bispell_settings s{};
        s.is_enabled = is_enabled ? 1 : 0;
        s.turkish_enabled = turkish_enabled ? 1 : 0;
        s.english_enabled = english_enabled ? 1 : 0;
        s.max_suggestions = max_suggestions;
        s.min_word_length = min_word_length;
        s.debounce_milliseconds = debounce_milliseconds;
        return s;
    }
};

/// One misspelling copied out of a C check result (owns its strings).
struct EngineMisspelling {
    std::string word;
    Utf16Range utf16_range;
    SpellLanguage language = SpellLanguage::Unknown;
    std::vector<std::string> suggestions;
};

struct EngineCheckResult {
    std::string source_text;
    std::vector<EngineMisspelling> misspellings;
};

/// RAII owner of `bispell_engine*`.
class Engine {
public:
    Engine() = default;

    /// Create from dict dir; throws EngineError on failure.
    /// @param lexicon_path empty = memory-only lexicon.
    static Engine create(const std::string& dict_dir,
                         const std::string& lexicon_path = {},
                         const EngineSettings& settings = {}) {
        auto cs = settings.to_c();
        bispell_engine* raw = bispell_engine_create(
            dict_dir.c_str(),
            lexicon_path.empty() ? nullptr : lexicon_path.c_str(),
            &cs);
        if (!raw) {
            throw EngineError(bispell_last_error());
        }
        return Engine(raw);
    }

    Engine(Engine&& other) noexcept : eng_(other.eng_) { other.eng_ = nullptr; }
    Engine& operator=(Engine&& other) noexcept {
        if (this != &other) {
            reset();
            eng_ = other.eng_;
            other.eng_ = nullptr;
        }
        return *this;
    }

    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;

    ~Engine() { reset(); }

    explicit operator bool() const noexcept { return eng_ != nullptr; }
    bispell_engine* get() const noexcept { return eng_; }

    void reset() noexcept {
        if (eng_) {
            bispell_engine_free(eng_);
            eng_ = nullptr;
        }
    }

    EngineCheckResult check(std::string_view text_utf8,
                            std::optional<std::int32_t> caret_utf16 = std::nullopt,
                            bool near_caret_only = false,
                            int window_radius = 120,
                            bool strict_utf8 = false) const {
        require();
        bispell_check_result* raw = nullptr;
        const int32_t caret = caret_utf16.has_value() ? *caret_utf16 : -1;
        const int rc = strict_utf8
            ? bispell_engine_check_strict(eng_, std::string(text_utf8).c_str(), caret,
                                          near_caret_only ? 1 : 0, window_radius, &raw)
            : bispell_engine_check(eng_, std::string(text_utf8).c_str(), caret,
                                   near_caret_only ? 1 : 0, window_radius, &raw);
        if (rc != 0 || !raw) {
            throw EngineError(bispell_last_error());
        }
        EngineCheckResult out;
        if (raw->source_text) {
            out.source_text = raw->source_text;
        }
        out.misspellings.reserve(raw->count);
        for (size_t i = 0; i < raw->count; ++i) {
            const auto& m = raw->items[i];
            EngineMisspelling em;
            if (m.word) {
                em.word = m.word;
            }
            em.utf16_range = Utf16Range{m.utf16_location, m.utf16_length};
            em.language = from_c_lang(m.language);
            em.suggestions.reserve(m.suggestion_count);
            for (size_t s = 0; s < m.suggestion_count; ++s) {
                if (m.suggestions && m.suggestions[s]) {
                    em.suggestions.emplace_back(m.suggestions[s]);
                }
            }
            out.misspellings.push_back(std::move(em));
        }
        bispell_check_result_free(raw);
        return out;
    }

    std::vector<std::string> suggestions(std::string_view word, SpellLanguage language) const {
        require();
        char** list = nullptr;
        size_t count = 0;
        if (bispell_engine_suggestions(eng_, std::string(word).c_str(), to_c_lang(language),
                                       &list, &count) != 0) {
            throw EngineError(bispell_last_error());
        }
        std::vector<std::string> out;
        out.reserve(count);
        for (size_t i = 0; i < count; ++i) {
            if (list && list[i]) {
                out.emplace_back(list[i]);
            }
        }
        bispell_string_list_free(list, count);
        return out;
    }

    void add_to_dictionary(std::string_view word) {
        require();
        if (bispell_engine_add_to_dictionary(eng_, std::string(word).c_str()) != 0) {
            throw EngineError(bispell_last_error());
        }
    }

    void ignore_word(std::string_view word) {
        require();
        if (bispell_engine_ignore_word(eng_, std::string(word).c_str()) != 0) {
            throw EngineError(bispell_last_error());
        }
    }

    void remove_from_dictionary(std::string_view word) {
        require();
        if (bispell_engine_remove_from_dictionary(eng_, std::string(word).c_str()) != 0) {
            throw EngineError(bispell_last_error());
        }
    }

    void update_settings(const EngineSettings& settings) {
        require();
        auto cs = settings.to_c();
        if (bispell_engine_update_settings(eng_, &cs) != 0) {
            throw EngineError(bispell_last_error());
        }
    }

private:
    explicit Engine(bispell_engine* eng) : eng_(eng) {}

    void require() const {
        if (!eng_) {
            throw EngineError("engine is null");
        }
    }

    static int to_c_lang(SpellLanguage l) {
        switch (l) {
        case SpellLanguage::Turkish: return BISPELL_LANG_TURKISH;
        case SpellLanguage::English: return BISPELL_LANG_ENGLISH;
        case SpellLanguage::Unknown: return BISPELL_LANG_UNKNOWN;
        }
        return BISPELL_LANG_UNKNOWN;
    }

    static SpellLanguage from_c_lang(int l) {
        switch (l) {
        case BISPELL_LANG_TURKISH: return SpellLanguage::Turkish;
        case BISPELL_LANG_ENGLISH: return SpellLanguage::English;
        default: return SpellLanguage::Unknown;
        }
    }

    bispell_engine* eng_ = nullptr;
};

} // namespace bispell
