#include "bispell/user_lexicon.hpp"
#include "bispell/paths.hpp"

#include <algorithm>
#include <cstdio>
#include <cctype>
#include <fstream>
#include <sstream>
#include <vector>

namespace bispell {
namespace {

std::string ascii_lower(std::string_view s) {
    std::string out;
    out.reserve(s.size());
    for (unsigned char c : s) {
        out.push_back(static_cast<char>(std::tolower(c)));
    }
    return out;
}

bool set_has_ci(const std::unordered_set<std::string>& set, std::string_view word) {
    if (set.find(std::string(word)) != set.end()) {
        return true;
    }
    const std::string key = ascii_lower(word);
    for (const auto& w : set) {
        if (ascii_lower(w) == key) {
            return true;
        }
    }
    return false;
}

void set_remove_ci(std::unordered_set<std::string>& set, std::string_view word) {
    const std::string key = ascii_lower(word);
    for (auto it = set.begin(); it != set.end();) {
        if (ascii_lower(*it) == key) {
            it = set.erase(it);
        } else {
            ++it;
        }
    }
}

// --- Minimal JSON helpers for lexicon schema only ---

void skip_ws(std::string_view s, std::size_t& i) {
    while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) {
        ++i;
    }
}

bool match_char(std::string_view s, std::size_t& i, char c) {
    skip_ws(s, i);
    if (i < s.size() && s[i] == c) {
        ++i;
        return true;
    }
    return false;
}

bool parse_string(std::string_view s, std::size_t& i, std::string& out) {
    skip_ws(s, i);
    if (i >= s.size() || s[i] != '"') {
        return false;
    }
    ++i;
    out.clear();
    while (i < s.size()) {
        char c = s[i++];
        if (c == '"') {
            return true;
        }
        if (c == '\\' && i < s.size()) {
            char e = s[i++];
            switch (e) {
            case '"': out.push_back('"'); break;
            case '\\': out.push_back('\\'); break;
            case '/': out.push_back('/'); break;
            case 'b': out.push_back('\b'); break;
            case 'f': out.push_back('\f'); break;
            case 'n': out.push_back('\n'); break;
            case 'r': out.push_back('\r'); break;
            case 't': out.push_back('\t'); break;
            case 'u': {
                // Minimal \uXXXX BMP handling
                if (i + 4 > s.size()) return false;
                unsigned code = 0;
                for (int k = 0; k < 4; ++k) {
                    char h = s[i++];
                    code <<= 4;
                    if (h >= '0' && h <= '9') code |= static_cast<unsigned>(h - '0');
                    else if (h >= 'a' && h <= 'f') code |= static_cast<unsigned>(h - 'a' + 10);
                    else if (h >= 'A' && h <= 'F') code |= static_cast<unsigned>(h - 'A' + 10);
                    else return false;
                }
                if (code < 0x80) {
                    out.push_back(static_cast<char>(code));
                } else if (code < 0x800) {
                    out.push_back(static_cast<char>(0xC0 | (code >> 6)));
                    out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
                } else {
                    out.push_back(static_cast<char>(0xE0 | (code >> 12)));
                    out.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
                    out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
                }
                break;
            }
            default: out.push_back(e); break;
            }
        } else {
            out.push_back(c);
        }
    }
    return false;
}

bool parse_string_array(std::string_view s, std::size_t& i, std::unordered_set<std::string>& out) {
    if (!match_char(s, i, '[')) return false;
    skip_ws(s, i);
    if (match_char(s, i, ']')) return true;
    for (;;) {
        std::string item;
        if (!parse_string(s, i, item)) return false;
        out.insert(std::move(item));
        skip_ws(s, i);
        if (match_char(s, i, ']')) return true;
        if (!match_char(s, i, ',')) return false;
    }
}

bool parse_ignored_in_apps(std::string_view s, std::size_t& i,
                           std::unordered_map<std::string, std::unordered_set<std::string>>& out) {
    if (!match_char(s, i, '{')) return false;
    skip_ws(s, i);
    if (match_char(s, i, '}')) return true;
    for (;;) {
        std::string key;
        if (!parse_string(s, i, key)) return false;
        if (!match_char(s, i, ':')) return false;
        std::unordered_set<std::string> vals;
        if (!parse_string_array(s, i, vals)) return false;
        out[std::move(key)] = std::move(vals);
        skip_ws(s, i);
        if (match_char(s, i, '}')) return true;
        if (!match_char(s, i, ',')) return false;
    }
}

std::string json_escape(std::string_view s) {
    std::string out;
    out.reserve(s.size() + 8);
    out.push_back('"');
    for (unsigned char c : s) {
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\b': out += "\\b"; break;
        case '\f': out += "\\f"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (c < 0x20) {
                char buf[8];
                std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                out += buf;
            } else {
                out.push_back(static_cast<char>(c));
            }
        }
    }
    out.push_back('"');
    return out;
}

