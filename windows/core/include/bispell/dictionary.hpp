#pragma once

/// @file dictionary.hpp
/// Hunspell-format `.dic` stem loader with membership + restricted-edit suggestions.
/// Matches Swift `HunspellDictionary` (not full affix expansion).
///
/// Naming: primary methods use snake_case house style; Swift/plan camelCase aliases
/// (`stemCandidates`, `wordCount`) are provided as inline wrappers for U3 reviewers.

#include "bispell/error.hpp"
#include "bispell/types.hpp"

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace bispell {

/// Loaded stem dictionary for one language.
class HunspellDictionary {
public:
    struct Entry {
        /// Canonical surface form when it differs from the lowercased key.
        /// `std::nullopt` means the key itself is the display form (Swift parity).
        std::optional<std::string> canonical;
        std::int32_t order = 0;
    };

    HunspellDictionary() = default;

    /// Load a `.dic` file. Throws `bispell::Error` with structured codes on failure.
    static HunspellDictionary load(const std::filesystem::path& path, SpellLanguage language);

    /// Parse dictionary text already in memory (UTF-8). Throws on empty parse if required.
    static HunspellDictionary load_from_string(std::string_view data,
                                               SpellLanguage language,
                                               std::string path_for_errors = {});

    SpellLanguage language() const noexcept { return language_; }
    std::size_t word_count() const noexcept { return entries_.size(); }
    /// Swift / plan alias for `word_count`.
    std::size_t wordCount() const noexcept { return word_count(); }
    bool empty() const noexcept { return entries_.empty(); }

    /// True if word is in the dictionary after normalize + light stem candidates.
    bool contains(std::string_view word) const;

    /// Distance-1 restricted-edit suggestions; empty if `contains(word)`.
    std::vector<std::string> suggestions(std::string_view word, int limit = 5) const;

    /// Language-aware lowercasing used for keys and queries.
    static std::string normalize(std::string_view word, SpellLanguage language);

    /// Light stem stripping candidates (English plurals/suffixes, Turkish suffixes).
    /// Length gates use Unicode scalar (code point) counts, matching Swift `Character` count.
    static std::vector<std::string> stem_candidates(std::string_view lower, SpellLanguage language);

    /// Swift / plan alias for `stem_candidates`.
    static std::vector<std::string> stemCandidates(std::string_view lower,
                                                   SpellLanguage language) {
        return stem_candidates(lower, language);
    }

    const std::unordered_map<std::string, Entry>& entries() const noexcept { return entries_; }

private:
    SpellLanguage language_ = SpellLanguage::Unknown;
    std::unordered_map<std::string, Entry> entries_;
};

/// Resolve bundled dictionary paths (en_US.dic / tr.dic) under a directory.
namespace DictionaryLoader {

struct BundledPaths {
    std::filesystem::path turkish;
    std::filesystem::path english;
};

/// Look for `tr.dic` and `en_US.dic` under `dict_dir`.
/// Throws `Error` (`FileNotFound`) if either is missing.
BundledPaths resolve_bundled(const std::filesystem::path& dict_dir);

/// Load both bundled dictionaries.
struct BundledDictionaries {
    HunspellDictionary turkish;
    HunspellDictionary english;
};

BundledDictionaries load_bundled(const std::filesystem::path& dict_dir);

} // namespace DictionaryLoader

} // namespace bispell
