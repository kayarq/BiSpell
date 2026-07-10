#pragma once
#include <cstdlib>
#include <iostream>
#include <string>

namespace bispell_test {

inline int g_failures = 0;

inline void expect(bool cond, const char* expr, const char* file, int line) {
    if (!cond) {
        std::cerr << "FAIL " << file << ":" << line << "  " << expr << "\n";
        ++g_failures;
    }
}

template <typename A, typename B>
inline void expect_eq(const A& a, const B& b, const char* ea, const char* eb, const char* file, int line) {
    if (!(a == b)) {
        std::cerr << "FAIL " << file << ":" << line << "  " << ea << " == " << eb
                  << "  left=" << a << " right=" << b << "\n";
        ++g_failures;
    }
}

inline int finalize(const char* suite) {
    if (g_failures == 0) {
        std::cout << suite << ": OK\n";
        return 0;
    }
    std::cerr << suite << ": " << g_failures << " failure(s)\n";
    return 1;
}

} // namespace bispell_test

#define EXPECT(c) ::bispell_test::expect(static_cast<bool>(c), #c, __FILE__, __LINE__)
#define EXPECT_EQ(a, b) ::bispell_test::expect_eq((a), (b), #a, #b, __FILE__, __LINE__)
