#pragma once

/// @file bispell.hpp
/// Umbrella header for the portable BiSpell core (no platform UI / WinRT headers).
///
/// # Frozen public API surface (U2)
///
/// | Header          | Responsibility |
/// |-----------------|----------------|
/// | `types.hpp`     | `SpellLanguage`, **`Utf16Range`**, **`TextToken`**, `Misspelling`, `SpellCheckResult` |
/// | `encoding.hpp`  | UTF-8 validate/decode/encode; dual-index spans; `utf8_slice_by_utf16` |
/// | `case_fold.hpp` | Locale-intent lowercasing (`tr_TR` / `en_US`) without libc locale |
/// | `error.hpp`     | Structured `Error` / `ErrorCode` for dict load and text failures |
/// | `tokenizer.hpp` | Word scan + `shouldSkipToken` (Swift parity) |
/// | `dictionary.hpp`| `HunspellDictionary` load / contains / suggestions / stem candidates |
///
/// ## TextToken / Misspelling range shape (frozen for U3)
/// Both `TextToken` and `Misspelling` carry a **nested** `Utf16Range utf16_range`
/// (not flat `utf16_location` / `utf16_length` fields). Apply and host UI share
/// one coordinate type — U3 implementers use this nested contract exclusively.
/// - All `std::string` text is **UTF-8**.
/// - All public ranges are **UTF-16 code units** (NSRange / WinUI parity).
/// - Convert / slice with `encoding::decode_utf8` and `encoding::utf8_slice_by_utf16`.
///
/// ## Dictionary naming
/// Snake_case is the primary C++ API (`word_count`, `stem_candidates`,
/// `load_from_string`). CamelCase aliases match Swift/plan (`wordCount`,
/// `stemCandidates`) for cross-platform review. Entry `canonical` is
/// `std::optional<std::string>` (nullopt = key is the surface form).
///
/// U3 (`SpellEngine`, tagger, lexicon) is intentionally **not** in this surface.

#include "bispell/case_fold.hpp"
#include "bispell/dictionary.hpp"
#include "bispell/encoding.hpp"
#include "bispell/error.hpp"
#include "bispell/tokenizer.hpp"
#include "bispell/types.hpp"
