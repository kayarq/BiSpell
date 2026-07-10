#include "test_assert.hpp"
#include "bispell/paths.hpp"
#include "bispell/user_lexicon.hpp"

#include <filesystem>
#include <fstream>
#include <string>

using namespace bispell;

static void Lexicon_AddAndIgnore() {
    UserLexicon lex;
    EXPECT(!lex.accepts("FooBar"));
    lex.add_word("FooBar");
    EXPECT(lex.accepts("FooBar"));
    EXPECT(lex.accepts("foobar"));
    lex.ignore_in_app("Baz", "com.example.app");
    std::string app = "com.example.app";
    std::string other = "com.other";
    EXPECT(lex.ignores("Baz", &app));
    EXPECT(!lex.ignores("Baz", &other));
}

static void Lexicon_RemoveWordCaseInsensitive() {
    UserLexicon lex;
    lex.add_word("FooBar");
    lex.remove_word("foobar");
    EXPECT(!lex.accepts("FooBar"));
}

static void Lexicon_UnignoreClearsGlobalAndPerApp() {
    UserLexicon lex;
    lex.ignore_once("Baz");
    lex.ignore_in_app("Baz", "com.example.app");
    lex.ignore_in_app("Qux", "com.example.app");
    lex.unignore("baz");
    EXPECT(!lex.ignores("Baz", nullptr));
    std::string app = "com.example.app";
    EXPECT(!lex.ignores("Baz", &app));
    EXPECT(lex.ignores("Qux", &app));
}

static void Lexicon_JsonRoundTrip() {
    UserLexicon lex;
    lex.add_word("BiSpellXYZ");
    lex.ignore_once("tmp");
    lex.ignore_in_app("appword", "com.example");
    const std::string json = lex.to_json();
    bool ok = false;
    auto back = UserLexicon::from_json(json, &ok);
    EXPECT(ok);
    EXPECT(back.accepts("BiSpellXYZ"));
    EXPECT(back.accepts("tmp"));
    std::string app = "com.example";
    EXPECT(back.ignores("appword", &app));
}

static void Lexicon_StoreInjectedPath() {
    const auto dir = std::filesystem::temp_directory_path() / "bispell_lex_test";
    std::filesystem::create_directories(dir);
    const auto path = dir / "user-lexicon.json";
    std::filesystem::remove(path);

    {
        UserLexiconStore store(path, true);
        store.update([](UserLexicon& lex) { lex.add_word("PersistMe"); });
    }
    {
        UserLexiconStore store(path, true);
        EXPECT(store.current().accepts("PersistMe"));
    }

    // Process override path helper
    paths::set_config_dir_override(dir);
    EXPECT(paths::default_lexicon_path().filename() == "user-lexicon.json");
    EXPECT(paths::default_settings_path().filename() == "settings.json");
    EXPECT(paths::default_settings_path().parent_path() == paths::default_lexicon_path().parent_path());
    paths::clear_config_dir_override();

    std::filesystem::remove_all(dir);
}

static void Lexicon_MemoryOnlyNoPath() {
    UserLexiconStore store({}, false);
    store.update([](UserLexicon& lex) { lex.add_word("MemOnly"); });
    EXPECT(store.current().accepts("MemOnly"));
    EXPECT(store.path().empty());
}

int main() {
    Lexicon_AddAndIgnore();
    Lexicon_RemoveWordCaseInsensitive();
    Lexicon_UnignoreClearsGlobalAndPerApp();
    Lexicon_JsonRoundTrip();
    Lexicon_StoreInjectedPath();
    Lexicon_MemoryOnlyNoPath();
    return bispell_test::finalize("test_lexicon");
}
