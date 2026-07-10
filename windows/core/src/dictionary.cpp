#include "bispell/dictionary.hpp"
#include "bispell/case_fold.hpp"
#include "bispell/encoding.hpp"

#include <algorithm>
#include <fstream>
#include <limits>
#include <sstream>
#include <unordered_set>

namespace bispell {
namespace {

std::string trim_ws(std::string_view s) {
    std::size_t b = 0;
    while (b < s.size() && (s[b] == ' ' || s[b] == '\t' || s[b] == '\r' || s[b] == '\f')) {
        ++b;
    }
    std::size_t e = s.size();
    while (e > b && (s[e - 1] == ' ' || s[e - 1] == '\t' || s[e - 1] == '\r' || s[e - 1] == '\f')) {
        --e;
    }
    return std::string(s.substr(b, e - b));
}

bool is_all_digits(std::string_view s) {
    if (s.empty()) {
        return false;
    }
    for (char c : s) {
        if (c < '0' || c > '9') {
            return false;
        }
    }
    return true;
}

std::vector<char32_t> to_codepoints(std::string_view s) {
    auto spans = encoding::decode_utf8(s, true, nullptr);
    std::vector<char32_t> out;
    out.reserve(spans.size());
    for (const auto& sp : spans) {
        out.push_back(sp.cp);
    }
    return out;
}

int common_prefix_length(const std::vector<char32_t>& a, const std::vector<char32_t>& b) {
    int i = 0;
    const int n = static_cast<int>(std::min(a.size(), b.size()));
    while (i < n && a[static_cast<std::size_t>(i)] == b[static_cast<std::size_t>(i)]) {
        ++i;
    }
    return i;
}

// Damerau–Levenshtein with early exit (Swift parity).
int damerau_levenshtein(const std::vector<char32_t>& A, const std::vector<char32_t>& B, int limit) {
    const int n = static_cast<int>(A.size());
    const int m = static_cast<int>(B.size());
    if (std::abs(n - m) > limit) {
        return limit + 1;
    }
    if (n == 0) {
        return m;
    }
    if (m == 0) {
        return n;
    }

    std::vector<int> prev_prev(static_cast<std::size_t>(m + 1), 0);
    std::vector<int> prev(static_cast<std::size_t>(m + 1));
    std::vector<int> cur(static_cast<std::size_t>(m + 1), 0);
    for (int j = 0; j <= m; ++j) {
        prev[static_cast<std::size_t>(j)] = j;
    }

    for (int i = 1; i <= n; ++i) {
        cur[0] = i;
        int row_min = cur[0];
        for (int j = 1; j <= m; ++j) {
            const int cost = (A[static_cast<std::size_t>(i - 1)] == B[static_cast<std::size_t>(j - 1)]) ? 0 : 1;
            int val = std::min({prev[static_cast<std::size_t>(j)] + 1,
                                cur[static_cast<std::size_t>(j - 1)] + 1,
                                prev[static_cast<std::size_t>(j - 1)] + cost});
            if (i > 1 && j > 1 && A[static_cast<std::size_t>(i - 1)] == B[static_cast<std::size_t>(j - 2)] &&
                A[static_cast<std::size_t>(i - 2)] == B[static_cast<std::size_t>(j - 1)]) {
                val = std::min(val, prev_prev[static_cast<std::size_t>(j - 2)] + 1);
            }
            cur[static_cast<std::size_t>(j)] = val;
            row_min = std::min(row_min, val);
        }
        if (row_min > limit) {
            return limit + 1;
        }
        prev_prev.swap(prev);
        prev.swap(cur);
        std::fill(cur.begin(), cur.end(), 0);
    }
    return prev[static_cast<std::size_t>(m)];
}

// Alphabet: a-z + çğıöşü (Swift generateRestrictedEdits)
const char32_t kAlphabet[] = {
    U'a', U'b', U'c', U'd', U'e', U'f', U'g', U'h', U'i', U'j', U'k', U'l', U'm',
    U'n', U'o', U'p', U'q', U'r', U's', U't', U'u', U'v', U'w', U'x', U'y', U'z',
    0x00E7u, // ç
    0x011Fu, // ğ
    0x0131u, // ı
    0x00F6u, // ö
    0x015Fu, // ş
    0x00FCu, // ü
};
constexpr std::size_t kAlphabetSize = sizeof(kAlphabet) / sizeof(kAlphabet[0]);

std::unordered_set<std::string> generate_restricted_edits(const std::vector<char32_t>& chars) {
    std::unordered_set<std::string> result;
    const int n = static_cast<int>(chars.size());
    if (n <= 0) {
        return result;
    }

    // deletes — build by splicing halves (no vector::erase; avoids GCC -Warray-bounds notes)
    for (int i = 0; i < n; ++i) {
        std::vector<char32_t> c;
        c.reserve(static_cast<std::size_t>(n - 1));
        c.insert(c.end(), chars.begin(), chars.begin() + i);
        c.insert(c.end(), chars.begin() + (i + 1), chars.end());
        result.insert(encoding::encode_utf8(c));
    }
    // transposes
    if (n > 1) {
        for (int i = 0; i < n - 1; ++i) {
            std::vector<char32_t> c = chars;
            std::swap(c[static_cast<std::size_t>(i)], c[static_cast<std::size_t>(i + 1)]);
            result.insert(encoding::encode_utf8(c));
        }
    }
    // replaces
    for (int i = 0; i < n; ++i) {
        for (std::size_t a = 0; a < kAlphabetSize; ++a) {
            const char32_t L = kAlphabet[a];
            if (L == chars[static_cast<std::size_t>(i)]) {
                continue;
            }
            std::vector<char32_t> c = chars;
            c[static_cast<std::size_t>(i)] = L;
            result.insert(encoding::encode_utf8(c));
        }
    }
    // inserts (only for short words, Swift: count <= 9) — build without vector::insert
    if (n <= 9) {
        for (int i = 0; i <= n; ++i) {
            for (std::size_t a = 0; a < kAlphabetSize; ++a) {
                std::vector<char32_t> c;
                c.reserve(static_cast<std::size_t>(n + 1));
                c.insert(c.end(), chars.begin(), chars.begin() + i);
                c.push_back(kAlphabet[a]);
                c.insert(c.end(), chars.begin() + i, chars.end());
                result.insert(encoding::encode_utf8(c));
            }
        }
    }
    return result;
}

bool starts_with(std::string_view s, std::string_view prefix) {
    return s.size() >= prefix.size() && s.substr(0, prefix.size()) == prefix;
}

bool ends_with(std::string_view s, std::string_view suffix) {
    return s.size() >= suffix.size() && s.substr(s.size() - suffix.size()) == suffix;
}

} // namespace

std::string HunspellDictionary::normalize(std::string_view word, SpellLanguage language) {
    return case_fold(word, language);
}

std::vector<std::string> HunspellDictionary::stem_candidates(std::string_view lower,
                                                             SpellLanguage language) {
    std::vector<std::string> out;
    // Swift uses Character (scalar) count for min length, not UTF-8 byte size.
    auto add = [&](std::string s) {
        if (encoding::codepoint_count(s) >= 2 &&
            std::find(out.begin(), out.end(), s) == out.end()) {
            out.push_back(std::move(s));
        }
    };

    switch (language) {
    case SpellLanguage::English:
    case SpellLanguage::Unknown: {
        static const char* suffixes[] = {"'s", "s",  "es",  "ed",   "ing",  "ly",   "er",
                                         "est", "ness", "ment", "tion", "able", "ible"};
        std::string current(lower);
        for (int pass = 0; pass < 3; ++pass) {
            bool stripped = false;
            for (const char* suf : suffixes) {
                const std::string_view sv(suf);
                // current.count > suf.count + 2  (Swift Character / scalar count)
                if (encoding::codepoint_count(current) > encoding::codepoint_count(sv) + 2 &&
                    ends_with(current, sv)) {
                    std::string base = current.substr(0, current.size() - sv.size());
                    add(base);
                    if (sv == "es") {
                        add(base + "e");
                    }
                    if (sv == "ing") {
                        add(base);
                        add(base + "e");
                        // doubled consonant: runn+ing → run (scalar walk on base)
                        const auto base_cps = to_codepoints(base);
                        if (base_cps.size() > 2 &&
                            base_cps.back() == base_cps[base_cps.size() - 2]) {
                            std::vector<char32_t> stripped_cps(base_cps.begin(),
                                                               base_cps.end() - 1);
                            add(encoding::encode_utf8(stripped_cps));
                        }
                    }
                    if (sv == "ed") {
                        add(base);
                        add(base + "e");
                    }
                    current = base;
                    stripped = true;
                    break;
                }
            }
            if (!stripped) {
                break;
            }
        }
        if (ends_with(lower, "ies") && encoding::codepoint_count(lower) > 4) {
            add(std::string(lower.substr(0, lower.size() - 3)) + "y");
        }
        break;
    }
    case SpellLanguage::Turkish: {
        static const char* suffixes[] = {
            "makta", "mekte", "acak", "ecek", "iyor", "uyor", "üyor", "ıyor",
            "lar",   "ler",   "den",  "dan",  "ten",  "tan",  "nin",  "nın",
            "nun",   "nün",   "sin",  "sın",  "sun",  "sün",  "yiz",  "yız",
            "yuz",   "yüz",   "dir",  "dır",  "dur",  "dür",  "tir",  "tır",
            "tur",   "tür",   "in",   "ın",   "un",   "ün",   "im",   "ım",
            "um",    "üm",    "de",   "da",   "te",   "ta",   "ki",   "mı",
            "mi",    "mu",    "mü"};
        std::string current(lower);
        for (int pass = 0; pass < 4; ++pass) {
            bool stripped = false;
            for (const char* suf : suffixes) {
                const std::string_view sv(suf);
                const auto cur_cps = encoding::codepoint_count(current);
                const auto suf_cps = encoding::codepoint_count(sv);
                if (cur_cps > suf_cps + 2 && ends_with(current, sv)) {
                    // drop last suf.size() UTF-8 bytes (suffixes stored as UTF-8)
                    std::string base = current.substr(0, current.size() - sv.size());
                    add(base);
                    current = std::move(base);
                    stripped = true;
                    break;
                }
            }
            if (!stripped) {
                break;
            }
        }
        break;
    }
    }
    return out;
}

HunspellDictionary HunspellDictionary::load(const std::filesystem::path& path,
                                            SpellLanguage language) {
    if (path.empty()) {
        throw Error(ErrorCode::EmptyPath, "Dictionary path is empty");
    }
    std::error_code ec;
    if (!std::filesystem::exists(path, ec)) {
        throw Error(ErrorCode::FileNotFound, "Missing dictionary file", path.string());
    }
    std::ifstream in(path, std::ios::binary);
    if (!in) {
        throw Error(ErrorCode::FileUnreadable, "Cannot open dictionary file", path.string());
    }
    std::ostringstream ss;
    ss << in.rdbuf();
    if (!in.good() && !in.eof()) {
        throw Error(ErrorCode::FileUnreadable, "Failed reading dictionary file", path.string());
    }
    return load_from_string(ss.str(), language, path.string());
}

HunspellDictionary HunspellDictionary::load_from_string(std::string_view data,
                                                        SpellLanguage language,
                                                        std::string path_for_errors) {
    // Require valid UTF-8 for dictionary files (structured error).
    if (!encoding::is_valid_utf8(data)) {
        throw Error(ErrorCode::InvalidUtf8, "Dictionary is not valid UTF-8",
                    std::move(path_for_errors));
    }

    HunspellDictionary dict;
    dict.language_ = language;
    std::int32_t order = 0;
    bool first_line = true;

    std::size_t pos = 0;
    const std::size_t n = data.size();
    auto next_line = [&]() -> std::string_view {
        if (pos >= n) {
            return {};
        }
        const std::size_t start = pos;
        while (pos < n && data[pos] != '\n' && data[pos] != '\r') {
            ++pos;
        }
        std::string_view line = data.substr(start, pos - start);
        if (pos < n && data[pos] == '\r') {
            ++pos;
        }
        if (pos < n && data[pos] == '\n') {
            ++pos;
        }
        return line;
    };

    while (pos < n) {
        std::string_view line = next_line();
        std::string raw = trim_ws(line);
        if (raw.empty() || starts_with(raw, "#")) {
            continue;
        }
        if (first_line) {
            first_line = false;
            if (is_all_digits(raw)) {
                continue;
            }
        }

        // stem = before '/' then before tab
        std::string_view stem_view = raw;
        const auto slash = stem_view.find('/');
        if (slash != std::string_view::npos) {
            stem_view = stem_view.substr(0, slash);
        }
        const auto tab = stem_view.find('\t');
        if (tab != std::string_view::npos) {
            stem_view = stem_view.substr(0, tab);
        }
        std::string word = trim_ws(stem_view);
        if (word.empty()) {
            continue;
        }

        const std::string lower = normalize(word, language);
        if (dict.entries_.find(lower) == dict.entries_.end()) {
            Entry e;
            e.order = order;
            // optional: set only when surface form differs from lowercased key
            if (word != lower) {
                e.canonical = word;
            }
            dict.entries_.emplace(lower, std::move(e));
            ++order;
        }
    }

    if (dict.entries_.empty()) {
        throw Error(ErrorCode::EmptyDictionary, "Dictionary contained no stems",
                    std::move(path_for_errors));
    }
    return dict;
}

bool HunspellDictionary::contains(std::string_view word) const {
    const std::string lower = normalize(word, language_);
    if (entries_.find(lower) != entries_.end()) {
        return true;
    }
    for (const auto& variant : stem_candidates(lower, language_)) {
        if (entries_.find(variant) != entries_.end()) {
            return true;
        }
    }
    return false;
}

std::vector<std::string> HunspellDictionary::suggestions(std::string_view word, int limit) const {
    if (limit <= 0) {
        return {};
    }
    if (contains(word)) {
        return {};
    }
    const std::string lower = normalize(word, language_);
    if (encoding::codepoint_count(lower) < 2) {
        return {};
    }

    const auto lower_cps = to_codepoints(lower);
    // best: canonical form → score
    std::unordered_map<std::string, int> best;

    auto consider = [&](const std::string& canonical) {
        const std::string cand_lower = normalize(canonical, language_);
        const auto cand_cps = to_codepoints(cand_lower);
        const int dist = damerau_levenshtein(lower_cps, cand_cps, 1);
        if (dist > 1) {
            return;
        }
        const int len_diff = std::abs(static_cast<int>(cand_cps.size()) - static_cast<int>(lower_cps.size()));
        if (len_diff > 2) {
            return;
        }
        const int same_start =
            (!cand_cps.empty() && !lower_cps.empty() && cand_cps.front() == lower_cps.front()) ? 0 : 8;
        const int prefix_bonus = -std::min(common_prefix_length(lower_cps, cand_cps), 4);
        std::int32_t order = 50000;
        const auto it = entries_.find(cand_lower);
        if (it != entries_.end()) {
            order = it->second.order;
        }
        const int freq_penalty = static_cast<int>(std::min(order, static_cast<std::int32_t>(50000)) / 500);
        const int score = dist * 100 + len_diff * 15 + same_start + freq_penalty + prefix_bonus;

        const auto prev = best.find(canonical);
        if (prev != best.end() && prev->second <= score) {
            return;
        }
        // If another canonical normalizes to same key with better/equal score, skip
        for (const auto& kv : best) {
            if (normalize(kv.first, language_) == cand_lower && kv.second <= score) {
                return;
            }
        }
        best[canonical] = score;
    };

    for (const auto& edit : generate_restricted_edits(lower_cps)) {
        const auto it = entries_.find(edit);
        if (it != entries_.end()) {
            // value_or(edit): missing optional means key itself is the surface form
            const std::string form = it->second.canonical.value_or(edit);
            consider(form);
        }
    }

    std::vector<std::pair<std::string, int>> ranked;
    ranked.reserve(best.size());
    for (auto& kv : best) {
        ranked.emplace_back(std::move(kv.first), kv.second);
    }
    std::sort(ranked.begin(), ranked.end(),
              [](const auto& a, const auto& b) { return a.second < b.second; });

    std::vector<std::string> out;
    const std::size_t lim = static_cast<std::size_t>(limit);
    for (std::size_t i = 0; i < ranked.size() && i < lim; ++i) {
        out.push_back(std::move(ranked[i].first));
    }
    return out;
}

namespace DictionaryLoader {

BundledPaths resolve_bundled(const std::filesystem::path& dict_dir) {
    if (dict_dir.empty()) {
        throw Error(ErrorCode::EmptyPath, "Dictionary directory path is empty");
    }
    BundledPaths paths;
    paths.turkish = dict_dir / "tr.dic";
    paths.english = dict_dir / "en_US.dic";
    std::error_code ec;
    if (!std::filesystem::exists(paths.turkish, ec)) {
        throw Error(ErrorCode::FileNotFound, "Missing dictionary resource: tr.dic",
                    paths.turkish.string());
    }
    if (!std::filesystem::exists(paths.english, ec)) {
        throw Error(ErrorCode::FileNotFound, "Missing dictionary resource: en_US.dic",
                    paths.english.string());
    }
    return paths;
}

BundledDictionaries load_bundled(const std::filesystem::path& dict_dir) {
    const auto paths = resolve_bundled(dict_dir);
    BundledDictionaries out;
    out.turkish = HunspellDictionary::load(paths.turkish, SpellLanguage::Turkish);
    out.english = HunspellDictionary::load(paths.english, SpellLanguage::English);
    return out;
}

} // namespace DictionaryLoader

} // namespace bispell
