// Minimal UI Automation COM interop (UIAutomationCore / CUIAutomation).
// Mandate A: hand-rolled [ComImport] subset only — no NuGet, no WPF/WinForms.
// Interfaces are internal; public surface is BiSpell.Services.UiaTextAccess.
//
// Vtable note: uncalled members exist only to keep method ordinals aligned with
// uiautomationclient.h. Never invoke the Placeholder* methods.

using System.Runtime.InteropServices;

namespace BiSpell.Interop;

/// <summary>CLSID / pattern / control-type constants used by the UIA facade.</summary>
internal static class UiaConstants
{
    /// <summary>CLSID_CUIAutomation — <c>{ff48dba4-60ef-4201-aa87-54103eef594e}</c>.</summary>
    public static readonly Guid ClsidCUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    /// <summary>UIA_ValuePatternId = 10002.</summary>
    public const int ValuePatternId = 10002;

    // Common control type IDs (subset for probe labels).
    public const int ButtonControlTypeId = 50000;
    public const int ComboBoxControlTypeId = 50003;
    public const int EditControlTypeId = 50004;
    public const int SpinnerControlTypeId = 50018;
    public const int TextControlTypeId = 50020;
    public const int DocumentControlTypeId = 50030;
}

/// <summary>
/// Coclass for <c>CUIAutomation</c>. Activate via <c>new CUIAutomationCom()</c>
/// cast to <see cref="IUIAutomation"/>.
/// </summary>
[ComImport]
[Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
[ClassInterface(ClassInterfaceType.None)]
internal class CUIAutomationCom
{
}

/// <summary>
/// Minimal <c>IUIAutomation</c> (IID <c>30cbe57d-d9d0-452a-ab13-7ac5ac4825ee</c>).
/// Only <see cref="GetFocusedElement"/> is used; preceding slots are placeholders.
/// </summary>
[ComImport]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    // slot 0 CompareElements
    void Placeholder00();
    // slot 1 CompareRuntimeIds
    void Placeholder01();
    // slot 2 GetRootElement
    void Placeholder02();
    // slot 3 ElementFromHandle
    void Placeholder03();
    // slot 4 ElementFromPoint
    void Placeholder04();

    /// <summary>IUIAutomation::GetFocusedElement — focused desktop element or COM error.</summary>
    IUIAutomationElement GetFocusedElement();
}

/// <summary>
/// Minimal <c>IUIAutomationElement</c> (IID <c>d22108aa-8ac5-49a5-837b-37bbb3d7591e</c>).
/// Slots up through <c>CurrentIsPassword</c>; uncalled members are placeholders.
/// </summary>
[ComImport]
[Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    // 0 SetFocus
    void Placeholder00();
    // 1 GetRuntimeId
    void Placeholder01();
    // 2 FindFirst
    void Placeholder02();
    // 3 FindAll
    void Placeholder03();
    // 4 FindFirstBuildCache
    void Placeholder04();
    // 5 FindAllBuildCache
    void Placeholder05();
    // 6 BuildUpdatedCache
    void Placeholder06();
    // 7 GetCurrentPropertyValue
    void Placeholder07();
    // 8 GetCurrentPropertyValueEx
    void Placeholder08();
    // 9 GetCachedPropertyValue
    void Placeholder09();
    // 10 GetCachedPropertyValueEx
    void Placeholder10();
    // 11 GetCurrentPatternAs
    void Placeholder11();
    // 12 GetCachedPatternAs
    void Placeholder12();

    /// <summary>IUIAutomationElement::GetCurrentPattern — IUnknown for the pattern (or null).</summary>
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetCurrentPattern(int patternId);

    // 14 GetCachedPattern
    void Placeholder14();
    // 15 GetCachedParent
    void Placeholder15();
    // 16 GetCachedChildren
    void Placeholder16();

    // 17
    int CurrentProcessId { get; }

    // 18
    int CurrentControlType { get; }

    // 19
    string? CurrentLocalizedControlType
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    // 20
    string? CurrentName
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    // 21 CurrentAcceleratorKey
    void Placeholder21();
    // 22 CurrentAccessKey
    void Placeholder22();
    // 23 CurrentHasKeyboardFocus
    void Placeholder23();
    // 24 CurrentIsKeyboardFocusable
    void Placeholder24();
    // 25 CurrentIsEnabled
    void Placeholder25();

    // 26
    string? CurrentAutomationId
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    // 27
    string? CurrentClassName
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    // 28 CurrentHelpText
    void Placeholder28();
    // 29 CurrentCulture
    void Placeholder29();
    // 30 CurrentIsControlElement
    void Placeholder30();
    // 31 CurrentIsContentElement
    void Placeholder31();

    // 32
    bool CurrentIsPassword
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        get;
    }
}

/// <summary>
/// Minimal <c>IUIAutomationValuePattern</c> (IID <c>a94cd8b1-0844-4cd6-9d2d-640537ab39e9</c>).
/// </summary>
[ComImport]
[Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    void SetValue([MarshalAs(UnmanagedType.BStr)] string val);

    string? CurrentValue
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    bool CurrentIsReadOnly
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        get;
    }
}
