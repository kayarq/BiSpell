#include "bispell/encoding.hpp"

#include <array>

namespace bispell {
namespace encoding {
namespace {

bool is_continuation(unsigned char c) noexcept {
    return (c & 0xC0u) == 0x80u;
}

// Decode one UTF-8 sequence at data[i..]. On success advances *i and sets *cp.
// On failure returns false without advancing past the bad lead if possible.
bool decode_one(const unsigned char* data, std::size_t n, std::size_t& i, char32_t& cp) noexcept {
    if (i >= n) {
        return false;
    }
    const unsigned char c0 = data[i];
    if (c0 <= 0x7Fu) {
        cp = c0;
        ++i;
        return true;
    }
    if ((c0 & 0xE0u) == 0xC0u) {
        if (i + 1 >= n || !is_continuation(data[i + 1])) {
            return false;
        }
        // overlong check: lead >= 0xC2
        if (c0 < 0xC2u) {
            return false;
        }
        cp = (static_cast<char32_t>(c0 & 0x1Fu) << 6) | (data[i + 1] & 0x3Fu);
        i += 2;
        return true;
    }
    if ((c0 & 0xF0u) == 0xE0u) {
        if (i + 2 >= n || !is_continuation(data[i + 1]) || !is_continuation(data[i + 2])) {
            return false;
        }
        cp = (static_cast<char32_t>(c0 & 0x0Fu) << 12) |
             (static_cast<char32_t>(data[i + 1] & 0x3Fu) << 6) |
             (data[i + 2] & 0x3Fu);
        // overlong / surrogate
        if (cp < 0x800u || (cp >= 0xD800u && cp <= 0xDFFFu)) {
            return false;
        }
        i += 3;
        return true;
    }
    if ((c0 & 0xF8u) == 0xF0u) {
        if (i + 3 >= n || !is_continuation(data[i + 1]) || !is_continuation(data[i + 2]) ||
            !is_continuation(data[i + 3])) {
            return false;
        }
        if (c0 > 0xF4u) {
            return false;
        }
        cp = (static_cast<char32_t>(c0 & 0x07u) << 18) |
             (static_cast<char32_t>(data[i + 1] & 0x3Fu) << 12) |
             (static_cast<char32_t>(data[i + 2] & 0x3Fu) << 6) |
             (data[i + 3] & 0x3Fu);
        if (cp < 0x10000u || cp > 0x10FFFFu) {
            return false;
        }
        i += 4;
        return true;
    }
    return false;
}

} // namespace

bool is_valid_utf8(std::string_view utf8) noexcept {
    const auto* data = reinterpret_cast<const unsigned char*>(utf8.data());
    const std::size_t n = utf8.size();
    std::size_t i = 0;
    char32_t cp = 0;
    while (i < n) {
        const std::size_t before = i;
        if (!decode_one(data, n, i, cp) || i == before) {
            return false;
        }
    }
    return true;
}

std::vector<CodePointSpan> decode_utf8(std::string_view utf8, bool replace_invalid, bool* ok) {
    std::vector<CodePointSpan> out;
    out.reserve(utf8.size()); // upper bound for ASCII-heavy text
    if (ok) {
        *ok = true;
    }

    const auto* data = reinterpret_cast<const unsigned char*>(utf8.data());
    const std::size_t n = utf8.size();
    std::size_t i = 0;
    std::uint32_t utf16_off = 0;

    while (i < n) {
        const std::size_t start = i;
        char32_t cp = 0;
        if (decode_one(data, n, i, cp)) {
            CodePointSpan span;
            span.cp = cp;
            span.utf8_offset = static_cast<std::uint32_t>(start);
            span.utf8_length = static_cast<std::uint32_t>(i - start);
            span.utf16_offset = utf16_off;
            span.utf16_length = utf16_units_for_codepoint(cp);
            utf16_off += span.utf16_length;
            out.push_back(span);
            continue;
        }

        if (!replace_invalid) {
            if (ok) {
                *ok = false;
            }
            return {};
        }
        if (ok) {
            *ok = false;
        }
        // Consume one bad byte as U+FFFD
        CodePointSpan span;
        span.cp = 0xFFFDu;
        span.utf8_offset = static_cast<std::uint32_t>(start);
        span.utf8_length = 1;
        span.utf16_offset = utf16_off;
        span.utf16_length = 1;
        utf16_off += 1;
        out.push_back(span);
        i = start + 1;
    }
    return out;
}

std::uint32_t utf16_length(std::string_view utf8) noexcept {
    auto spans = decode_utf8(utf8, true, nullptr);
    if (spans.empty()) {
        return 0;
    }
    const auto& last = spans.back();
    return last.utf16_offset + last.utf16_length;
}

std::string utf8_slice_by_utf16(std::string_view utf8, Utf16Range range) {
    if (range.length == 0) {
        return {};
    }
    auto spans = decode_utf8(utf8, true, nullptr);
    std::size_t begin_byte = std::string::npos;
    std::size_t end_byte = std::string::npos;
    const std::uint32_t end16 = range.end();

    for (const auto& sp : spans) {
        const std::uint32_t sp_end = sp.utf16_offset + sp.utf16_length;
        if (begin_byte == std::string::npos && sp.utf16_offset <= range.location && range.location < sp_end) {
            begin_byte = sp.utf8_offset;
        }
        if (sp.utf16_offset < end16 && end16 <= sp_end) {
            end_byte = sp.utf8_offset + sp.utf8_length;
            break;
        }
        // Range ends exactly at span boundary: catch when next would start at end16
        if (sp_end == end16) {
            end_byte = sp.utf8_offset + sp.utf8_length;
            break;
        }
    }
    if (begin_byte == std::string::npos) {
        return {};
    }
    if (end_byte == std::string::npos) {
        // take through end if range overruns
        end_byte = utf8.size();
    }
    if (begin_byte >= end_byte || begin_byte >= utf8.size()) {
        return {};
    }
    return std::string(utf8.substr(begin_byte, end_byte - begin_byte));
}

void append_utf8(std::string& out, char32_t cp) {
    if (cp <= 0x7Fu) {
        out.push_back(static_cast<char>(cp));
    } else if (cp <= 0x7FFu) {
        out.push_back(static_cast<char>(0xC0u | ((cp >> 6) & 0x1Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else if (cp <= 0xFFFFu) {
        if (cp >= 0xD800u && cp <= 0xDFFFu) {
            cp = 0xFFFDu; // invalid lone surrogate
        }
        out.push_back(static_cast<char>(0xE0u | ((cp >> 12) & 0x0Fu)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else {
        if (cp > 0x10FFFFu) {
            cp = 0xFFFDu;
        }
        out.push_back(static_cast<char>(0xF0u | ((cp >> 18) & 0x07u)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    }
}

std::string encode_utf8(const std::vector<char32_t>& cps) {
    return encode_utf8(cps.data(), cps.size());
}

std::string encode_utf8(const char32_t* data, std::size_t n) {
    std::string out;
    out.reserve(n);
    for (std::size_t i = 0; i < n; ++i) {
        append_utf8(out, data[i]);
    }
    return out;
}

std::size_t codepoint_count(std::string_view utf8) noexcept {
    return decode_utf8(utf8, true, nullptr).size();
}

} // namespace encoding
} // namespace bispell
