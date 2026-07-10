#include "test_assert.hpp"
#include "bispell/tokenizer.hpp"
#include "bispell/encoding.hpp"

#include <string>
#include <vector>

using namespace bispell;

static std::vector<std::string> texts(const std::vector<TextToken>& toks) {
    std::vector<std::string> out;
    out.reserve(toks.size());
    for (const auto& t : toks) {
        out.push_back(t.text);
    }
    return out;
}

static void test_english_and_turkish() {
    const std::string text = u8"Hello d\u00fcnya, this is g\u00fczel!";
    const auto tokens = Tokenizer::tokenize(text);
    const auto t = texts(tokens);
    EXPECT_EQ(t.size(), 5u);
    if (t.size() == 5) {
        EXPECT_EQ(t[0], "Hello");
        EXPECT_EQ(t[1], u8"d\u00fcnya");
        EXPECT_EQ(t[2], "this");
        EXPECT_EQ(t[3], "is");
        EXPECT_EQ(t[4], u8"g\u00fczel");
    }
}

static void test_turkish_letters_inside_words() {
    const std::string text = u8"\u011f\u00fc\u015f\u0131\u00f6\u00e7 \u011e\u00dc\u015e\u0130\u00d6\u00c7";
    const auto tokens = Tokenizer::tokenize(text);
    EXPECT_EQ(tokens.size(), 2u);
    if (tokens.size() == 2) {
        EXPECT_EQ(tokens[0].text, u8"\u011f\u00fc\u015f\u0131\u00f6\u00e7");
        EXPECT_EQ(tokens[1].text, u8"\u011e\u00dc\u015e\u0130\u00d6\u00c7");
    }
}

static void test_should_skip() {
    EXPECT(Tokenizer::shouldSkipToken("a"));
    EXPECT(Tokenizer::shouldSkipToken("42"));
    EXPECT(Tokenizer::shouldSkipToken("foo_bar"));
    EXPECT(!Tokenizer::shouldSkipToken("hello"));
    EXPECT(!Tokenizer::shouldSkipToken("merhaba"));
    EXPECT(Tokenizer::shouldSkipToken("user@host"));
    EXPECT(Tokenizer::shouldSkipToken("https://example.com"));
    EXPECT(Tokenizer::shouldSkipToken("http"));
    EXPECT(Tokenizer::shouldSkipToken("Abc123def456ghi")); // >12, letters+digits
    EXPECT(!Tokenizer::shouldSkipToken("word2vec"));
}

static void test_utf16_ranges() {
    const std::string text = u8"\u015feker tea"; // şeker tea
    const auto tokens = Tokenizer::tokenize(text);
    EXPECT_EQ(tokens.size(), 2u);
    if (tokens.size() == 2) {
        EXPECT_EQ(encoding::utf8_slice_by_utf16(text, tokens[0].utf16_range), u8"\u015feker");
        EXPECT_EQ(encoding::utf8_slice_by_utf16(text, tokens[1].utf16_range), "tea");
        EXPECT_EQ(tokens[0].utf16_range.location, 0u);
        EXPECT_EQ(tokens[0].utf16_range.length, 5u);
        EXPECT_EQ(tokens[1].utf16_range.location, 6u);
        EXPECT_EQ(tokens[1].utf16_range.length, 3u);
    }
}

static void test_empty_and_emoji() {
    EXPECT(Tokenizer::tokenize("").empty());
    EXPECT(Tokenizer::tokenize("   \t\n").empty());

    // Emoji is not a word character; words on both sides still tokenize.
    const std::string text = u8"hello\U0001F600world";
    const auto tokens = Tokenizer::tokenize(text);
    EXPECT_EQ(tokens.size(), 2u);
    if (tokens.size() == 2) {
        EXPECT_EQ(tokens[0].text, "hello");
        EXPECT_EQ(tokens[1].text, "world");
        // hello = 5 utf16; emoji = 2; world starts at 7
        EXPECT_EQ(tokens[1].utf16_range.location, 7u);
    }
}

static void test_huge_line() {
    std::string huge;
    huge.reserve(200000);
    for (int i = 0; i < 10000; ++i) {
        huge += "word";
        huge += ' ';
    }
    huge += u8"d\u00fcnya";
    const auto tokens = Tokenizer::tokenize(huge);
    EXPECT_EQ(tokens.size(), 10001u);
    EXPECT_EQ(tokens.back().text, u8"d\u00fcnya");
}

static void test_apostrophe_trim() {
    const auto tokens = Tokenizer::tokenize("'hello' world");
    EXPECT_EQ(tokens.size(), 2u);
    if (tokens.size() >= 1) {
        EXPECT_EQ(tokens[0].text, "hello");
    }
}

int main() {
    test_english_and_turkish();
    test_turkish_letters_inside_words();
    test_should_skip();
    test_utf16_ranges();
    test_empty_and_emoji();
    test_huge_line();
    test_apostrophe_trim();
    return ::bispell_test::finalize("test_tokenizer");
}