std::string json_string_array(const std::unordered_set<std::string>& set) {
    std::vector<std::string> items(set.begin(), set.end());
    std::sort(items.begin(), items.end());
    std::string out = "[";
    for (std::size_t i = 0; i < items.size(); ++i) {
        if (i) out += ',';
        out += json_escape(items[i]);
    }
    out += ']';
    return out;
}

} // namespace

bool UserLexicon::accepts(std::string_view word) const {
    if (set_has_ci(added_words, word)) return true;
    if (set_has_ci(ignored_words, word)) return true;
    return false;
}

bool UserLexicon::ignores(std::string_view word, const std::string* app_id) const {
    if (accepts(word)) return true;
    if (!app_id || app_id->empty()) return false;
    auto it = ignored_in_apps.find(*app_id);
    if (it == ignored_in_apps.end()) return false;
    return set_has_ci(it->second, word);
}

void UserLexicon::add_word(std::string word) {
    set_remove_ci(ignored_words, word);
    added_words.insert(std::move(word));
}

void UserLexicon::ignore_once(std::string word) {
    ignored_words.insert(std::move(word));
}

void UserLexicon::ignore_in_app(std::string word, std::string app_id) {
    ignored_in_apps[std::move(app_id)].insert(std::move(word));
}

void UserLexicon::remove_word(std::string_view word) {
    set_remove_ci(added_words, word);
}

void UserLexicon::unignore(std::string_view word) {
    set_remove_ci(ignored_words, word);
    for (auto it = ignored_in_apps.begin(); it != ignored_in_apps.end();) {
        set_remove_ci(it->second, word);
        if (it->second.empty()) {
            it = ignored_in_apps.erase(it);
        } else {
            ++it;
        }
    }
}

std::string UserLexicon::to_json() const {
    std::string out = "{\"addedWords\":";
    out += json_string_array(added_words);
    out += ",\"ignoredWords\":";
    out += json_string_array(ignored_words);
    out += ",\"ignoredInApps\":{";
    std::vector<std::string> keys;
    keys.reserve(ignored_in_apps.size());
    for (const auto& kv : ignored_in_apps) {
        keys.push_back(kv.first);
    }
    std::sort(keys.begin(), keys.end());
    for (std::size_t i = 0; i < keys.size(); ++i) {
        if (i) out += ',';
        out += json_escape(keys[i]);
        out += ':';
        out += json_string_array(ignored_in_apps.at(keys[i]));
    }
    out += "}}";
    return out;
}

