#include "bispell/language_tagger.hpp"
#include "bispell/case_fold.hpp"
#include "bispell/encoding.hpp"
#include "bispell/tokenizer.hpp"

#include <unordered_set>

namespace bispell {
namespace {

bool contains_turkish_chars(std::string_view sample) {
    // ğ ü ş ı ö ç Ğ Ü Ş İ Ö Ç
    static const char32_t kTurkish[] = {
        0x011F, 0x00FC, 0x015F, 0x0131, 0x00F6, 0x00E7,
        0x011E, 0x00DC, 0x015E, 0x0130, 0x00D6, 0x00C7,
    };
    auto spans = encoding::decode_utf8(sample, true, nullptr);
    for (const auto& sp : spans) {
        for (char32_t t : kTurkish) {
            if (sp.cp == t) {
                return true;
            }
        }
    }
    return false;
}

const std::unordered_set<std::string>& turkish_function_words() {
    static const std::unordered_set<std::string> k = {
        "ve", "bir", "bu", "da", "de", "mi", "mı", "mu", "mü",
        "için", "ile", "ama", "çok", "daha", "gibi", "kadar",
        "var", "yok", "ben", "sen", "biz", "siz", "onlar",
        "şey", "ki", "ne", "nasıl", "neden", "çünkü",
    };
    return k;
}

const std::unordered_set<std::string>& english_function_words() {
    static const std::unordered_set<std::string> k = {
        "the", "and", "for", "with", "that", "this", "from", "have",
        "was", "were", "are", "is", "not", "you", "your", "what",
        "when", "where", "which", "would", "could", "should",
    };
    return k;
}

/// Loose token scan for EN function words in a context snippet (NL substitute).
bool context_has_english_function_words(std::string_view sample) {
    std::string token;
    auto flush = [&]() -> bool {
        if (token.empty()) {
            return false;
        }
        const std::string lower = case_fold_english(token);
        token.clear();
        return english_function_words().count(lower) > 0;
    };
    for (unsigned char uc : sample) {
        if ((uc >= 'A' && uc <= 'Z') || (uc >= 'a' && uc <= 'z') || uc >= 0x80) {
            token.push_back(static_cast<char>(uc));
        } else {
            if (flush()) {
                return true;
            }
        }
    }
    return flush();
}

} // namespace

LanguageTagger::LanguageTagger(bool turkish_enabled, bool english_enabled)
    : turkish_enabled_(turkish_enabled)
    , english_enabled_(english_enabled) {}

bool LanguageTagger::only_one_language_enabled() const noexcept {
    return (turkish_enabled_ && !english_enabled_) || (!turkish_enabled_ && english_enabled_);
}

SpellLanguage LanguageTagger::detect(std::string_view word, std::string_view context) const {
    if (only_one_language_enabled()) {
        return turkish_enabled_ ? SpellLanguage::Turkish : SpellLanguage::English;
    }

    const std::string_view sample = !context.empty() ? context : word;

    if (contains_turkish_chars(sample)) {
        return SpellLanguage::Turkish;
    }

    const std::string lower_tr = case_fold_turkish(word);
    if (turkish_function_words().count(lower_tr)) {
        return SpellLanguage::Turkish;
    }

    const std::string lower_en = case_fold_english(word);
    if (english_function_words().count(lower_en)) {
        return SpellLanguage::English;
    }

    // Without NLLanguageRecognizer: long context → EN function-word bias for the word.
    // Turkish orthography on `sample` already handled above.
    if (!context.empty() && context.size() >= 8) {
        if (context_has_english_function_words(context)) {
            return SpellLanguage::English;
        }
    }

    // No Apple NaturalLanguage on Windows — stay unknown; dict membership resolves later.
    return SpellLanguage::Unknown;
}

std::optional<SpellLanguage> LanguageTagger::detect_document_language(std::string_view text) const {
    if (encoding::codepoint_count(text) < 8) {
        return std::nullopt;
    }
    if (only_one_language_enabled()) {
        return turkish_enabled_ ? SpellLanguage::Turkish : SpellLanguage::English;
    }
    if (contains_turkish_chars(text)) {
        return SpellLanguage::Turkish;
    }

    // Function-word vote as NL substitute for document bias.
    int tr = 0;
    int en = 0;
    for (const auto& tok : Tokenizer::tokenize(text)) {
        if (turkish_function_words().count(case_fold_turkish(tok.text))) {
            ++tr;
        }
        if (english_function_words().count(case_fold_english(tok.text))) {
            ++en;
        }
    }
    if (en >= 2 && en > tr) {
        return SpellLanguage::English;
    }
    if (tr >= 2 && tr > en) {
        return SpellLanguage::Turkish;
    }
    return std::nullopt;
}

} // namespace bispell
