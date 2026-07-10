#include "test_assert.hpp"
#include "bispell/language_tagger.hpp"

using namespace bispell;

static void LanguageTagger_DetectsTurkishCharacters() {
    LanguageTagger tagger;
    EXPECT(tagger.detect("güzel") == SpellLanguage::Turkish);
    EXPECT(tagger.detect("şeker") == SpellLanguage::Turkish);
}

static void LanguageTagger_DetectsEnglishFunctionWords() {
    LanguageTagger tagger;
    EXPECT(tagger.detect("the") == SpellLanguage::English);
    EXPECT(tagger.detect("because") == SpellLanguage::Unknown); // not in small list
    EXPECT(tagger.detect("and") == SpellLanguage::English);
}

static void LanguageTagger_SingleLanguageMode() {
    LanguageTagger tr_only(true, false);
    EXPECT(tr_only.detect("hello") == SpellLanguage::Turkish);
    LanguageTagger en_only(false, true);
    EXPECT(en_only.detect("merhaba") == SpellLanguage::English);
}

static void LanguageTagger_DocumentBiasTurkishChars() {
    LanguageTagger tagger;
    auto lang = tagger.detect_document_language("Bugün hava çok güzel ve güneşli.");
    EXPECT(lang.has_value());
    EXPECT(*lang == SpellLanguage::Turkish);
}

static void LanguageTagger_DocumentBiasEnglishFunctionWords() {
    LanguageTagger tagger;
    // No Turkish orthography; multiple English function words.
    auto lang = tagger.detect_document_language("the and for with that this from have");
    EXPECT(lang.has_value());
    EXPECT(*lang == SpellLanguage::English);
}

// Word-level EN bias when surrounding context has English function words.
static void LanguageTagger_WordContextEnglishBias() {
    LanguageTagger tagger;
    // "xyzzy" is not a function word; long EN context should bias EN.
    EXPECT(tagger.detect("xyzzy", "the and for with that this from have") ==
           SpellLanguage::English);
    // Short / empty context → unknown
    EXPECT(tagger.detect("xyzzy") == SpellLanguage::Unknown);
}

int main() {
    LanguageTagger_DetectsTurkishCharacters();
    LanguageTagger_DetectsEnglishFunctionWords();
    LanguageTagger_SingleLanguageMode();
    LanguageTagger_DocumentBiasTurkishChars();
    LanguageTagger_DocumentBiasEnglishFunctionWords();
    LanguageTagger_WordContextEnglishBias();
    return bispell_test::finalize("test_language_tagger");
}
