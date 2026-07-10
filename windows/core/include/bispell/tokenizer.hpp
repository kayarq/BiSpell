#pragma once

/// @file tokenizer.hpp
/// Word tokenizer with TR/EN word characters and skip rules (Swift `Tokenizer` parity).

#include "bispell/types.hpp"

#include <string_view>
#include <vector>

namespace bispell {
namespace Tokenizer {

/// Tokenize UTF-8 text into word tokens.
/// Ranges are UTF-16 code units into the same logical text.
/// Empty input → empty vector. Invalid UTF-8 is scanned in replacement mode
/// so emoji-adjacent and noisy input does not abort.
std::vector<TextToken> tokenize(std::string_view text_utf8);

/// Whether a token should be ignored by the spell checker
/// (short, numeric, URL-like, underscore identifiers, long alnum ids).
bool shouldSkipToken(std::string_view word_utf8);

/// True if the Unicode scalar is a word character (TR/EN letters, apostrophe, hyphen).
bool isWordCodePoint(char32_t cp) noexcept;

} // namespace Tokenizer
} // namespace bispell
