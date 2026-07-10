#pragma once

/// @file encoding.hpp
/// Explicit UTF-8 internal storage helpers and UTF-16 range conversion.
///
/// Contract (mandate: robust dual-coder path):
/// 1. Core APIs accept and store text as **UTF-8**.
/// 2. Public ranges (`Utf16Range`) are **UTF-16 code units** so WinUI / Win32 /
///    and macOS `NSRange` consumers share one coordinate system.
/// 3. Invalid UTF-8 is never silent corruption: callers can validate strictly,
///    or use replacement-mode decoding for resilient scanning (tokenizer).

#include "bispell/types.hpp"

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace bispell {
namespace encoding {

/// One decoded Unicode scalar with dual-index bookkeeping.
struct CodePointSpan {
    char32_t cp = 0;
    std::uint32_t utf8_offset = 0;   ///< Byte offset into original UTF-8
    std::uint32_t utf8_length = 0;   ///< Byte length of this code point
    std::uint32_t utf16_offset = 0;  ///< UTF-16 code unit offset
    std::uint32_t utf16_length = 0;  ///< 1 for BMP, 2 for supplementary
};

/// Number of UTF-16 code units needed for a single code point.
inline constexpr std::uint32_t utf16_units_for_codepoint(char32_t cp) noexcept {
    return (cp <= 0xFFFFu) ? 1u : 2u;
}

/// True if bytes are well-formed UTF-8.
bool is_valid_utf8(std::string_view utf8) noexcept;

/// Decode UTF-8 into code-point spans with UTF-16 offsets.
/// @param replace_invalid If true, invalid sequences become U+FFFD (each bad
///        byte group → one replacement) and scanning continues; if false,
///        returns empty on the first error and sets *ok = false when provided.
std::vector<CodePointSpan> decode_utf8(std::string_view utf8,
                                       bool replace_invalid = true,
                                       bool* ok = nullptr);

/// Total UTF-16 length of a UTF-8 string (replacement mode for invalid bytes).
std::uint32_t utf16_length(std::string_view utf8) noexcept;

/// Extract UTF-8 substring covering [range.location, range.location+range.length)
/// in UTF-16 space. Returns empty string if range is out of bounds.
std::string utf8_slice_by_utf16(std::string_view utf8, Utf16Range range);

/// Encode a single code point to UTF-8 (appends).
void append_utf8(std::string& out, char32_t cp);

/// Encode a sequence of code points to UTF-8.
std::string encode_utf8(const std::vector<char32_t>& cps);

/// Encode code points from iterators.
std::string encode_utf8(const char32_t* data, std::size_t n);

/// Count Unicode scalar values (code points); invalid UTF-8 uses replacement mode.
std::size_t codepoint_count(std::string_view utf8) noexcept;

} // namespace encoding
} // namespace bispell
