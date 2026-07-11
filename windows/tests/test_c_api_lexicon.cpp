#include "test_assert.hpp"
#include "bispell/c_api.h"
#include "bispell/engine.hpp"

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

static bool contains(const std::vector<std::string>& v, const std::string& w) {
    return std::find(v.begin(), v.end(), w) != v.end();
}

static void CApi_NullArgsMatchSiblings() {
    char** list = reinterpret_cast<char**>(1);
    size_t count = 99;

    EXPECT(bispell_engine_unignore_word(nullptr, "x") != 0);
    EXPECT(std::string(bispell_last_error()).find("null") != std::string::npos);

    EXPECT(bispell_engine_list_added_words(nullptr, &list, &count) != 0);
    EXPECT(bispell_engine_list_ignored_words(nullptr, &list, &count) != 0);

    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), /*lexicon*/ "", es);
    bispell_engine* raw = eng.get();

    EXPECT(bispell_engine_unignore_word(raw, nullptr) != 0);
    EXPECT(bispell_engine_list_added_words(raw, nullptr, &count) != 0);
    EXPECT(bispell_engine_list_added_words(raw, &list, nullptr) != 0);
    EXPECT(bispell_engine_list_ignored_words(raw, nullptr, &count) != 0);
    EXPECT(bispell_engine_list_ignored_words(raw, &list, nullptr) != 0);

    // Sibling null-arg style (parity smoke)
    EXPECT(bispell_engine_add_to_dictionary(nullptr, "x") != 0);
    EXPECT(bispell_engine_ignore_word(nullptr, "x") != 0);
    EXPECT(bispell_engine_remove_from_dictionary(nullptr, "x") != 0);
}

static void CApi_EmptyListsAreNullZeroRc() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), "", es);

    char** list = reinterpret_cast<char**>(0x1);
    size_t count = 42;
    EXPECT(bispell_engine_list_added_words(eng.get(), &list, &count) == 0);
    EXPECT(list == nullptr);
    EXPECT(count == 0u);

    list = reinterpret_cast<char**>(0x1);
    count = 42;
    EXPECT(bispell_engine_list_ignored_words(eng.get(), &list, &count) == 0);
    EXPECT(list == nullptr);
    EXPECT(count == 0u);

    auto added = eng.list_added_words();
    auto ignored = eng.list_ignored_words();
    EXPECT(added.empty());
    EXPECT(ignored.empty());
}

static void CApi_AddListRemoveListEmpty() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), "", es);

    eng.add_to_dictionary("ZebraLex");
    eng.add_to_dictionary("AlphaLex");
    eng.add_to_dictionary("MidLex");

    auto added = eng.list_added_words();
    EXPECT(added.size() == 3u);
    EXPECT(added[0] == "AlphaLex");
    EXPECT(added[1] == "MidLex");
    EXPECT(added[2] == "ZebraLex");
    EXPECT(contains(added, "AlphaLex"));
    EXPECT(contains(added, "ZebraLex"));

    // Live check: custom words accepted
    auto r = eng.check("AlphaLex MidLex ZebraLex");
    EXPECT(r.misspellings.empty());

    eng.remove_from_dictionary("MidLex");
    added = eng.list_added_words();
    EXPECT(added.size() == 2u);
    EXPECT(!contains(added, "MidLex"));
    EXPECT(contains(added, "AlphaLex"));
    EXPECT(contains(added, "ZebraLex"));

    eng.remove_from_dictionary("AlphaLex");
    eng.remove_from_dictionary("ZebraLex");
    added = eng.list_added_words();
    EXPECT(added.empty());
}

static void CApi_IgnoreListUnignoreListEmpty() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), "", es);

    eng.ignore_word("ignoreZ");
    eng.ignore_word("ignoreA");

    auto ignored = eng.list_ignored_words();
    EXPECT(ignored.size() == 2u);
    EXPECT(ignored[0] == "ignoreA");
    EXPECT(ignored[1] == "ignoreZ");

    // Ignored word not flagged
    auto r = eng.check("ignoreA is fine");
    bool found = false;
    for (const auto& m : r.misspellings) {
        if (m.word == "ignoreA") found = true;
    }
    EXPECT(!found);

    // Case-insensitive unignore (core parity)
    eng.unignore_word("IGNOREA");
    ignored = eng.list_ignored_words();
    EXPECT(ignored.size() == 1u);
    EXPECT(ignored[0] == "ignoreZ");

    eng.unignore_word("ignoreZ");
    ignored = eng.list_ignored_words();
    EXPECT(ignored.empty());
}

static void CApi_RawListOwnershipAndSort() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), "", es);
    eng.add_to_dictionary("ccc");
    eng.add_to_dictionary("aaa");
    eng.add_to_dictionary("bbb");

    char** list = nullptr;
    size_t count = 0;
    EXPECT(bispell_engine_list_added_words(eng.get(), &list, &count) == 0);
    EXPECT(count == 3u);
    EXPECT(list != nullptr);
    EXPECT(std::string(list[0]) == "aaa");
    EXPECT(std::string(list[1]) == "bbb");
    EXPECT(std::string(list[2]) == "ccc");
    bispell_string_list_free(list, count);
}

static void CApi_LiveAfterMutationsViaC() {
    EngineSettings es;
    auto eng = Engine::create(dict_dir().string(), "", es);
    bispell_engine* raw = eng.get();

    EXPECT(bispell_engine_add_to_dictionary(raw, "LiveAdd") == 0);
    EXPECT(bispell_engine_ignore_word(raw, "LiveIgn") == 0);

    char** added = nullptr;
    size_t n_added = 0;
    EXPECT(bispell_engine_list_added_words(raw, &added, &n_added) == 0);
    EXPECT(n_added == 1u);
    EXPECT(std::string(added[0]) == "LiveAdd");
    bispell_string_list_free(added, n_added);

    char** ign = nullptr;
    size_t n_ign = 0;
    EXPECT(bispell_engine_list_ignored_words(raw, &ign, &n_ign) == 0);
    EXPECT(n_ign == 1u);
    EXPECT(std::string(ign[0]) == "LiveIgn");
    bispell_string_list_free(ign, n_ign);

    EXPECT(bispell_engine_remove_from_dictionary(raw, "LiveAdd") == 0);
    EXPECT(bispell_engine_unignore_word(raw, "LiveIgn") == 0);

    added = reinterpret_cast<char**>(1);
    n_added = 9;
    EXPECT(bispell_engine_list_added_words(raw, &added, &n_added) == 0);
    EXPECT(added == nullptr && n_added == 0u);

    ign = reinterpret_cast<char**>(1);
    n_ign = 9;
    EXPECT(bispell_engine_list_ignored_words(raw, &ign, &n_ign) == 0);
    EXPECT(ign == nullptr && n_ign == 0u);
}

int main() {
    CApi_NullArgsMatchSiblings();
    CApi_EmptyListsAreNullZeroRc();
    CApi_AddListRemoveListEmpty();
    CApi_IgnoreListUnignoreListEmpty();
    CApi_RawListOwnershipAndSort();
    CApi_LiveAfterMutationsViaC();
    return bispell_test::finalize("test_c_api_lexicon");
}
