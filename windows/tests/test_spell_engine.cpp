#include "test_assert.hpp"
#include "bispell/bispell.hpp"

#include <algorithm>
#include <filesystem>
#include <string>
#include <vector>

using namespace bispell;

#ifndef BISPELL_DICT_DIR
#error "BISPELL_DICT_DIR must be defined"
#endif

static std::filesystem::path dict_dir() {
    return std::filesystem::path(BISPELL_DICT_DIR);
}

static SpellEngine make_engine(AppSettings settings = AppSettings::defaults()) {
    // Memory-only lexicon for hermetic tests.
    return SpellEngine::bundled(dict_dir(), settings, UserLexiconStore({}, false),
                                make_system_clock());
}

static std::vector<std::string> miss_words(const SpellCheckResult& r) {
    std::vector<std::string> w;
    w.reserve(r.misspellings.size());
    for (const auto& m : r.misspellings) {
        w.push_back(m.word);
    }
    return w;
}

static bool contains_word(const std::vector<std::string>& words, const std::string& w) {
    return std::find(words.begin(), words.end(), w) != words.end();
}

// --- Acceptance criteria mirrors of Swift SpellEngineTests ---

static void SpellEngine_EnglishTypoIsFlaggedWithSuggestion() {
    auto engine = make_engine();
    auto result = engine.check("I recieve mail today");
    auto words = miss_words(result);
    EXPECT(contains_word(words, "recieve"));
    const Misspelling* miss = nullptr;
    for (const auto& m : result.misspellings) {
        if (m.word == "recieve") {
            miss = &m;
            break;
        }
    }
    EXPECT(miss != nullptr);
    if (miss) {
        auto sug = engine.suggestions(miss->word, miss->language);
        EXPECT(!sug.empty());
    }
}

static void SpellEngine_CorrectEnglishReceiveNotFlagged() {
    auto engine = make_engine();
    auto result = engine.check("I receive mail today");
    EXPECT(!contains_word(miss_words(result), "receive"));
}

static void SpellEngine_TurkishTypoIsFlagged() {
    auto engine = make_engine();
    auto result = engine.check("merhabaa ve dünyya");
    auto words = miss_words(result);
    EXPECT(contains_word(words, "merhabaa"));
    EXPECT(contains_word(words, "dünyya"));
}

static void SpellEngine_CorrectTurkishCommonWord() {
    auto engine = make_engine();
    auto result = engine.check("merhaba dünya");
    for (const auto& m : result.misspellings) {
        // case-insensitive merhaba should not appear
        auto lower = case_fold_turkish(m.word);
        EXPECT(lower != "merhaba");
    }
}

static void SpellEngine_UserLexiconAcceptsCustomWord() {
    auto engine = make_engine();
    engine.add_to_dictionary("BiSpellXYZ");
    auto result = engine.check("BiSpellXYZ is cool");
    EXPECT(!contains_word(miss_words(result), "BiSpellXYZ"));
}

static void SpellEngine_DisabledReturnsNoIssues() {
    auto s = AppSettings::defaults();
    s.is_enabled = false;
    auto engine = make_engine(s);
    auto result = engine.check("recieve merhabaa");
    EXPECT(result.misspellings.empty());
}

static void SpellEngine_MixedSentenceFindsEnglishTypo() {
    auto engine = make_engine();
    auto result = engine.check("Bugün recieve etmek istiyorum");
    EXPECT(contains_word(miss_words(result), "recieve"));
}

static void SpellEngine_NearCaretOnlySkipsFarTokens() {
    auto engine = make_engine();
    std::string text = "recieve ";
    for (int i = 0; i < 50; ++i) {
        text += "word ";
    }
    text += "teh";
    const auto caret = encoding::utf16_length(text) - 1;
    CheckOptions opt;
    opt.caret_utf16 = caret;
    opt.near_caret_only = true;
    opt.window_radius = 20;
    auto result = engine.check(text, opt);
    EXPECT(!contains_word(miss_words(result), "recieve"));
}

static void SpellEngine_LanguageHeuristicsBias() {
    LanguageTagger tagger;
    EXPECT(tagger.detect("öğrenci") == SpellLanguage::Turkish);
    EXPECT(tagger.detect("the") == SpellLanguage::English);
    auto doc_tr = tagger.detect_document_language("ğüşıöç harfleri burada görünür.");
    EXPECT(doc_tr.has_value() && *doc_tr == SpellLanguage::Turkish);
    auto doc_en = tagger.detect_document_language("the and for with that this from have more");
    EXPECT(doc_en.has_value() && *doc_en == SpellLanguage::English);
}

