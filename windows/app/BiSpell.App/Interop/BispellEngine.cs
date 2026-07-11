using System.Runtime.InteropServices;
using BiSpell.Models;

namespace BiSpell.Interop;

/// <summary>
/// Managed RAII wrapper around <c>bispell_engine*</c> from the C ABI.
/// Thread-safety: not concurrent without an external lock (matches native contract).
/// </summary>
public sealed class BispellEngine : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private BispellEngine(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Create engine from a dictionary directory that must contain
    /// <c>tr.dic</c> and <c>en_US.dic</c>.
    /// </summary>
    /// <param name="dictDir">UTF-8 path to dictionary directory.</param>
    /// <param name="lexiconPath">
    /// Optional path to user-lexicon JSON. Null or empty = memory-only lexicon.
    /// Default file location on Windows: %APPDATA%\BiSpell\user-lexicon.json
    /// </param>
    /// <param name="settings">Optional; null uses native defaults.</param>
    /// <exception cref="BispellException">When create fails (e.g. missing dictionaries).</exception>
    public static BispellEngine Create(
        string dictDir,
        string? lexiconPath = null,
        BispellSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(dictDir))
            throw new ArgumentException("Dictionary directory is required.", nameof(dictDir));

        var s = settings ?? BispellSettings.CreateDefault();
        // Empty string → pass null for memory-only (C API treats NULL/"" as memory-only).
        string? lex = string.IsNullOrEmpty(lexiconPath) ? null : lexiconPath;

        IntPtr handle = NativeMethods.bispell_engine_create(dictDir, lex, in s);
        if (handle == IntPtr.Zero)
        {
            var err = NativeString.LastError();
            throw new BispellException(
                string.IsNullOrEmpty(err)
                    ? $"Failed to create spell engine (dict_dir={dictDir})."
                    : err);
        }
        return new BispellEngine(handle);
    }

    public IReadOnlyList<MisspellingItem> Check(
        string text,
        int caretUtf16 = -1,
        bool nearCaretOnly = false,
        int windowRadius = 120)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        text ??= string.Empty;

        int rc = NativeMethods.bispell_engine_check(
            _handle,
            text,
            caretUtf16,
            nearCaretOnly ? 1 : 0,
            windowRadius,
            out IntPtr resultPtr);

        if (rc != 0 || resultPtr == IntPtr.Zero)
        {
            var err = NativeString.LastError();
            throw new BispellException(
                string.IsNullOrEmpty(err) ? $"Spell check failed (code {rc})." : err);
        }

        try
        {
            return ReadCheckResult(resultPtr);
        }
        finally
        {
            NativeMethods.bispell_check_result_free(resultPtr);
        }
    }

    public IReadOnlyList<string> Suggestions(string word, BispellLanguage language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(word))
            return Array.Empty<string>();

        int rc = NativeMethods.bispell_engine_suggestions(
            _handle,
            word,
            (int)language,
            out IntPtr list,
            out UIntPtr count);

        if (rc != 0)
        {
            var err = NativeString.LastError();
            throw new BispellException(
                string.IsNullOrEmpty(err) ? $"Suggestions failed (code {rc})." : err);
        }

        try
        {
            return NativeString.ReadStringList(list, count);
        }
        finally
        {
            if (list != IntPtr.Zero)
                NativeMethods.bispell_string_list_free(list, count);
        }
    }

    public void AddToDictionary(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(word)) return;
        int rc = NativeMethods.bispell_engine_add_to_dictionary(_handle, word);
        if (rc != 0)
            throw new BispellException(NativeString.LastError() is { Length: > 0 } e
                ? e
                : "add_to_dictionary failed");
    }

    public void IgnoreWord(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(word)) return;
        int rc = NativeMethods.bispell_engine_ignore_word(_handle, word);
        if (rc != 0)
            throw new BispellException(NativeString.LastError() is { Length: > 0 } e
                ? e
                : "ignore_word failed");
    }

    public void RemoveFromDictionary(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(word)) return;
        int rc = NativeMethods.bispell_engine_remove_from_dictionary(_handle, word);
        if (rc != 0)
            throw new BispellException(NativeString.LastError() is { Length: > 0 } e
                ? e
                : "remove_from_dictionary failed");
    }

    public void UnignoreWord(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(word)) return;
        int rc = NativeMethods.bispell_engine_unignore_word(_handle, word);
        if (rc != 0)
            throw new BispellException(NativeString.LastError() is { Length: > 0 } e
                ? e
                : "unignore_word failed");
    }

    /// <summary>
    /// Live personal-dictionary words (sorted). Empty list does not throw.
    /// </summary>
    public IReadOnlyList<string> ListAddedWords()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int rc = NativeMethods.bispell_engine_list_added_words(
            _handle,
            out IntPtr list,
            out UIntPtr count);

        if (rc != 0)
        {
            var err = NativeString.LastError();
            throw new BispellException(
                string.IsNullOrEmpty(err) ? $"list_added_words failed (code {rc})." : err);
        }

        try
        {
            return NativeString.ReadStringList(list, count);
        }
        finally
        {
            if (list != IntPtr.Zero)
                NativeMethods.bispell_string_list_free(list, count);
        }
    }

    /// <summary>
    /// Live ignored words (sorted). Empty list does not throw.
    /// Does not include per-app ignoredInApps entries.
    /// </summary>
    public IReadOnlyList<string> ListIgnoredWords()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int rc = NativeMethods.bispell_engine_list_ignored_words(
            _handle,
            out IntPtr list,
            out UIntPtr count);

        if (rc != 0)
        {
            var err = NativeString.LastError();
            throw new BispellException(
                string.IsNullOrEmpty(err) ? $"list_ignored_words failed (code {rc})." : err);
        }

        try
        {
            return NativeString.ReadStringList(list, count);
        }
        finally
        {
            if (list != IntPtr.Zero)
                NativeMethods.bispell_string_list_free(list, count);
        }
    }

    public void UpdateSettings(BispellSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int rc = NativeMethods.bispell_engine_update_settings(_handle, in settings);
        if (rc != 0)
            throw new BispellException(NativeString.LastError() is { Length: > 0 } e
                ? e
                : "update_settings failed");
    }

    private static IReadOnlyList<MisspellingItem> ReadCheckResult(IntPtr resultPtr)
    {
        var native = Marshal.PtrToStructure<BispellCheckResultNative>(resultPtr);
        int count = (int)native.Count;
        if (count <= 0 || native.Items == IntPtr.Zero)
            return Array.Empty<MisspellingItem>();

        int stride = Marshal.SizeOf<BispellMisspellingNative>();
        var list = new List<MisspellingItem>(count);

        for (int i = 0; i < count; i++)
        {
            var itemPtr = IntPtr.Add(native.Items, i * stride);
            var m = Marshal.PtrToStructure<BispellMisspellingNative>(itemPtr);
            var word = NativeString.PtrToUtf8(m.Word) ?? string.Empty;
            var suggestions = NativeString.ReadStringList(m.Suggestions, m.SuggestionCount);

            list.Add(new MisspellingItem
            {
                Word = word,
                Utf16Location = m.Utf16Location,
                Utf16Length = m.Utf16Length,
                Language = (BispellLanguage)m.Language,
                Suggestions = suggestions,
            });
        }

        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.bispell_engine_free(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~BispellEngine()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.bispell_engine_free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

/// <summary>Native BiSpell C ABI error.</summary>
public sealed class BispellException : Exception
{
    public BispellException(string message) : base(message) { }
    public BispellException(string message, Exception inner) : base(message, inner) { }
}
