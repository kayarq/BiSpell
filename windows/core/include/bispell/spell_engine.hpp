#pragma once

/// @file spell_engine.hpp
/// Portable spell engine (Swift `SpellEngine` parity).
///
/// ## Thread-safety
/// **Not concurrent-safe without an external mutex.** Match practical use of Swift
/// `SpellEngine: @unchecked Sendable` — one owner/session at a time. The internal
/// correctness/suggestion LRU is mutex-guarded for its own map ops, but concurrent
/// `check` / `add_to_dictionary` / `update_settings` still races on settings/lexicon
/// without locking outside this type.
///
/// ## Encoding
/// - Input text is UTF-8. Invalid sequences: default **replacement mode** (U+FFFD)
///   so hosts never crash on noisy paste; set `CheckOptions::strict_utf8` to throw
///   `Error(InvalidUtf8)` instead.
/// - Misspelling ranges are UTF-16 code units (`Utf16Range`), nested on `Misspelling`.
///
/// ## Injection
/// - Dictionary directory via `bundled(dict_dir, ...)`
/// - Lexicon path via `UserLexiconStore` (empty path = memory-only)
/// - Clock via `IClock` (reserved for debounce / future timed policies; injectable now)

#include "bispell/app_settings.hpp"
#include "bispell/clock.hpp"
#include "bispell/dictionary.hpp"
#include "bispell/error.hpp"
#include "bispell/language_tagger.hpp"
#include "bispell/types.hpp"
#include "bispell/user_lexicon.hpp"

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <list>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

namespace bispell {

struct CheckOptions {
    std::optional<std::uint32_t> caret_utf16;
    std::optional<std::string> bundle_id;
    bool near_caret_only = false;
    int window_radius = 120;
    /// If true, invalid UTF-8 throws `Error` with `ErrorCode::InvalidUtf8`.
    /// If false (default), invalid bytes are replaced (tokenizer parity) and check continues.
    bool strict_utf8 = false;
};

class SpellEngine {
public:
    /// Construct from already-loaded dictionaries.
    SpellEngine(HunspellDictionary turkish,
                HunspellDictionary english,
                AppSettings settings = AppSettings::defaults(),
                UserLexiconStore lexicon_store = UserLexiconStore{},
                std::shared_ptr<IClock> clock = make_system_clock());

    /// Load bundled `tr.dic` + `en_US.dic` from `dict_dir`. Throws on missing dicts.
    static SpellEngine bundled(const std::filesystem::path& dict_dir,
                               AppSettings settings = AppSettings::defaults(),
                               UserLexiconStore lexicon_store = UserLexiconStore{},
                               std::shared_ptr<IClock> clock = make_system_clock());

    void update_settings(const AppSettings& settings);
    void updateSettings(const AppSettings& settings) { update_settings(settings); }

    const AppSettings& settings() const noexcept { return settings_; }
    AppSettings& settings() noexcept { return settings_; }

    /// Injectable clock (tests / future debounce).
    const IClock& clock() const { return *clock_; }

    UserLexicon lexicon() const { return lexicon_store_.current(); }

    void add_to_dictionary(std::string_view word);
    void ignore_word(std::string_view word);
    void ignore_word(std::string_view word, std::string_view app_id);
    void remove_from_dictionary(std::string_view word);
    void unignore_word(std::string_view word);

    // Swift aliases
    void addToDictionary(std::string_view word) { add_to_dictionary(word); }
    void ignoreWord(std::string_view word) { ignore_word(word); }
    void removeFromDictionary(std::string_view word) { remove_from_dictionary(word); }

    /// Markers only: correctness + language. Suggestions empty until filled.
    SpellCheckResult check(std::string_view text_utf8, const CheckOptions& options = {}) const;

    /// Convenience overload matching Swift keyword args.
    SpellCheckResult check(std::string_view text_utf8,
                           std::optional<std::uint32_t> caret_utf16,
                           bool near_caret_only = false,
                           int window_radius = 120) const {
        CheckOptions opt;
        opt.caret_utf16 = caret_utf16;
        opt.near_caret_only = near_caret_only;
        opt.window_radius = window_radius;
        return check(text_utf8, opt);
    }

    /// Public suggestion API — call just before showing a popup.
    std::vector<std::string> suggestions(std::string_view word, SpellLanguage language) const;

    /// Fill suggestions on a misspelling; may disambiguate TR vs EN by edit distance.
    Misspelling with_suggestions(const Misspelling& misspelling) const;
    Misspelling withSuggestions(const Misspelling& m) const { return with_suggestions(m); }

    /// Misspelling nearest to caret.
    std::optional<Misspelling> nearest_misspelling(const std::vector<Misspelling>& misspellings,
                                                   std::optional<std::uint32_t> caret_utf16) const;

private:
    SpellLanguage resolve_language(std::string_view word,
                                   std::string_view context,
                                   std::optional<SpellLanguage> document_lang) const;
    std::pair<bool, SpellLanguage> evaluate_correctness(std::string_view word,
                                                        SpellLanguage language) const;
    std::pair<bool, SpellLanguage> evaluate_both_correctness(std::string_view word) const;
    bool is_correct_cached(std::string_view word, SpellLanguage language) const;
    bool is_correct_local_or_system(std::string_view word, SpellLanguage language) const;
    bool should_disambiguate_language(SpellLanguage language) const;
    static int edit_distance_utf8(std::string_view a, std::string_view b);
    static std::string context_snippet(std::string_view text,
                                       Utf16Range range,
                                       int radius_utf16);
    static std::string cache_key(std::string_view word, SpellLanguage language);

    HunspellDictionary turkish_;
    HunspellDictionary english_;
    mutable LanguageTagger tagger_;
    mutable UserLexiconStore lexicon_store_;
    AppSettings settings_;
    std::shared_ptr<IClock> clock_;

    struct CacheEntry {
        bool is_correct = false;
        std::vector<std::string> suggestions;
        bool suggestions_computed = false;
    };

    /// Small LRU (cap 2000) with O(1) touch and suggestion-flag merge (Swift parity).
    /// Mutable so const check()/suggestions() may populate it.
    class SpellResultCache {
    public:
        explicit SpellResultCache(std::size_t capacity = 2000);

        std::optional<CacheEntry> get(const std::string& key);
        void set(const std::string& key, CacheEntry value);
        void remove_all();

    private:
        static CacheEntry merge(const CacheEntry& existing, const CacheEntry& incoming);

        std::size_t capacity_;
        std::list<std::string> order_;
        std::unordered_map<std::string, std::pair<CacheEntry, std::list<std::string>::iterator>>
            map_;
        /// unique_ptr so SpellEngine remains movable (same pattern as UserLexiconStore).
        std::unique_ptr<std::mutex> mutex_ = std::make_unique<std::mutex>();
    };

    static constexpr std::size_t k_cache_capacity = 2000;
    mutable SpellResultCache cache_{k_cache_capacity};
};

} // namespace bispell
