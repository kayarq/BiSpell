namespace BiSpell.Services;

/// <summary>
/// Windows user-data locations under %APPDATA%\BiSpell\
/// (mirrors C++ <c>bispell::paths</c> defaults).
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "BiSpell";
    public const string SettingsFileName = "settings.json";
    public const string LexiconFileName = "user-lexicon.json";
    public const string NotesFolderName = "Notes";

    /// <summary>%APPDATA%\BiSpell\ — creates the directory when possible.</summary>
    public static string ConfigDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
            {
                // Fallback for unusual profiles; still prefer a writable local path.
                appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "Roaming");
            }

            var dir = Path.Combine(appData, AppFolderName);
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // Callers may still use the path; create failure is non-fatal for read attempts.
            }

            return dir;
        }
    }

    public static string SettingsPath => Path.Combine(ConfigDirectory, SettingsFileName);

    public static string LexiconPath => Path.Combine(ConfigDirectory, LexiconFileName);

    /// <summary>%APPDATA%\BiSpell\Notes\ — plain-text note files for the in-app Notes MVP.</summary>
    public static string NotesDirectory
    {
        get
        {
            var dir = Path.Combine(ConfigDirectory, NotesFolderName);
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // Best-effort; callers handle missing dir.
            }

            return dir;
        }
    }
}
