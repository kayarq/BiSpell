#include "bispell/spell_engine.hpp"
#include "bispell/case_fold.hpp"
#include "bispell/encoding.hpp"
#include "bispell/system_spell_suggester.hpp"
#include "bispell/tokenizer.hpp"

#include <algorithm>
#include <cstdint>
#include <cmath>

namespace bispell {
namespace {

bool ranges_intersect(Utf16Range a, Utf16Range b) noexcept {
    const auto start = std::max(a.location, b.location);
    const auto end = std::min(a.end(), b.end());
    return end > start;
}

} // namespace

// --- SpellResultCache (list+map LRU, suggestion-flag merge) ---

SpellEngine::SpellResultCache::SpellResultCache(std::size_t capacity)
    : capacity_(std::max<std::size_t>(16, capacity)) {}

std::optional<SpellEngine::CacheEntry> SpellEngine::SpellResultCache::get(
    const std::string& key) {
    std::lock_guard<std::mutex> lock(*mutex_);
    auto it = map_.find(key);
    if (it == map_.end()) {
        return std::nullopt;
    }
    order_.erase(it->second.second);
    order_.push_back(key);
    it->second.second = std::prev(order_.end());
    return it->second.first;
}

void SpellEngine::SpellResultCache::set(const std::string& key, CacheEntry value) {
    std::lock_guard<std::mutex> lock(*mutex_);
    auto it = map_.find(key);
    if (it != map_.end()) {
        it->second.first = merge(it->second.first, value);
        order_.erase(it->second.second);
        order_.push_back(key);
        it->second.second = std::prev(order_.end());
        return;
    }
    order_.push_back(key);
    auto ord_it = std::prev(order_.end());
    map_.emplace(key, std::make_pair(std::move(value), ord_it));
    while (order_.size() > capacity_) {
        const std::string old = order_.front();
        order_.pop_front();
        map_.erase(old);
    }
}

void SpellEngine::SpellResultCache::remove_all() {
    std::lock_guard<std::mutex> lock(*mutex_);
    map_.clear();
    order_.clear();
}

SpellEngine::CacheEntry SpellEngine::SpellResultCache::merge(const CacheEntry& existing,
                                                            const CacheEntry& incoming) {
    CacheEntry out;
    out.is_correct = incoming.is_correct;
    out.suggestions = incoming.suggestions_computed ? incoming.suggestions
                                                    : existing.suggestions;
    out.suggestions_computed =
        existing.suggestions_computed || incoming.suggestions_computed;
    return out;
}

// --- SpellEngine ---

SpellEngine::SpellEngine(HunspellDictionary turkish,
                         HunspellDictionary english,
                         AppSettings settings,
                         UserLexiconStore lexicon_store,
                         std::shared_ptr<IClock> clock)
    : turkish_(std::move(turkish))
    , english_(std::move(english))
    , tagger_(settings.turkish_enabled, settings.english_enabled)
    , lexicon_store_(std::move(lexicon_store))
    , settings_(settings)
    , clock_(clock ? std::move(clock) : make_system_clock())
    , cache_(k_cache_capacity) {}

SpellEngine SpellEngine::bundled(const std::filesystem::path& dict_dir,
                                 AppSettings settings,
                                 UserLexiconStore lexicon_store,
                                 std::shared_ptr<IClock> clock) {
    auto dicts = DictionaryLoader::load_bundled(dict_dir);
    return SpellEngine(std::move(dicts.turkish), std::move(dicts.english), settings,
                       std::move(lexicon_store), std::move(clock));
}

void SpellEngine::update_settings(const AppSettings& settings) {
    settings_ = settings;
    tagger_ = LanguageTagger(settings.turkish_enabled, settings.english_enabled);
}

void SpellEngine::add_to_dictionary(std::string_view word) {
    lexicon_store_.update([&](UserLexicon& lex) { lex.add_word(std::string(word)); });
    cache_.remove_all();
}

void SpellEngine::ignore_word(std::string_view word) {
    lexicon_store_.update([&](UserLexicon& lex) { lex.ignore_once(std::string(word)); });
}

void SpellEngine::ignore_word(std::string_view word, std::string_view app_id) {
    lexicon_store_.update([&](UserLexicon& lex) {
        lex.ignore_in_app(std::string(word), std::string(app_id));
    });
}

void SpellEngine::remove_from_dictionary(std::string_view word) {
    lexicon_store_.update([&](UserLexicon& lex) { lex.remove_word(word); });
    cache_.remove_all();
}

void SpellEngine::unignore_word(std::string_view word) {
    lexicon_store_.update([&](UserLexicon& lex) { lex.unignore(word); });
}

SpellCheckResult SpellEngine::check(std::string_view text_utf8, const CheckOptions& options) const {
    if (options.strict_utf8) {
        if (!encoding::is_valid_utf8(text_utf8)) {
            throw Error(ErrorCode::InvalidUtf8, "check: input is not valid UTF-8");
        }
    }

    SpellCheckResult result;
    result.source_text.assign(text_utf8.data(), text_utf8.size());

    if (!settings_.is_enabled) {
        return result;
    }
    if (!settings_.turkish_enabled && !settings_.english_enabled) {
        return result;
    }

    const auto tokens = Tokenizer::tokenize(text_utf8);
    const UserLexicon lexicon = lexicon_store_.current();
    const auto document_lang = tagger_.detect_document_language(text_utf8);

    std::optional<Utf16Range> focus_range;
    if (options.near_caret_only && options.caret_utf16.has_value()) {
        const auto caret = *options.caret_utf16;
        const auto ns_len = encoding::utf16_length(text_utf8);
        const int radius = options.window_radius;
        const std::uint32_t start =
            static_cast<std::uint32_t>(std::max(0, static_cast<int>(caret) - radius));
        const std::uint32_t end = std::min(ns_len, caret + static_cast<std::uint32_t>(radius));
        const std::uint32_t len = end > start ? end - start : 0;
        focus_range = Utf16Range{start, len};
    }

    for (const auto& token : tokens) {
        if (static_cast<int>(encoding::codepoint_count(token.text)) < settings_.min_word_length) {
            continue;
        }
        if (Tokenizer::shouldSkipToken(token.text)) {
            continue;
        }
        if (focus_range && !ranges_intersect(*focus_range, token.utf16_range)) {
            continue;
        }
        const std::string* app_ptr = options.bundle_id ? &*options.bundle_id : nullptr;
        if (lexicon.ignores(token.text, app_ptr)) {
            continue;
        }

        const std::string context = context_snippet(text_utf8, token.utf16_range, 40);
        const SpellLanguage lang = resolve_language(token.text, context, document_lang);
        const auto [is_correct, resolved_lang] = evaluate_correctness(token.text, lang);
        if (!is_correct) {
            Misspelling m;
            m.word = token.text;
            m.utf16_range = token.utf16_range;
            m.language = resolved_lang;
            m.suggestions = {};
            result.misspellings.push_back(std::move(m));
        }
    }
    return result;
}

std::vector<std::string> SpellEngine::suggestions(std::string_view word,
                                                  SpellLanguage language) const {
    const std::string key = cache_key(word, language);
    if (auto hit = cache_.get(key)) {
        if (hit->suggestions_computed) {
            std::vector<std::string> out = hit->suggestions;
            if (static_cast<int>(out.size()) > settings_.max_suggestions) {
                out.resize(static_cast<std::size_t>(settings_.max_suggestions));
            }
            return out;
        }
    }

    const int limit = settings_.max_suggestions;
    auto system = SystemSpellSuggester::suggestions(word, language, limit);
    std::vector<std::string> list;
    if (!system.empty()) {
        list = std::move(system);
    } else {
        switch (language) {
        case SpellLanguage::Turkish:
            list = turkish_.suggestions(word, limit);
            break;
        case SpellLanguage::English:
            list = english_.suggestions(word, limit);
            break;
        case SpellLanguage::Unknown: {
            auto en = english_.suggestions(word, limit);
            list = en.empty() ? turkish_.suggestions(word, limit) : std::move(en);
            break;
        }
        }
    }

    const bool correct = is_correct_local_or_system(word, language);
    CacheEntry entry;
    entry.is_correct = correct;
    entry.suggestions = list;
    entry.suggestions_computed = true;
    cache_.set(key, std::move(entry));
    return list;
}

Misspelling SpellEngine::with_suggestions(const Misspelling& misspelling) const {
    Misspelling copy = misspelling;
    if (!copy.suggestions.empty()) {
        return copy;
    }

    if (should_disambiguate_language(copy.language)) {
        const auto en = settings_.english_enabled
            ? suggestions(copy.word, SpellLanguage::English)
            : std::vector<std::string>{};
        const auto tr = settings_.turkish_enabled
            ? suggestions(copy.word, SpellLanguage::Turkish)
            : std::vector<std::string>{};
        if (!en.empty() && tr.empty()) {
            copy.language = SpellLanguage::English;
            copy.suggestions = en;
        } else if (!tr.empty() && en.empty()) {
            copy.language = SpellLanguage::Turkish;
            copy.suggestions = tr;
        } else if (!en.empty() && !tr.empty()) {
            const int en_dist = edit_distance_utf8(copy.word, en[0]);
            const int tr_dist = edit_distance_utf8(copy.word, tr[0]);
            if (tr_dist < en_dist) {
                copy.language = SpellLanguage::Turkish;
                copy.suggestions = tr;
            } else {
                copy.language = SpellLanguage::English;
                copy.suggestions = en;
            }
        } else {
            copy.suggestions = suggestions(copy.word, copy.language);
        }
    } else {
        copy.suggestions = suggestions(copy.word, copy.language);
    }
    return copy;
}

std::optional<Misspelling> SpellEngine::nearest_misspelling(
    const std::vector<Misspelling>& misspellings,
    std::optional<std::uint32_t> caret_utf16) const {
    if (misspellings.empty()) {
        return std::nullopt;
    }
    if (!caret_utf16) {
        return misspellings.front();
    }
    const auto caret = *caret_utf16;

    for (const auto& m : misspellings) {
        if (caret >= m.utf16_range.location && caret <= m.utf16_range.end()) {
            return m;
        }
    }

    const Misspelling* just_after = nullptr;
    std::uint32_t best_after = UINT32_MAX;
    for (const auto& m : misspellings) {
        if (caret >= m.utf16_range.end()) {
            const auto d = caret - m.utf16_range.end();
            if (d < best_after) {
                best_after = d;
                just_after = &m;
            }
        }
    }
    if (just_after && best_after <= 2) {
        return *just_after;
    }

    const Misspelling* best = &misspellings.front();
    std::uint32_t best_dist = UINT32_MAX;
    for (const auto& m : misspellings) {
        const auto mid = m.utf16_range.location + m.utf16_range.length / 2;
        const auto dist = mid > caret ? mid - caret : caret - mid;
        if (dist < best_dist) {
            best_dist = dist;
            best = &m;
        }
    }
    return *best;
}

SpellLanguage SpellEngine::resolve_language(std::string_view word,
                                            std::string_view context,
                                            std::optional<SpellLanguage> document_lang) const {
    const SpellLanguage detected = tagger_.detect(word, context);
    if (detected != SpellLanguage::Unknown) {
        return detected;
    }
    if (settings_.turkish_enabled && is_correct_cached(word, SpellLanguage::Turkish)) {
        return SpellLanguage::Turkish;
    }
    if (settings_.english_enabled && is_correct_cached(word, SpellLanguage::English)) {
        return SpellLanguage::English;
    }
    if (document_lang && *document_lang != SpellLanguage::Unknown) {
        return *document_lang;
    }
    return detected;
}

std::pair<bool, SpellLanguage> SpellEngine::evaluate_correctness(std::string_view word,
                                                                 SpellLanguage language) const {
    switch (language) {
    case SpellLanguage::Turkish:
        if (!settings_.turkish_enabled) {
            return evaluate_both_correctness(word);
        }
        if (is_correct_cached(word, SpellLanguage::Turkish)) {
            return {true, SpellLanguage::Turkish};
        }
        return {false, SpellLanguage::Turkish};
    case SpellLanguage::English:
        if (!settings_.english_enabled) {
            return evaluate_both_correctness(word);
        }
        if (is_correct_cached(word, SpellLanguage::English)) {
            return {true, SpellLanguage::English};
        }
        return {false, SpellLanguage::English};
    case SpellLanguage::Unknown:
        return evaluate_both_correctness(word);
    }
    return evaluate_both_correctness(word);
}

std::pair<bool, SpellLanguage> SpellEngine::evaluate_both_correctness(std::string_view word) const {
    const bool tr_ok = settings_.turkish_enabled && is_correct_cached(word, SpellLanguage::Turkish);
    const bool en_ok = settings_.english_enabled && is_correct_cached(word, SpellLanguage::English);
    if (tr_ok) {
        return {true, SpellLanguage::Turkish};
    }
    if (en_ok) {
        return {true, SpellLanguage::English};
    }
    if (settings_.turkish_enabled && settings_.english_enabled) {
        return {false, SpellLanguage::Unknown};
    }
    const SpellLanguage lang =
        settings_.english_enabled ? SpellLanguage::English : SpellLanguage::Turkish;
    return {false, lang};
}

bool SpellEngine::is_correct_cached(std::string_view word, SpellLanguage language) const {
    const std::string key = cache_key(word, language);
    if (auto hit = cache_.get(key)) {
        return hit->is_correct;
    }
    const bool ok = is_correct_local_or_system(word, language);
    CacheEntry entry;
    entry.is_correct = ok;
    entry.suggestions = {};
    entry.suggestions_computed = false;
    cache_.set(key, std::move(entry));
    return ok;
}

bool SpellEngine::is_correct_local_or_system(std::string_view word, SpellLanguage language) const {
    switch (language) {
    case SpellLanguage::Turkish:
        if (turkish_.contains(word)) {
            return true;
        }
        return SystemSpellSuggester::is_correct(word, SpellLanguage::Turkish);
    case SpellLanguage::English:
        if (english_.contains(word)) {
            return true;
        }
        return SystemSpellSuggester::is_correct(word, SpellLanguage::English);
    case SpellLanguage::Unknown:
        return false;
    }
    return false;
}

bool SpellEngine::should_disambiguate_language(SpellLanguage language) const {
    if (!(settings_.turkish_enabled && settings_.english_enabled)) {
        return false;
    }
    return language == SpellLanguage::Unknown;
}

int SpellEngine::edit_distance_utf8(std::string_view a, std::string_view b) {
    // Simple Levenshtein on lowercased code points (Swift Character arrays).
    const std::string al = case_fold_english(a);
    const std::string bl = case_fold_english(b);
    auto spans_a = encoding::decode_utf8(al, true, nullptr);
    auto spans_b = encoding::decode_utf8(bl, true, nullptr);
    const int n = static_cast<int>(spans_a.size());
    const int m = static_cast<int>(spans_b.size());
    if (n == 0) return m;
    if (m == 0) return n;
    std::vector<int> prev(static_cast<std::size_t>(m + 1));
    std::vector<int> cur(static_cast<std::size_t>(m + 1));
    for (int j = 0; j <= m; ++j) {
        prev[static_cast<std::size_t>(j)] = j;
    }
    for (int i = 1; i <= n; ++i) {
        cur[0] = i;
        for (int j = 1; j <= m; ++j) {
            const int cost =
                spans_a[static_cast<std::size_t>(i - 1)].cp == spans_b[static_cast<std::size_t>(j - 1)].cp
                    ? 0
                    : 1;
            cur[static_cast<std::size_t>(j)] = std::min(
                {prev[static_cast<std::size_t>(j)] + 1, cur[static_cast<std::size_t>(j - 1)] + 1,
                 prev[static_cast<std::size_t>(j - 1)] + cost});
        }
        prev.swap(cur);
    }
    return prev[static_cast<std::size_t>(m)];
}

std::string SpellEngine::context_snippet(std::string_view text, Utf16Range range, int radius_utf16) {
    const auto total = encoding::utf16_length(text);
    const int loc = static_cast<int>(range.location);
    const int len = static_cast<int>(range.length);
    const int start = std::max(0, loc - radius_utf16);
    const int end = std::min(static_cast<int>(total), loc + len + radius_utf16);
    const Utf16Range slice{static_cast<std::uint32_t>(start),
                           static_cast<std::uint32_t>(std::max(0, end - start))};
    return encoding::utf8_slice_by_utf16(text, slice);
}

std::string SpellEngine::cache_key(std::string_view word, SpellLanguage language) {
    std::string key = to_string(language);
    key.push_back('|');
    key.append(word.data(), word.size());
    return key;
}

} // namespace bispell
