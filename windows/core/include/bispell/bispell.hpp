#pragma once

/// @file bispell.hpp
/// Umbrella header for the portable BiSpell core (no platform UI / WinRT headers).
///
/// # Public API surface (U2 + U3)
///
/// | Header                        | Responsibility |
/// |-------------------------------|----------------|
/// | `types.hpp`                   | `SpellLanguage`, `Utf16Range`, `TextToken`, `Misspelling`, `SpellCheckResult` |
/// | `encoding.hpp`                | UTF-8 validate/decode/encode; dual-index spans; `utf8_slice_by_utf16` |
/// | `case_fold.hpp`               | Locale-intent lowercasing (`tr_TR` / `en_US`) without libc locale |
/// | `error.hpp`                   | Structured `Error` / `ErrorCode` |
/// | `tokenizer.hpp`               | Word scan + `shouldSkipToken` |
/// | `dictionary.hpp`              | `HunspellDictionary` load / contains / suggestions |
/// | `app_settings.hpp`            | Spell-relevant settings subset |
/// | `user_lexicon.hpp`            | Personal dict + JSON store (injectable path) |
/// | `language_tagger.hpp`         | TR/EN heuristics (no NaturalLanguage) |
/// | `system_spell_suggester.hpp`  | **Stub** — empty on Windows MVP |
/// | `spell_engine.hpp`            | C++ `SpellEngine` (check / suggest / lexicon) |
/// | `clock.hpp` / `paths.hpp`     | Injectable clock + config paths |
/// | `c_api.h`                     | Stable **C ABI** for C# / interop hosts |
/// | `engine.hpp`                  | **RAII** C++ wrapper over the C ABI |
///
/// ## Encoding contract
/// - All `std::string` text is **UTF-8**.
/// - All public ranges are **UTF-16 code units** (nested `Utf16Range`).
/// - Invalid UTF-8: replacement mode by default; strict mode throws.
///
/// ## Thread-safety
/// `SpellEngine` / `bispell_engine` are **not** safe for concurrent use without
/// an external mutex. Document this to WinUI / P/Invoke hosts.
///
/// ## Range shape (frozen)
/// `TextToken` and `Misspelling` nest `Utf16Range utf16_range` — not flat fields.

#include "bispell/app_settings.hpp"
#include "bispell/case_fold.hpp"
#include "bispell/clock.hpp"
#include "bispell/dictionary.hpp"
#include "bispell/encoding.hpp"
#include "bispell/error.hpp"
#include "bispell/language_tagger.hpp"
#include "bispell/paths.hpp"
#include "bispell/spell_engine.hpp"
#include "bispell/system_spell_suggester.hpp"
#include "bispell/tokenizer.hpp"
#include "bispell/types.hpp"
#include "bispell/user_lexicon.hpp"

// C ABI + RAII wrapper (C header is include-guarded for C++).
#include "bispell/c_api.h"
#include "bispell/engine.hpp"
