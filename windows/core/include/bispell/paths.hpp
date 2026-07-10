#pragma once

/// @file paths.hpp
/// Injectable path resolution for config / lexicon locations.
///
/// Defaults:
/// - Windows: `%APPDATA%\BiSpell\`
/// - Linux/Unix: `$XDG_CONFIG_HOME/bispell/` or `~/.config/bispell/`
///
/// All paths are injectable for hermetic tests (never hard-require AppData).

#include <filesystem>
#include <string>
#include <string_view>

namespace bispell {
namespace paths {

/// Platform default config directory for BiSpell (created lazily by callers).
std::filesystem::path default_config_dir();

/// Default user-lexicon JSON path under the config directory.
std::filesystem::path default_lexicon_path();

/// Join config dir with a filename.
std::filesystem::path config_file(const std::filesystem::path& dir, std::string_view filename);

/// Ensure directory exists (best-effort; returns false on failure).
bool ensure_directory(const std::filesystem::path& dir);

/// Override config root for the process (empty clears). Useful in tests.
void set_config_dir_override(std::filesystem::path dir);
void clear_config_dir_override();
std::filesystem::path config_dir_override();

} // namespace paths
} // namespace bispell
