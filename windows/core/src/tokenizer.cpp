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
    // Hex code points only — MSVC rejects non-ASCII U'…' without /utf-8,
    // and hex is portable across source encodings.
    switch (cp) {
    // Latin-1 lowercase accents
    case 0x00E0u: case 0x00E1u: case 0x00E2u: case 0x00E3u: case 0x00E4u:
    case 0x00E5u: case 0x00E6u: case 0x00E7u: case 0x00E8u: case 0x00E9u:
    case 0x00EAu: case 0x00EBu: case 0x00ECu: case 0x00EDu: case 0x00EEu:
    case 0x00EFu: case 0x00F0u: case 0x00F1u: case 0x00F2u: case 0x00F3u:
    case 0x00F4u: case 0x00F5u: case 0x00F6u: case 0x00F8u: case 0x00F9u:
    case 0x00FAu: case 0x00FBu: case 0x00FCu: case 0x00FDu: case 0x00FEu:
    case 0x00FFu: case 0x00DFu:
    // Latin-1 uppercase accents
    case 0x00C0u: case 0x00C1u: case 0x00C2u: case 0x00C3u: case 0x00C4u:
    case 0x00C5u: case 0x00C6u: case 0x00C7u: case 0x00C8u: case 0x00C9u:
    case 0x00CAu: case 0x00CBu: case 0x00CCu: case 0x00CDu: case 0x00CEu:
    case 0x00CFu: case 0x00D0u: case 0x00D1u: case 0x00D2u: case 0x00D3u:
    case 0x00D4u: case 0x00D5u: case 0x00D6u: case 0x00D8u: case 0x00D9u:
    case 0x00DAu: case 0x00DBu: case 0x00DCu: case 0x00DDu: case 0x00DEu:
    // Turkish extended
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
