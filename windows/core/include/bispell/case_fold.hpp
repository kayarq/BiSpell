#pragma once

/// @file case_fold.hpp
/// Locale-aware lowercasing for dictionary normalization.
///
/// Turkish (`tr_TR` intent):
///   - U+0049 LATIN CAPITAL LETTER I  → U+0131 LATIN SMALL LETTER DOTLESS I (ı)
///   - U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE (İ) → U+0069 LATIN SMALL LETTER I (i)
///   - Other letters: Unicode simple lowercase (single code point where possible)
///
/// English / Unknown (`en_US` intent):
///   - U+0049 I → i
///   - U+0130 İ → i  (no combining dot; dictionary stems are NFC-simple)
///   - Standard A–Z and common Latin extensions

#include "bispell/types.hpp"

#include <string>
#include <string_view>

namespace bispell {

/// Lowercase `word` for the given language (UTF-8 in, UTF-8 out).
std::string case_fold(std::string_view word, SpellLanguage language);

/// Turkish-specific lowercase (tr_TR).
std::string case_fold_turkish(std::string_view word);

/// English / default lowercase (en_US-ish).
std::string case_fold_english(std::string_view word);

} // namespace bispell
