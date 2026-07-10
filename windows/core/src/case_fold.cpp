#include "bispell/case_fold.hpp"
#include "bispell/encoding.hpp"

namespace bispell {
namespace {

// Simple Unicode lowercase for common Latin / Turkish orthography used in dicts.
// Prefer explicit maps over locale-dependent C library functions.
char32_t simple_lower(char32_t cp) noexcept {
    // ASCII
    if (cp >= U'A' && cp <= U'Z') {
        return cp - U'A' + U'a';
    }
    // Latin-1 supplement capitals
    if (cp >= 0x00C0u && cp <= 0x00D6u) {
        return cp + 0x20u;
    }
    if (cp >= 0x00D8u && cp <= 0x00DEu) {
        return cp + 0x20u;
    }
    // Latin extended-A (common)
    switch (cp) {
    case 0x011Eu: return 0x011Fu; // Ğ→ğ
    case 0x0130u: return U'i';    // İ→i (caller may override for English)
    case 0x015Eu: return 0x015Fu; // Ş→ş
    case 0x00C7u: return 0x00E7u; // Ç→ç (also covered by Latin-1)
    case 0x00D6u: return 0x00F6u; // Ö→ö
    case 0x00DCu: return 0x00FCu; // Ü→ü
    case 0x0139u: return 0x013Au;
    case 0x013Bu: return 0x013Cu;
    case 0x013Du: return 0x013Eu;
    case 0x0141u: return 0x0142u;
    case 0x0143u: return 0x0144u;
    case 0x0147u: return 0x0148u;
    case 0x014Au: return 0x014Bu;
    case 0x014Cu: return 0x014Du;
    case 0x0150u: return 0x0151u;
    case 0x0154u: return 0x0155u;
    case 0x0158u: return 0x0159u;
    case 0x015Au: return 0x015Bu;
    case 0x0160u: return 0x0161u;
    case 0x0162u: return 0x0163u;
    case 0x0164u: return 0x0165u;
    case 0x016Eu: return 0x016Fu;
    case 0x0170u: return 0x0171u;
    case 0x0178u: return 0x00FFu; // Ÿ→ÿ
    case 0x0179u: return 0x017Au;
    case 0x017Bu: return 0x017Cu;
    case 0x017Du: return 0x017Eu;
    default: break;
    }
    return cp;
}

std::string fold_spans(std::string_view word, bool turkish) {
    auto spans = encoding::decode_utf8(word, true, nullptr);
    std::string out;
    out.reserve(word.size());
    for (const auto& sp : spans) {
        char32_t cp = sp.cp;
        if (turkish) {
            if (cp == U'I') {
                encoding::append_utf8(out, 0x0131u); // ı
                continue;
            }
            if (cp == 0x0130u) {
                encoding::append_utf8(out, U'i');
                continue;
            }
        } else {
            if (cp == U'I') {
                encoding::append_utf8(out, U'i');
                continue;
            }
            if (cp == 0x0130u) {
                // en_US-ish: map to plain i (avoid i + combining dot for dict keys)
                encoding::append_utf8(out, U'i');
                continue;
            }
        }
        encoding::append_utf8(out, simple_lower(cp));
    }
    return out;
}

} // namespace

std::string case_fold_turkish(std::string_view word) {
    return fold_spans(word, true);
}

std::string case_fold_english(std::string_view word) {
    return fold_spans(word, false);
}

std::string case_fold(std::string_view word, SpellLanguage language) {
    switch (language) {
    case SpellLanguage::Turkish:
        return case_fold_turkish(word);
    case SpellLanguage::English:
    case SpellLanguage::Unknown:
        return case_fold_english(word);
    }
    return case_fold_english(word);
}

} // namespace bispell
