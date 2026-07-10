#include "test_assert.hpp"
#include "bispell/dictionary.hpp"
#include "bispell/error.hpp"

#include <algorithm>
#include <filesystem>
#include <optional>
#include <string>

using namespace bispell;

#ifndef BISPELL_DICT_DIR
#error "BISPELL_DICT_DIR must be defined"
#endif

static std::filesystem::path dict_dir() {
    return std::filesystem::path(BISPELL_DICT_DIR);
}

// Case names mirror Swift XCTest style (Dictionary.*) for cross-platform mapping.

static void Dictionary_MissingPathErrors() {
    bool threw = false;
    try {
        HunspellDictionary::load("/nonexistent/path/en_US.dic", SpellLanguage::English);
    } catch (const Error& e) {
        threw = true;
        EXPECT(e.code() == ErrorCode::FileNotFound);
        EXPECT(!std::string(e.what()).empty());
    }
    EXPECT(threw);

    threw = false;
    try {
        DictionaryLoader::resolve_bundled("/tmp/definitely-missing-bispell-dicts");
    } catch (const Error& e) {
        threw = true;
        EXPECT(e.code() == ErrorCode::FileNotFound);
    }
    EXPECT(threw);

    threw = false;
    try {
        HunspellDictionary::load("", SpellLanguage::English);
    } catch (const Error& e) {
        threw = true;
        EXPECT(e.code() == ErrorCode::EmptyPath);
    }
    EXPECT(threw);
}

static void Dictionary_EnglishContainsReceive() {
    const auto path = dict_dir() / "en_US.dic";
    auto en = HunspellDictionary::load(path, SpellLanguage::English);
    EXPECT(en.word_count() > 10000u);
    // Swift/plan alias
    EXPECT(en.wordCount() == en.word_count());
    EXPECT(en.contains("receive"));
    EXPECT(en.contains("Receive"));
    EXPECT(!en.contains("recieve"));
    EXPECT(en.contains("message"));
}

static void Dictionary_LightEnglishStemMessages() {
    const auto path = dict_dir() / "en_US.dic";
    auto en = HunspellDictionary::load(path, SpellLanguage::English);
    // Light English stem: messages → message
    EXPECT(en.contains("messages"));
    auto stems = HunspellDictionary::stem_candidates("messages", SpellLanguage::English);
    EXPECT(std::find(stems.begin(), stems.end(), "message") != stems.end());
    // camelCase alias matches snake_case
    auto stems_alias = HunspellDictionary::stemCandidates("messages", SpellLanguage::English);
    EXPECT(stems_alias == stems);
}

static void Dictionary_SuggestionsForRecieve() {
    const auto path = dict_dir() / "en_US.dic";
    auto en = HunspellDictionary::load(path, SpellLanguage::English);
    auto sug = en.suggestions("recieve", 5);
    EXPECT(!sug.empty());
    bool has_receive = false;
    for (const auto& s : sug) {
        if (s == "receive" || HunspellDictionary::normalize(s, SpellLanguage::English) == "receive") {
            has_receive = true;
        }
    }
    EXPECT(has_receive);
    // Known-good word → no suggestions
    EXPECT(en.suggestions("receive", 5).empty());
}

static void Dictionary_TurkishMerhaba() {
    const auto path = dict_dir() / "tr.dic";
    auto tr = HunspellDictionary::load(path, SpellLanguage::Turkish);
    EXPECT(tr.word_count() > 10000u);
    EXPECT(tr.wordCount() == tr.word_count());
    EXPECT(tr.contains("merhaba"));
    EXPECT(tr.contains("Merhaba"));
    EXPECT(!tr.contains("merhabaa"));
    EXPECT(!tr.contains("xyzzytrtypo999"));
}

static void Dictionary_BundledLoader() {
    auto bundled = DictionaryLoader::load_bundled(dict_dir());
    EXPECT(bundled.english.contains("receive"));
    EXPECT(bundled.turkish.contains("merhaba"));
}

static void Dictionary_OptionalCanonical() {
    // "Dog" lowercases to "dog" → optional canonical holds surface form
    // "cat" is already lower → nullopt
    const char* data =
        "3\n"
        "cat\n"
        "Dog\n"
        "receive\n";
    auto d = HunspellDictionary::load_from_string(data, SpellLanguage::English, "inline");
    EXPECT_EQ(d.wordCount(), 3u);
    EXPECT(d.contains("cat"));
    EXPECT(d.contains("dog"));
    EXPECT(d.contains("DOG"));
    EXPECT(d.contains("receive"));

    const auto& entries = d.entries();
    auto cat = entries.find("cat");
    auto dog = entries.find("dog");
    EXPECT(cat != entries.end());
    EXPECT(dog != entries.end());
    if (cat != entries.end()) {
        EXPECT(!cat->second.canonical.has_value());
    }
    if (dog != entries.end()) {
        EXPECT(dog->second.canonical.has_value());
        EXPECT_EQ(*dog->second.canonical, "Dog");
    }

    // Suggestions should surface optional canonical when present
    auto sug = d.suggestions("recieve", 5);
    EXPECT(!sug.empty());
}

static void Dictionary_EmptyDictionaryError() {
    bool threw = false;
    try {
        HunspellDictionary::load_from_string("# only comment\n", SpellLanguage::English, "empty");
    } catch (const Error& e) {
        threw = true;
        EXPECT(e.code() == ErrorCode::EmptyDictionary);
    }
    EXPECT(threw);
}

int main() {
    Dictionary_MissingPathErrors();
    Dictionary_OptionalCanonical();
    Dictionary_EmptyDictionaryError();
    Dictionary_EnglishContainsReceive();
    Dictionary_LightEnglishStemMessages();
    Dictionary_SuggestionsForRecieve();
    Dictionary_TurkishMerhaba();
    Dictionary_BundledLoader();
    return ::bispell_test::finalize("test_dictionary");
}
