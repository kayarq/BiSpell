#include "bispell/tokenizer.hpp"
#include "bispell/encoding.hpp"

#include <string>

namespace bispell {
namespace Tokenizer {
namespace {

// Swift CharacterSet: Latin + Turkish letters + apostrophe + hyphen.
// Digits are intentionally NOT word characters.
bool is_word_cp(char32_t cp) noexcept {
    if (cp == U'\'' || cp == U'-') {
        return true;
    }
    if ((cp >= U'a' && cp <= U'z') || (cp >= U'A' && cp <= U'Z')) {
        return true;
    }
    switch (cp) {
    case U'à': case U'á': case U'â': case U'ã': case U'ä': case U'å': case U'æ':
    case U'ç': case U'è': case U'é': case U'ê': case U'ë': case U'ì': case U'í':
    case U'î': case U'ï': case U'ð': case U'ñ': case U'ò': case U'ó': case U'ô':
    case U'õ': case U'ö': case U'ø': case U'ù': case U'ú': case U'û': case U'ü':
    case U'ý': case U'þ': case U'ÿ': case U'ß':
    case U'À': case U'Á': case U'Â': case U'Ã': case U'Ä': case U'Å': case U'Æ':
    case U'Ç': case U'È': case U'É': case U'Ê': case U'Ë': case U'Ì': case U'Í':
    case U'Î': case U'Ï': case U'Ð': case U'Ñ': case U'Ò': case U'Ó': case U'Ô':
    case U'Õ': case U'Ö': case U'Ø': case U'Ù': case U'Ú': case U'Û': case U'Ü':
    case U'Ý': case U'Þ':
    case 0x011Fu: // ğ
    case 0x011Eu: // Ğ
    case 0x015Fu: // ş
    case 0x015Eu: // Ş
    case 0x0131u: // ı
    case 0x0130u: // İ
        return true;
    default:
        return false;
    }
}

bool is_ascii_digit_cp(char32_t cp) noexcept {
    return cp >= U'0' && cp <= U'9';
}

bool is_ascii_letter_cp(char32_t cp) noexcept {
    return (cp >= U'a' && cp <= U'z') || (cp >= U'A' && cp <= U'Z');
}

bool is_ascii_alnum_cp(char32_t cp) noexcept {
    return is_ascii_letter_cp(cp) || is_ascii_digit_cp(cp);
}

// Match Swift: ^[A-Za-z]+\d+[A-Za-z0-9]*$ with Character count > 12
bool matches_long_identifier(std::string_view text) {
    const auto spans = encoding::decode_utf8(text, true, nullptr);
    if (spans.size() <= 12) {
        return false;
    }
    std::size_t i = 0;
    const std::size_t n = spans.size();
    std::size_t letters = 0;
    while (i < n && is_ascii_letter_cp(spans[i].cp)) {
        ++letters;
        ++i;
    }
    if (letters == 0) {
        return false;
    }
    std::size_t digits = 0;
    while (i < n && is_ascii_digit_cp(spans[i].cp)) {
        ++digits;
        ++i;
    }
    if (digits == 0) {
        return false;
    }
    while (i < n && is_ascii_alnum_cp(spans[i].cp)) {
        ++i;
    }
    return i == n;
}

} // namespace

bool isWordCodePoint(char32_t cp) noexcept {
    return is_word_cp(cp);
}

std::vector<TextToken> tokenize(std::string_view text_utf8) {
    std::vector<TextToken> tokens;
    if (text_utf8.empty()) {
        return tokens;
    }

    const auto spans = encoding::decode_utf8(text_utf8, true, nullptr);
    const std::size_t n = spans.size();
    std::size_t i = 0;

    while (i < n) {
        while (i < n && !is_word_cp(spans[i].cp)) {
            ++i;
        }
        if (i >= n) {
            break;
        }

        // letterRange start; consume run of word characters (Swift end scan)
        const std::size_t run_start = i;
        ++i;
        while (i < n && is_word_cp(spans[i].cp)) {
            ++i;
        }
        const std::size_t run_end = i; // exclusive

        std::size_t start = run_start;
        std::size_t end = run_end;

        while (start < end && (spans[start].cp == U'\'' || spans[start].cp == U'-')) {
            ++start;
        }
        while (start < end && (spans[end - 1].cp == U'\'' || spans[end - 1].cp == U'-')) {
            --end;
        }

        if (start < end) {
            TextToken tok;
            const auto byte0 = spans[start].utf8_offset;
            const auto byte1 = spans[end - 1].utf8_offset + spans[end - 1].utf8_length;
            tok.text.assign(text_utf8.data() + byte0, byte1 - byte0);
            tok.utf16_range.location = spans[start].utf16_offset;
            const auto end16 = spans[end - 1].utf16_offset + spans[end - 1].utf16_length;
            tok.utf16_range.length = end16 - tok.utf16_range.location;
            tokens.push_back(std::move(tok));
        }

        // Swift: location = max(end, letterRange.location + 1)
        // i is already at run_end; ensure progress if run was empty after trim
        if (i <= run_start) {
            i = run_start + 1;
        }
    }

    return tokens;
}

bool shouldSkipToken(std::string_view text) {
    if (encoding::codepoint_count(text) < 2) {
        return true;
    }
    if (text.find('@') != std::string_view::npos) {
        return true;
    }
    if (text.find("://") != std::string_view::npos) {
        return true;
    }

    {
        const auto spans = encoding::decode_utf8(text, true, nullptr);
        bool all_num = !spans.empty();
        for (const auto& sp : spans) {
            if (!is_ascii_digit_cp(sp.cp)) {
                all_num = false;
                break;
            }
        }
        if (all_num) {
            return true;
        }
    }

    if (text.find('_') != std::string_view::npos) {
        return true;
    }
    // hasPrefix("http")
    if (text.size() >= 4 && text[0] == 'h' && text[1] == 't' && text[2] == 't' && text[3] == 'p') {
        return true;
    }
    if (matches_long_identifier(text)) {
        return true;
    }
    return false;
}

} // namespace Tokenizer
} // namespace bispell