static void SpellEngine_StrictInvalidUtf8Throws() {
    auto engine = make_engine();
    // Invalid UTF-8 continuation
    std::string bad = "hello\x80world";
    CheckOptions opt;
    opt.strict_utf8 = true;
    bool threw = false;
    try {
        engine.check(bad, opt);
    } catch (const Error& e) {
        threw = true;
        EXPECT(e.code() == ErrorCode::InvalidUtf8);
    }
    EXPECT(threw);

    // Replacement mode does not throw
    opt.strict_utf8 = false;
    auto r = engine.check(bad, opt);
    EXPECT(true); // completed
    (void)r;
}

static void SpellEngine_CAbiAndRaiiWrapper() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), /*lexicon*/ "", es);
    auto result = eng.check("I recieve mail today");
    bool found = false;
    for (const auto& m : result.misspellings) {
        if (m.word == "recieve") {
            found = true;
            auto sug = eng.suggestions(m.word, m.language);
            EXPECT(!sug.empty());
        }
    }
    EXPECT(found);

    eng.add_to_dictionary("BiSpellXYZ");
    auto r2 = eng.check("BiSpellXYZ");
    bool bad = false;
    for (const auto& m : r2.misspellings) {
        if (m.word == "BiSpellXYZ") bad = true;
    }
    EXPECT(!bad);

    // Disabled via settings
    es.is_enabled = false;
    eng.update_settings(es);
    auto r3 = eng.check("recieve merhabaa");
    EXPECT(r3.misspellings.empty());
}

static void SpellEngine_WithSuggestionsFills() {
    auto engine = make_engine();
    auto result = engine.check("I recieve mail today");
    EXPECT(!result.misspellings.empty());
    for (const auto& m : result.misspellings) {
        if (m.word == "recieve") {
            auto filled = engine.with_suggestions(m);
            EXPECT(!filled.suggestions.empty());
        }
    }
}

static void SpellEngine_InjectedFakeClock() {
    auto clock = std::make_shared<FakeClock>();
    auto engine = SpellEngine::bundled(dict_dir(), AppSettings::defaults(),
                                       UserLexiconStore({}, false), clock);
    // Clock is reachable; advance does not break check.
    clock->advance(std::chrono::seconds(5));
    auto r = engine.check("receive");
    EXPECT(!contains_word(miss_words(r), "receive"));
    EXPECT(engine.clock().now() == clock->now());
}

// Stemming path (English plural via Hunspell)
static void SpellEngine_CorrectEnglishPluralViaStemming() {
    auto engine = make_engine();
    auto result = engine.check("messages");
    EXPECT(!contains_word(miss_words(result), "messages"));
}

// Both languages disabled
static void SpellEngine_BothLanguagesDisabledEmpty() {
    AppSettings s = AppSettings::defaults();
    s.turkish_enabled = false;
    s.english_enabled = false;
    auto engine = make_engine(s);
    auto result = engine.check("recieve merhabaa");
    EXPECT(result.misspellings.empty());
}

// update_settings re-gates
static void SpellEngine_UpdateSettingsDisables() {
    auto engine = make_engine();
    auto before = engine.check("recieve");
    EXPECT(contains_word(miss_words(before), "recieve"));
    AppSettings s = AppSettings::defaults();
    s.is_enabled = false;
    engine.update_settings(s);
    auto after = engine.check("recieve");
    EXPECT(after.misspellings.empty());
}

// nearest_misspelling prefers caret-containing range
static void SpellEngine_NearestMisspelling() {
    auto engine = make_engine();
    auto result = engine.check("recieve mail teh");
    EXPECT(result.misspellings.size() >= 1u);
    if (!result.misspellings.empty()) {
        const auto& first = result.misspellings.front();
        const auto mid = first.utf16_range.location + first.utf16_range.length / 2;
        auto near = engine.nearest_misspelling(result.misspellings, mid);
        EXPECT(near.has_value());
        if (near) {
            EXPECT(near->word == first.word);
        }
    }
}

int main() {
    SpellEngine_EnglishTypoIsFlaggedWithSuggestion();
    SpellEngine_CorrectEnglishReceiveNotFlagged();
    SpellEngine_TurkishTypoIsFlagged();
    SpellEngine_CorrectTurkishCommonWord();
    SpellEngine_UserLexiconAcceptsCustomWord();
    SpellEngine_DisabledReturnsNoIssues();
    SpellEngine_MixedSentenceFindsEnglishTypo();
    SpellEngine_NearCaretOnlySkipsFarTokens();
    SpellEngine_LanguageHeuristicsBias();
    SpellEngine_StrictInvalidUtf8Throws();
    SpellEngine_CAbiAndRaiiWrapper();
    SpellEngine_WithSuggestionsFills();
    SpellEngine_InjectedFakeClock();
    SpellEngine_CorrectEnglishPluralViaStemming();
    SpellEngine_BothLanguagesDisabledEmpty();
    SpellEngine_UpdateSettingsDisables();
    SpellEngine_NearestMisspelling();
    return bispell_test::finalize("test_spell_engine");
}
