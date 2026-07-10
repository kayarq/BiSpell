#pragma once

/// @file error.hpp
/// Structured errors for the portable BiSpell core (no WinRT/Win32 UI headers).

#include <exception>
#include <string>
#include <utility>

namespace bispell {

/// Stable error codes for dictionary I/O and text handling.
enum class ErrorCode {
    Ok = 0,
    EmptyPath,
    FileNotFound,
    FileUnreadable,
    InvalidUtf8,
    ParseError,
    EmptyDictionary,
};

/// Exception type with machine-readable code and optional path context.
class Error final : public std::exception {
public:
    Error(ErrorCode code, std::string message, std::string path = {})
        : code_(code)
        , message_(std::move(message))
        , path_(std::move(path)) {
        if (!path_.empty()) {
            full_ = message_ + " (" + path_ + ")";
        } else {
            full_ = message_;
        }
    }

    ErrorCode code() const noexcept { return code_; }
    const std::string& message() const noexcept { return message_; }
    const std::string& path() const noexcept { return path_; }
    const char* what() const noexcept override { return full_.c_str(); }

private:
    ErrorCode code_;
    std::string message_;
    std::string path_;
    std::string full_;
};

inline const char* to_string(ErrorCode code) noexcept {
    switch (code) {
    case ErrorCode::Ok: return "Ok";
    case ErrorCode::EmptyPath: return "EmptyPath";
    case ErrorCode::FileNotFound: return "FileNotFound";
    case ErrorCode::FileUnreadable: return "FileUnreadable";
    case ErrorCode::InvalidUtf8: return "InvalidUtf8";
    case ErrorCode::ParseError: return "ParseError";
    case ErrorCode::EmptyDictionary: return "EmptyDictionary";
    }
    return "Unknown";
}

} // namespace bispell
