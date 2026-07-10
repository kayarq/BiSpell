#pragma once

/// @file app_settings.hpp
/// Spell-relevant subset of Swift `AppSettings` for the portable core.

namespace bispell {

/// Settings that gate SpellEngine behavior (UI-only fields omitted on Windows MVP).
struct AppSettings {
    bool is_enabled = true;
    bool turkish_enabled = true;
    bool english_enabled = true;
    int debounce_milliseconds = 250;
    int max_suggestions = 5;
    int min_word_length = 2;

    /// Swift `AppSettings.default` equivalent.
    static AppSettings defaults() noexcept { return AppSettings{}; }

    // --- Swift-style accessors (plan / cross-platform review) ---
    bool isEnabled() const noexcept { return is_enabled; }
    void setIsEnabled(bool v) noexcept { is_enabled = v; }
    bool turkishEnabled() const noexcept { return turkish_enabled; }
    void setTurkishEnabled(bool v) noexcept { turkish_enabled = v; }
    bool englishEnabled() const noexcept { return english_enabled; }
    void setEnglishEnabled(bool v) noexcept { english_enabled = v; }
    int maxSuggestions() const noexcept { return max_suggestions; }
    void setMaxSuggestions(int v) noexcept { max_suggestions = v; }
    int minWordLength() const noexcept { return min_word_length; }
    void setMinWordLength(int v) noexcept { min_word_length = v; }
};

inline AppSettings default_settings() noexcept { return AppSettings::defaults(); }

} // namespace bispell
