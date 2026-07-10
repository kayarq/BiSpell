using System.Runtime.InteropServices;

namespace BiSpell.Interop;

/// <summary>UTF-8 native string helpers for the C ABI.</summary>
internal static class NativeString
{
    public static string? PtrToUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        // Manual UTF-8 decode (Marshal.PtrToStringUTF8 is available on .NET 5+)
        return Marshal.PtrToStringUTF8(ptr);
    }

    public static string LastError()
    {
        var p = NativeMethods.bispell_last_error();
        return PtrToUtf8(p) ?? string.Empty;
    }

    public static IReadOnlyList<string> ReadStringList(IntPtr list, nuint count)
    {
        if (list == IntPtr.Zero || count == 0)
            return Array.Empty<string>();

        var result = new string[(int)count];
        for (int i = 0; i < (int)count; i++)
        {
            var sp = Marshal.ReadIntPtr(list, i * IntPtr.Size);
            result[i] = PtrToUtf8(sp) ?? string.Empty;
        }
        return result;
    }
}
