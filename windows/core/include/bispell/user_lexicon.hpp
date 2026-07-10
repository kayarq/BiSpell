#pragma once

/// @file user_lexicon.hpp
/// Personal dictionary / ignore lists (Swift `UserLexicon` + `UserLexiconStore` parity).
///
/// JSON format (compatible in spirit with Swift Codable):
///   { "addedWords": [...], "ignoredWords": [...], "ignoredInApps": { "id": [...] } }
///
/// Paths are injectable — pass a temp file for hermetic tests; empty path = memory-only.

#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>

namespace bispell {

struct UserLexicon {
    std::unordered_set<std::string> added_words;
    std::unordered_set<std::string> ignored_words;
    /// app_id -> words
    std::unordered_map<std::string, std::unordered_set<std::string>> ignored_in_apps;

    bool accepts(std::string_view word) const;
    bool ignores(std::string_view word, const std::string* app_id = nullptr) const;

    void add_word(std::string word);
    void ignore_once(std::string word);
    void ignore_in_app(std::string word, std::string app_id);
    void remove_word(std::string_view word);
    void unignore(std::string_view word);

    // Swift aliases
    void addWord(std::string word) { add_word(std::move(word)); }
    void ignoreOnce(std::string word) { ignore_once(std::move(word)); }
    void ignoreInApp(std::string word, std::string app_id) {
        ignore_in_app(std::move(word), std::move(app_id));
    }
    void removeWord(std::string_view word) { remove_word(word); }

    /// Serialize to compact JSON (UTF-8).
    std::string to_json() const;
    /// Parse JSON; on failure returns empty lexicon and sets *ok = false if provided.
    static UserLexicon from_json(std::string_view json, bool* ok = nullptr);
};

/// File-backed lexicon store. Path is fully injectable for hermetic tests.
///
/// Thread-safety: methods are synchronized with an internal mutex.
/// Movable (not copyable) so it can be transferred into SpellEngine.
class UserLexiconStore {
public:
    /// @param path  Lexicon file path. If empty, operates in-memory only (no I/O).
    /// @param load  When true, load existing file if present.
    explicit UserLexiconStore(std::filesystem::path path = {}, bool load = true);

    UserLexiconStore(UserLexiconStore&& other) noexcept;
    UserLexiconStore& operator=(UserLexiconStore&& other) noexcept;
    UserLexiconStore(const UserLexiconStore&) = delete;
    UserLexiconStore& operator=(const UserLexiconStore&) = delete;

    /// Default store under `paths::default_lexicon_path()` (creates parent dir).
    static UserLexiconStore open_default();

    const std::filesystem::path& path() const noexcept { return path_; }

    UserLexicon current() const;

    template <typename Fn>
    void update(Fn&& body) {
        std::lock_guard<std::mutex> lock(*mutex_);
        body(lexicon_);
        persist_unlocked();
    }

    /// Persist current lexicon (no-op if path empty). Returns false on I/O failure.
    bool save() const;

    /// Reload from disk (no-op if path empty / missing).
    bool reload();

private:
    bool persist_unlocked() const;

    std::filesystem::path path_;
    UserLexicon lexicon_;
    std::unique_ptr<std::mutex> mutex_;
};

} // namespace bispell
