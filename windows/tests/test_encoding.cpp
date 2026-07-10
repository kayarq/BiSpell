#include "test_assert.hpp"
#include "bispell/encoding.hpp"
#include "bispell/case_fold.hpp"

#include <string>

using namespace bispell;
using namespace bispell::encoding;

static void test_ascii_utf16() {
    EXPECT_EQ(utf16_length("hello"), 5u);
    EXPECT(is_valid_utf8("hello"));
}

static void test_turkish_utf16() {
    // ş is U+015F → 1 UTF-16 unit; 😀 is U+1F600 → 2
    const std::string s = u8"şeker";
    EXPECT_EQ(utf16_length(s), 5u);
    auto spans = decode_utf8(s);
    EXPECT_EQ(spans.size(), 5u);
    EXPECT_EQ(static_cast<unsigned>(spans[0].cp), 0x015Fu);
}

static void test_emoji_adjacent_ranges() {
    // "hi😀yo" — emoji takes 2 UTF-16 units
    const std::string s = u8"hi\U0001F600yo";
    EXPECT_EQ(utf16_length(s), 6u); // 2 + 2 + 2
    auto spans = decode_utf8(s);
    EXPECT_EQ(spans.size(), 5u);
    EXPECT_EQ(spans[2].utf16_length, 2u);
    EXPECT_EQ(spans[2].utf16_offset, 2u);

    Utf16Range r{0, 2};
    EXPECT_EQ(utf8_slice_by_utf16(s, r), "hi");
    Utf16Range r2{4, 2};
    EXPECT_EQ(utf8_slice_by_utf16(s, r2), "yo");
}

static void test_invalid_utf8_replacement() {
    std::string bad;
    bad.push_back(static_cast<char>(0xFFu));
    bad += "a";
    bool ok = true;
    auto spans = decode_utf8(bad, true, &ok);
    EXPECT(!ok);
    EXPECT_EQ(spans.size(), 2u);
    EXPECT_EQ(static_cast<unsigned>(spans[0].cp), 0xFFFDu);

    ok = true;
    auto strict = decode_utf8(bad, false, &ok);
    EXPECT(!ok);
    EXPECT(strict.empty());
    EXPECT(!is_valid_utf8(bad));
}

static void test_turkish_case_fold() {
    // I → ı, İ → i under Turkish
    EXPECT_EQ(case_fold_turkish(u8"I"), u8"\u0131");
    EXPECT_EQ(case_fold_turkish(u8"\u0130"), "i"); // İ
    EXPECT_EQ(case_fold_turkish("ISLIK"), u8"\u0131sl\u0131k"); // ıslık
    EXPECT_EQ(case_fold_english("I"), "i");
    EXPECT_EQ(case_fold_english("RECEIVE"), "receive");
    EXPECT_EQ(case_fold(u8"\u011e\u00dc\u015e\u0130\u00d6\u00c7", SpellLanguage::Turkish),
              u8"\u011f\u00fc\u015fi\u00f6\u00e7");
}

static void test_turkish_case_fold_full() {
    // ĞÜŞİÖÇ → ğüşiöç
    const std::string upper = u8"\u011e\u00dc\u015e\u0130\u00d6\u00c7";
    const std::string lower = case_fold_turkish(upper);
    EXPECT_EQ(lower, u8"\u011f\u00fc\u015fi\u00f6\u00e7");
}

int main() {
    test_ascii_utf16();
    test_turkish_utf16();
    test_emoji_adjacent_ranges();
    test_invalid_utf8_replacement();
    test_turkish_case_fold();
    test_turkish_case_fold_full();
    return ::bispell_test::finalize("test_encoding");
}