UserLexicon UserLexicon::from_json(std::string_view json, bool* ok) {
    UserLexicon lex;
    std::size_t i = 0;
    if (!match_char(json, i, '{')) {
        if (ok) *ok = false;
        return {};
    }
    skip_ws(json, i);
    if (match_char(json, i, '}')) {
        if (ok) *ok = true;
        return lex;
    }
    for (;;) {
        std::string key;
        if (!parse_string(json, i, key)) {
            if (ok) *ok = false;
            return {};
        }
        if (!match_char(json, i, ':')) {
            if (ok) *ok = false;
            return {};
        }
        if (key == "addedWords") {
            if (!parse_string_array(json, i, lex.added_words)) {
                if (ok) *ok = false;
                return {};
            }
        } else if (key == "ignoredWords") {
            if (!parse_string_array(json, i, lex.ignored_words)) {
                if (ok) *ok = false;
                return {};
            }
        } else if (key == "ignoredInApps") {
            if (!parse_ignored_in_apps(json, i, lex.ignored_in_apps)) {
                if (ok) *ok = false;
                return {};
            }
        } else {
            // Skip unknown value: string, array, object, number, bool, null — best effort
            skip_ws(json, i);
            if (i < json.size() && json[i] == '"') {
                std::string tmp;
                if (!parse_string(json, i, tmp)) {
                    if (ok) *ok = false;
                    return {};
                }
            } else if (i < json.size() && json[i] == '[') {
                std::unordered_set<std::string> tmp;
                if (!parse_string_array(json, i, tmp)) {
                    // try skip to matching ]
                    int depth = 0;
                    do {
                        if (json[i] == '[') ++depth;
                        else if (json[i] == ']') --depth;
                        ++i;
                    } while (i < json.size() && depth > 0);
                }
            } else if (i < json.size() && json[i] == '{') {
                std::unordered_map<std::string, std::unordered_set<std::string>> tmp;
                if (!parse_ignored_in_apps(json, i, tmp)) {
                    if (ok) *ok = false;
                    return {};
                }
            } else {
                while (i < json.size() && json[i] != ',' && json[i] != '}') ++i;
            }
        }
        skip_ws(json, i);
        if (match_char(json, i, '}')) {
            if (ok) *ok = true;
            return lex;
        }
        if (!match_char(json, i, ',')) {
            if (ok) *ok = false;
            return {};
        }
    }
}

UserLexiconStore::UserLexiconStore(std::filesystem::path path, bool load)
    : path_(std::move(path))
    , mutex_(std::make_unique<std::mutex>()) {
    if (load) {
        reload();
    }
}

UserLexiconStore::UserLexiconStore(UserLexiconStore&& other) noexcept
    : path_(std::move(other.path_))
    , lexicon_(std::move(other.lexicon_))
    , mutex_(std::move(other.mutex_)) {
    if (!mutex_) {
        mutex_ = std::make_unique<std::mutex>();
    }
}

UserLexiconStore& UserLexiconStore::operator=(UserLexiconStore&& other) noexcept {
    if (this != &other) {
        path_ = std::move(other.path_);
        lexicon_ = std::move(other.lexicon_);
        mutex_ = std::move(other.mutex_);
        if (!mutex_) {
            mutex_ = std::make_unique<std::mutex>();
        }
    }
    return *this;
}

UserLexiconStore UserLexiconStore::open_default() {
    auto p = paths::default_lexicon_path();
    paths::ensure_directory(p.parent_path());
    return UserLexiconStore(std::move(p), true);
}

UserLexicon UserLexiconStore::current() const {
    std::lock_guard<std::mutex> lock(*mutex_);
    return lexicon_;
}

bool UserLexiconStore::persist_unlocked() const {
    if (path_.empty()) {
        return true;
    }
    paths::ensure_directory(path_.parent_path());
    const std::string json = lexicon_.to_json();
    std::ofstream ofs(path_, std::ios::binary | std::ios::trunc);
    if (!ofs) {
        return false;
    }
    ofs.write(json.data(), static_cast<std::streamsize>(json.size()));
    return static_cast<bool>(ofs);
}

bool UserLexiconStore::save() const {
    std::lock_guard<std::mutex> lock(*mutex_);
    return persist_unlocked();
}

bool UserLexiconStore::reload() {
    std::lock_guard<std::mutex> lock(*mutex_);
    if (path_.empty()) {
        return true;
    }
    std::ifstream ifs(path_, std::ios::binary);
    if (!ifs) {
        lexicon_ = UserLexicon{};
        return false;
    }
    std::ostringstream ss;
    ss << ifs.rdbuf();
    bool ok = false;
    lexicon_ = UserLexicon::from_json(ss.str(), &ok);
    if (!ok) {
        lexicon_ = UserLexicon{};
    }
    return ok;
}

} // namespace bispell
