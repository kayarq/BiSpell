using BiSpell.Models;

namespace BiSpell.Utilities;

/// <summary>
/// Result of applying top suggestions to a clipboard (or any) string.
/// Pure managed outcome — no clipboard or engine IO.
/// </summary>
public sealed class ClipboardFixResult
{
    /// <summary>Text after best-effort replacements (same as input when unchanged).</summary>
    public required string FixedText { get; init; }

    /// <summary>Number of misspellings successfully replaced with a top suggestion.</summary>
    public int ReplacementsApplied { get; init; }

    /// <summary>Misspellings skipped because they had no usable suggestion.</summary>
    public int SkippedNoSuggestion { get; init; }

    /// <summary>True when <see cref="FixedText"/> equals the original (no successful replace).</summary>
    public bool Unchanged { get; init; }
}

/// <summary>
/// Pure batch apply of top suggestions over UTF-16 ranges (Mac
/// <c>TextReplacementBatch</c> intent). Unit-testable without clipboard or engine.
/// </summary>
/// <remarks>
/// GLUE calls <see cref="ApplyTopSuggestions"/> after <c>BispellEngine.Check</c>.
/// Ranges are applied <b>back-to-front</b> by <see cref="MisspellingItem.Utf16Location"/>
/// so earlier offsets stay valid. A range is applied only when the slice at
/// location/length still equals <see cref="MisspellingItem.Word"/>.
/// </remarks>
public static class ClipboardSpellFix
{
    /// <summary>
    /// Apply the first non-empty suggestion for each misspelling that has one.
    /// Misspellings with no suggestions are left in place and counted as skipped.
    /// Invalid or non-matching ranges are ignored (not counted as replacements).
    /// </summary>
    public static ClipboardFixResult ApplyTopSuggestions(
        string original,
        IReadOnlyList<MisspellingItem>? misses)
    {
        original ??= string.Empty;

        if (misses is null || misses.Count == 0)
        {
            return new ClipboardFixResult
            {
                FixedText = original,
                ReplacementsApplied = 0,
                SkippedNoSuggestion = 0,
                Unchanged = true,
            };
        }

        // Plan: (location, length, expected word, replacement) — only candidates with a top suggestion.
        var planned = new List<(int Location, int Length, string Word, string Replacement)>(misses.Count);
        int skippedNoSuggestion = 0;

        foreach (var miss in misses)
        {
            if (miss is null)
                continue;

            string? top = null;
            if (miss.Suggestions is { Count: > 0 })
            {
                for (int i = 0; i < miss.Suggestions.Count; i++)
                {
                    var s = miss.Suggestions[i];
                    if (!string.IsNullOrEmpty(s))
                    {
                        top = s;
                        break;
                    }
                }
            }

            if (top is null)
            {
                skippedNoSuggestion++;
                continue;
            }

            // Engine emits uint ranges; C# string indices are int.
            if (miss.Utf16Location > int.MaxValue || miss.Utf16Length > int.MaxValue)
                continue;

            int loc = (int)miss.Utf16Location;
            int len = (int)miss.Utf16Length;
            if (loc < 0 || len < 0 || loc > original.Length || len > original.Length - loc)
                continue;

            // Only apply when the range still matches the reported word (best-effort safety).
            if (!string.Equals(original.Substring(loc, len), miss.Word, StringComparison.Ordinal))
                continue;

            planned.Add((loc, len, miss.Word, top));
        }

        if (planned.Count == 0)
        {
            return new ClipboardFixResult
            {
                FixedText = original,
                ReplacementsApplied = 0,
                SkippedNoSuggestion = skippedNoSuggestion,
                Unchanged = true,
            };
        }

        // Back-to-front so earlier UTF-16 offsets remain valid after later edits.
        planned.Sort(static (a, b) => b.Location.CompareTo(a.Location));

        string text = original;
        int applied = 0;
        foreach (var (location, length, word, replacement) in planned)
        {
            if (location > text.Length || length > text.Length - location)
                continue;
            if (!text.AsSpan(location, length).Equals(word.AsSpan(), StringComparison.Ordinal))
                continue;

            text = string.Concat(
                text.AsSpan(0, location),
                replacement,
                text.AsSpan(location + length));
            applied++;
        }

        return new ClipboardFixResult
        {
            FixedText = text,
            ReplacementsApplied = applied,
            SkippedNoSuggestion = skippedNoSuggestion,
            Unchanged = applied == 0 || string.Equals(text, original, StringComparison.Ordinal),
        };
    }
}
