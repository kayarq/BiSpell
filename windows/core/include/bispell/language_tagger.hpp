#pragma once

/// @file language_tagger.hpp
/// TR/EN language heuristics (Swift `LanguageTagger` without Apple NaturalLanguage).
///
/// Detection order (both languages enabled):
/// 1. Turkish orthography characters (ğüşıöçĞÜŞİÖÇ) in word/context
/// 2. Turkish function-word list (tr_TR lowercasing)
/// 3. English function-word list (word itself)
/// 4. Long context (≥8 bytes): English function-word presence → EN bias
/// 5. Otherwise Unknown — dictionary membership resolves later in SpellEngine
///
/// Document-level bias: Turkish chars, then function-word vote (no NL).

#include "bispell/types.hpp"

#include <optional>
#include <string>
#include <string_view>

namespace bispell {

class LanguageTagger {
public:
    explicit LanguageTagger(bool turkish_enabled = true, bool english_enabled = true);

    bool turkish_enabled() const noexcept { return turkish_enabled_; }
    bool english_enabled() const noexcept { return english_enabled_; }
    void set_turkish_enabled(bool v) noexcept { turkish_enabled_ = v; }
    void set_english_enabled(bool v) noexcept { english_enabled_ = v; }

    /// Detect language for a single word (+ optional surrounding context snippet).
    SpellLanguage detect(std::string_view word, std::string_view context = {}) const;

    /// Detect once for a whole document/snippet. nullopt if undetermined / too short.
    std::optional<SpellLanguage> detect_document_language(std::string_view text) const;

    // Swift aliases
    std::optional<SpellLanguage> detectDocumentLanguage(std::string_view text) const {
        return detect_document_language(text);
    }

private:
    bool only_one_language_enabled() const noexcept;
    bool turkish_enabled_;
    bool english_enabled_;
};

} // namespace bispell
