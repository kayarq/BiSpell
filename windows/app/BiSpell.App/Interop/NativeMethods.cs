// Real P/Invoke bindings to windows/core/include/bispell/c_api.h
// DLL: bispell_core.dll (built from windows/core via CMake target bispell_core_shared)

using System.Runtime.InteropServices;

namespace BiSpell.Interop;

/// <summary>Language codes matching <c>bispell_language</c> in c_api.h.</summary>
public enum BispellLanguage
{
    Turkish = 0,
    English = 1,
    Unknown = 2,
}

/// <summary>Maps to <c>bispell_settings</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BispellSettings
{
    public int IsEnabled;
    public int TurkishEnabled;
    public int EnglishEnabled;
    public int MaxSuggestions;
    public int MinWordLength;
    public int DebounceMilliseconds;

    public static BispellSettings CreateDefault()
    {
        try
        {
            NativeMethods.bispell_settings_default(out var s);
            return s;
        }
        catch (DllNotFoundException)
        {
            // Fallback if DLL not yet on PATH (design-time / early boot).
            return new BispellSettings
            {
                IsEnabled = 1,
                TurkishEnabled = 1,
                EnglishEnabled = 1,
                MaxSuggestions = 5,
                MinWordLength = 2,
                DebounceMilliseconds = 250,
            };
        }
    }
}

/// <summary>Maps to <c>bispell_misspelling</c> (native layout for marshaling).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BispellMisspellingNative
{
    public IntPtr Word;              // const char* UTF-8
    public uint Utf16Location;
    public uint Utf16Length;
    public int Language;
    public IntPtr Suggestions;       // char**
    public UIntPtr SuggestionCount;  // size_t
}

/// <summary>Maps to <c>bispell_check_result</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BispellCheckResultNative
{
    public IntPtr Items;             // bispell_misspelling*
    public UIntPtr Count;            // size_t
    public IntPtr SourceText;        // char* UTF-8
}

/// <summary>
/// P/Invoke surface for the BiSpell C ABI (<c>bispell/c_api.h</c>).
/// Calling convention: Cdecl. Strings are UTF-8. Symbols match c_api.h exactly.
/// </summary>
internal static class NativeMethods
{
    /// <summary>Native library name without extension (loads bispell_core.dll on Windows).</summary>
    public const string DllName = "bispell_core";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void bispell_settings_default(out BispellSettings settings);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr bispell_engine_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dictDir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? lexiconPath,
        in BispellSettings settings);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void bispell_engine_free(IntPtr engine);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_check(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string textUtf8,
        int caretUtf16,
        int nearCaretOnly,
        int windowRadius,
        out IntPtr outResult);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_check_strict(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string textUtf8,
        int caretUtf16,
        int nearCaretOnly,
        int windowRadius,
        out IntPtr outResult);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void bispell_check_result_free(IntPtr result);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_suggestions(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string wordUtf8,
        int language,
        out IntPtr outList,
        out UIntPtr outCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void bispell_string_list_free(IntPtr list, UIntPtr count);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_add_to_dictionary(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_ignore_word(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_remove_from_dictionary(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_unignore_word(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_list_added_words(
        IntPtr engine,
        out IntPtr outList,
        out UIntPtr outCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_list_ignored_words(
        IntPtr engine,
        out IntPtr outList,
        out UIntPtr outCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int bispell_engine_update_settings(
        IntPtr engine,
        in BispellSettings settings);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr bispell_last_error();
}
