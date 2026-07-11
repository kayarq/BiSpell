using System.Text;

namespace BiSpell.Services;

/// <summary>
/// Lightweight note file entry for the in-app Notes MVP.
/// Title is derived from the first non-empty line of body text (or "Untitled").
/// </summary>
public sealed class NoteItem
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>Display title (first non-empty line truncated, or "Untitled").</summary>
    public string Title { get; set; } = "Untitled";

    public DateTime LastWriteUtc { get; set; }

    public override string ToString() => Title;
}

/// <summary>
/// Persist plain-text notes under <c>%APPDATA%\BiSpell\Notes\</c> as <c>.txt</c> files.
/// No templates, taxonomy, or markdown preview — just list / load / save / delete.
/// </summary>
public sealed class NotesStore
{
    /// <summary>
    /// Optional help text for a new note the user can create — never auto-inserted
    /// into an empty notes folder (user may keep the list empty).
    /// </summary>
    public const string OptionalHelpBody =
        "Type or paste here. Spelling is checked as you type (or press F7).\n";

    private readonly string _directory;

    public NotesStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.NotesDirectory;
        try { Directory.CreateDirectory(_directory); } catch { /* best-effort */ }
    }

    public string DirectoryPath => _directory;

    /// <summary>
    /// List notes newest-first. Never auto-creates a welcome/sample note —
    /// empty folder stays empty (use New in the UI).
    /// </summary>
    public IReadOnlyList<NoteItem> ListNotes()
    {
        var list = new List<NoteItem>();
        try
        {
            if (!Directory.Exists(_directory))
                Directory.CreateDirectory(_directory);

            foreach (var path in Directory.EnumerateFiles(_directory, "*.txt"))
            {
                try
                {
                    list.Add(LoadMeta(path));
                }
                catch
                {
                    // Skip unreadable files.
                }
            }
        }
        catch
        {
            return list;
        }

        list.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
        return list;
    }

    public NoteItem CreateNote(string? initialBody = null)
    {
        Directory.CreateDirectory(_directory);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string name = $"note-{stamp}.txt";
        string path = Path.Combine(_directory, name);
        int n = 0;
        while (File.Exists(path))
        {
            n++;
            name = $"note-{stamp}-{n}.txt";
            path = Path.Combine(_directory, name);
        }

        string body = initialBody ?? string.Empty;
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return LoadMeta(path);
    }

    public string ReadBody(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return string.Empty;
        return File.ReadAllText(filePath);
    }

    public NoteItem SaveBody(string filePath, string body)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("filePath required", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        body ??= string.Empty;
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Copy(tmp, filePath, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }

        return LoadMeta(filePath);
    }

    public bool DeleteNote(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;
        try
        {
            File.Delete(filePath);
            try { File.Delete(filePath + ".tmp"); } catch { /* ignore */ }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string TitleFromBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "Untitled";

        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            return t.Length <= 48 ? t : t.Substring(0, 48) + "…";
        }

        return "Untitled";
    }

    private NoteItem LoadMeta(string path)
    {
        string body = string.Empty;
        try { body = File.ReadAllText(path); } catch { /* ignore */ }
        DateTime mtime = DateTime.UtcNow;
        try { mtime = File.GetLastWriteTimeUtc(path); } catch { /* ignore */ }

        return new NoteItem
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            Title = TitleFromBody(body),
            LastWriteUtc = mtime,
        };
    }
}
