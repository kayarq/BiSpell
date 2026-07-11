using System.Text.Json;
using System.Text.Json.Serialization;
using BiSpell.Interop;

namespace BiSpell.Services;

/// <summary>
/// Spell-relevant settings subset for the in-app editor. Persisted as JSON at
/// <c>%APPDATA%\BiSpell\settings.json</c>.
/// Extra JSON keys from older builds (globalHotkeyEnabled, clipboardReplaceEnabled,
/// uiaAssistEnabled) are ignored on load and not rewritten.
/// </summary>
public sealed class AppUserSettings
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("turkishEnabled")]
    public bool TurkishEnabled { get; set; } = true;

    [JsonPropertyName("englishEnabled")]
    public bool EnglishEnabled { get; set; } = true;

    [JsonPropertyName("maxSuggestions")]
    public int MaxSuggestions { get; set; } = 5;

    [JsonPropertyName("minWordLength")]
    public int MinWordLength { get; set; } = 2;

    [JsonPropertyName("debounceMilliseconds")]
    public int DebounceMilliseconds { get; set; } = 250;

    /// <summary>
    /// Shell-only: when true (default), the in-app editor may run debounced as-you-type
    /// spell check. Not part of <see cref="BispellSettings"/> / native ABI.
    /// JSON: <c>asYouTypeEnabled</c>. Missing key → default true.
    /// </summary>
    [JsonPropertyName("asYouTypeEnabled")]
    public bool AsYouTypeEnabled { get; set; } = true;

    public static AppUserSettings CreateDefault() => new();

    /// <summary>Clamp values to safe ranges used by the engine UI.</summary>
    public void Normalize()
    {
        if (MaxSuggestions < 1) MaxSuggestions = 1;
        if (MaxSuggestions > 20) MaxSuggestions = 20;
        if (MinWordLength < 1) MinWordLength = 1;
        if (MinWordLength > 10) MinWordLength = 10;
        if (DebounceMilliseconds < 0) DebounceMilliseconds = 0;
        if (DebounceMilliseconds > 5000) DebounceMilliseconds = 5000;

        // Keep at least one language enabled so empty checks are not confusing.
        if (!TurkishEnabled && !EnglishEnabled)
            EnglishEnabled = true;
    }

    /// <summary>
    /// Native engine settings only. Does <b>not</b> include shell-only
    /// <see cref="AsYouTypeEnabled"/>. <see cref="DebounceMilliseconds"/> is included.
    /// </summary>
    public BispellSettings ToNative()
    {
        Normalize();
        return new BispellSettings
        {
            IsEnabled = IsEnabled ? 1 : 0,
            TurkishEnabled = TurkishEnabled ? 1 : 0,
            EnglishEnabled = EnglishEnabled ? 1 : 0,
            MaxSuggestions = MaxSuggestions,
            MinWordLength = MinWordLength,
            DebounceMilliseconds = DebounceMilliseconds,
        };
    }

    public AppUserSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        TurkishEnabled = TurkishEnabled,
        EnglishEnabled = EnglishEnabled,
        MaxSuggestions = MaxSuggestions,
        MinWordLength = MinWordLength,
        DebounceMilliseconds = DebounceMilliseconds,
        AsYouTypeEnabled = AsYouTypeEnabled,
    };
}

/// <summary>
/// Load/save <see cref="AppUserSettings"/> from
/// <c>%APPDATA%\BiSpell\settings.json</c>.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsPath;
    }

    public string Path => _path;

    public AppUserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return AppUserSettings.CreateDefault();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return AppUserSettings.CreateDefault();

            var settings = JsonSerializer.Deserialize<AppUserSettings>(json, JsonOptions)
                           ?? AppUserSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch
        {
            // Corrupt or unreadable file → defaults (do not crash startup).
            return AppUserSettings.CreateDefault();
        }
    }

    public void Save(AppUserSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        settings.Normalize();

        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            // Atomic-ish write: write temp then replace.
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, _path, overwrite: true);
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
        catch
        {
            // Persistence is best-effort; UI keeps working with in-memory settings.
        }
    }
}
