using BiSpell.Interop;

namespace BiSpell.Models;

/// <summary>Managed misspelling with UTF-16 range for WinUI text apply.</summary>
public sealed class MisspellingItem
{
    public required string Word { get; init; }

    /// <summary>UTF-16 code unit offset into the source text (C# string index).</summary>
    public uint Utf16Location { get; init; }

    /// <summary>UTF-16 code unit length.</summary>
    public uint Utf16Length { get; init; }

    public BispellLanguage Language { get; init; }

    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    public string LanguageLabel => Language switch
    {
        BispellLanguage.Turkish => "TR",
        BispellLanguage.English => "EN",
        _ => "?",
    };

    public string Display => $"{Word}  [{LanguageLabel}]  @{Utf16Location}+{Utf16Length}";

    public string RangeLabel => $"UTF-16 [{Utf16Location}, {Utf16Length}]";
}
