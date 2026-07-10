#pragma once

/// @file types.hpp
/// Public model types for the portable spell core.
///
/// Encoding contract:
/// - All `std::string` text fields are **UTF-8**.
/// - `Utf16Range` uses **UTF-16 code unit** offsets/lengths (NSRange / WinUI parity).
/// - Convert with helpers in `encoding.hpp` when bridging to UTF-16 APIs.
///
/// Range layout (U2 freeze — public API for U3 Apply / host UI):
/// tokens and misspellings both nest `Utf16Range`
/// (`TextToken::utf16_range`, `Misspelling::utf16_range`). Flat
/// `utf16_location`/`utf16_length` fields are **not** part of this API.
/// U3 implementers must use this nested `Utf16Range` contract exclusively.

#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace bispell {

enum class SpellLanguage {
    Turkish,
    English,
    Unknown,
};

/// UTF-16 code-unit span (location + length), matching Swift `NSRange`.
/// Shared by `TextToken` and `Misspelling` (single shape for Apply / host UI).
struct Utf16Range {
    std::uint32_t location = 0;
    std::uint32_t length = 0;

    constexpr bool empty() const noexcept { return length == 0; }
    constexpr std::uint32_t end() const noexcept { return location + length; }

    friend constexpr bool operator==(const Utf16Range& a, const Utf16Range& b) noexcept {
        return a.location == b.location && a.length == b.length;
    }
    friend constexpr bool operator!=(const Utf16Range& a, const Utf16Range& b) noexcept {
        return !(a == b);
    }
};

/// A word token extracted from source text.
/// Range shape: nested `Utf16Range` (frozen; same as `Misspelling`).
struct TextToken {
    std::string text;           ///< UTF-8 token text
    Utf16Range utf16_range;     ///< Range in the original text as UTF-16 code units
};

/// A misspelled word with optional suggestions (suggestions may be filled lazily).
struct Misspelling {
    std::string word;                      ///< UTF-8
    Utf16Range utf16_range;                ///< Same nested range type as `TextToken`
    SpellLanguage language = SpellLanguage::Unknown;
    std::vector<std::string> suggestions;  ///< UTF-8 suggestion strings
};

/// Result of a full-document spell check (U3+; type defined here for stability).
struct SpellCheckResult {
    std::string source_text;  ///< UTF-8
    std::vector<Misspelling> misspellings;
};

inline const char* to_string(SpellLanguage lang) noexcept {
    switch (lang) {
    case SpellLanguage::Turkish: return "tr";
    case SpellLanguage::English: return "en";
    case SpellLanguage::Unknown: return "unknown";
    }
    return "unknown";
}

inline const char* display_name(SpellLanguage lang) noexcept {
    switch (lang) {
    case SpellLanguage::Turkish: return "Turkish";
    case SpellLanguage::English: return "English";
    case SpellLanguage::Unknown: return "Unknown";
    }
    return "Unknown";
}

} // namespace bispell
