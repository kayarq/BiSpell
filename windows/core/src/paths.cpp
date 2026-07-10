#include "bispell/paths.hpp"

#include <cstdlib>
#include <mutex>

namespace bispell {
namespace paths {
namespace {

std::mutex g_override_mu;
std::filesystem::path g_override;

std::filesystem::path home_dir() {
#if defined(_WIN32)
    if (const char* p = std::getenv("USERPROFILE")) {
        if (p[0]) return std::filesystem::path(p);
    }
    if (const char* home = std::getenv("HOME")) {
        if (home[0]) return std::filesystem::path(home);
    }
    return {};
#else
    if (const char* home = std::getenv("HOME")) {
        if (home[0]) return std::filesystem::path(home);
    }
    return {};
#endif
}

} // namespace

void set_config_dir_override(std::filesystem::path dir) {
    std::lock_guard<std::mutex> lock(g_override_mu);
    g_override = std::move(dir);
}

void clear_config_dir_override() {
    std::lock_guard<std::mutex> lock(g_override_mu);
    g_override.clear();
}

std::filesystem::path config_dir_override() {
    std::lock_guard<std::mutex> lock(g_override_mu);
    return g_override;
}

std::filesystem::path default_config_dir() {
    {
        std::lock_guard<std::mutex> lock(g_override_mu);
        if (!g_override.empty()) {
            return g_override;
        }
    }

#if defined(_WIN32)
    if (const char* appdata = std::getenv("APPDATA")) {
        if (appdata[0]) {
            return std::filesystem::path(appdata) / "BiSpell";
        }
    }
    auto home = home_dir();
    if (!home.empty()) {
        return home / "AppData" / "Roaming" / "BiSpell";
    }
    return std::filesystem::path("BiSpell");
#else
    if (const char* xdg = std::getenv("XDG_CONFIG_HOME")) {
        if (xdg[0]) {
            return std::filesystem::path(xdg) / "bispell";
        }
    }
    auto home = home_dir();
    if (!home.empty()) {
        return home / ".config" / "bispell";
    }
    return std::filesystem::path("bispell");
#endif
}

std::filesystem::path default_lexicon_path() {
    return default_config_dir() / "user-lexicon.json";
}

std::filesystem::path config_file(const std::filesystem::path& dir, std::string_view filename) {
    return dir / std::filesystem::path(std::string(filename));
}

bool ensure_directory(const std::filesystem::path& dir) {
    if (dir.empty()) {
        return false;
    }
    std::error_code ec;
    if (std::filesystem::exists(dir, ec)) {
        return std::filesystem::is_directory(dir, ec);
    }
    return std::filesystem::create_directories(dir, ec) || std::filesystem::is_directory(dir, ec);
}

} // namespace paths
} // namespace bispell
