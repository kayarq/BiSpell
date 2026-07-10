#pragma once

/// @file clock.hpp
/// Injectable clock for hermetic tests and future debounce / mtime checks.
///
/// Thread-safety: implementations used concurrently with SpellEngine still
/// require an external mutex on the engine; the clock itself should be
/// const-safe for concurrent `now()` if shared.

#include <chrono>
#include <memory>

namespace bispell {

using TimePoint = std::chrono::system_clock::time_point;
using Duration = std::chrono::system_clock::duration;

/// Abstract wall-clock source.
class IClock {
public:
    virtual ~IClock() = default;
    virtual TimePoint now() const = 0;
};

/// Production clock (`std::chrono::system_clock`).
class SystemClock final : public IClock {
public:
    TimePoint now() const override { return std::chrono::system_clock::now(); }
};

/// Mutable fake clock for tests (advance manually).
class FakeClock final : public IClock {
public:
    explicit FakeClock(TimePoint start = std::chrono::system_clock::now()) : now_(start) {}

    TimePoint now() const override { return now_; }

    void set(TimePoint t) noexcept { now_ = t; }
    void advance(Duration d) noexcept { now_ += d; }

private:
    TimePoint now_;
};

inline std::shared_ptr<IClock> make_system_clock() {
    return std::make_shared<SystemClock>();
}

} // namespace bispell
