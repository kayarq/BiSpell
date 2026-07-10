#pragma once

/// @file system_spell_suggester.hpp
/// Stub for macOS `SystemSpellSuggester` (NSSpellChecker).
///
/// On Windows MVP this is intentionally a **no-op**:
/// - suggestions() always returns empty (SpellEngine falls back to local Hunspell dict)
/// - is_correct() always returns false (membership is dict/lexicon only)
///
/// A future unit may wire Windows Spell Checking API here without changing SpellEngine.

#include "bispell/types.hpp"

#include <string>
#include <string_view>
#include <vector>

namespace bispell {

struct SystemSpellSuggester {
    /// Always empty on Windows MVP (macOS-only quality path).
    static std::vector<std::string> suggestions(std::string_view /*word*/,
                                                SpellLanguage /*language*/,
                                                int /*limit*/ = 5) {
        return {};
    }

    /// Always false on Windows MVP — local dictionary decides correctness.
    static bool is_correct(std::string_view /*word*/, SpellLanguage /*language*/) {
        return false;
    }

    // Swift aliases
    static bool isCorrect(std::string_view word, SpellLanguage language) {
        return is_correct(word, language);
    }
};

} // namespace bispell
