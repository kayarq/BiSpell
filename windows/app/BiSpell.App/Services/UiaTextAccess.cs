using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BiSpell.Interop;

namespace BiSpell.Services;

/// <summary>
/// Soft-fail UI Automation facade for focused-control <b>ValuePattern</b> read / write.
/// <para>
/// <b>Threading:</b> call from the UI / STA thread that owns the WinUI dispatcher (or any
/// apartment where COM is already initialized). Do not call from a random thread-pool
/// worker without <c>CoInitialize</c>/<c>CoInitializeEx</c>. This type does not create
/// background threads or register continuous UIA event handlers.
/// </para>
/// <para>
/// <b>Contracts:</b> never throws to callers; COM / hostile-provider failures → soft log
/// via <see cref="CrashLog"/> + <c>null</c>/<c>false</c>. Password fields
/// (<c>CurrentIsPassword</c>) refuse both read and write. Smoke
/// (<see cref="CrashLog.IsSmokeMode"/>) early-returns without touching UIA so headless
/// launch cannot hang on a focused provider.
/// </para>
/// <para>
/// Zero NuGet / no WPF: activates <c>CUIAutomation</c> via COM interop
/// (see <c>BiSpell.Interop.UiaComInterop</c>). Packaging flags unchanged.
/// </para>
/// </summary>
public sealed class UiaTextAccess
{
    private static readonly object AutomationGate = new();
    private static IUIAutomation? s_automation;
    private static bool s_automationFailed;

    /// <summary>
    /// Probe the focused element: control type, patterns, process, tier estimate.
    /// Never throws. Returns <c>null</c> in smoke mode or when UIA cannot start;
    /// otherwise a snapshot (Tier C on soft failure is preferred over null when useful).
    /// </summary>
    public UiaFocusSnapshot? TryProbeFocused()
    {
        if (CrashLog.IsSmokeMode)
        {
            LogSoft("TryProbeFocused: skipped (smoke mode)");
            return null;
        }

        try
        {
            return Capture(writeText: null, out _);
        }
        catch (Exception ex)
        {
            LogSoft($"TryProbeFocused: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read the focused element's ValuePattern text.
    /// Never throws. Password / no pattern / COM fail → <c>false</c> with best-effort meta.
    /// Empty string is a successful read when ValuePattern returns empty.
    /// </summary>
    public bool TryReadFocusedValue(out string? text, out UiaFocusSnapshot? meta)
    {
        text = null;
        meta = null;

        if (CrashLog.IsSmokeMode)
        {
            LogSoft("TryReadFocusedValue: skipped (smoke mode)");
            return false;
        }

        try
        {
            meta = Capture(writeText: null, out _);
            if (meta is null)
                return false;

            if (meta.IsPassword)
            {
                LogSoft("TryReadFocusedValue: password field refused");
                return false;
            }

            if (!meta.CanReadValue)
                return false;

            text = meta.Value ?? string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            LogSoft($"TryReadFocusedValue: {ex.GetType().Name}: {ex.Message}");
            text = null;
            return false;
        }
    }

    /// <summary>
    /// Write <paramref name="text"/> to the focused element via ValuePattern.SetValue.
    /// Never throws. Refuses password and read-only; no continuous listeners.
    /// </summary>
    public bool TryWriteFocusedValue(string text, out UiaFocusSnapshot? meta)
    {
        meta = null;

        if (text is null)
            return false;

        if (CrashLog.IsSmokeMode)
        {
            LogSoft("TryWriteFocusedValue: skipped (smoke mode)");
            return false;
        }

        try
        {
            meta = Capture(writeText: text, out bool writeOk);
            return writeOk;
        }
        catch (Exception ex)
        {
            LogSoft($"TryWriteFocusedValue: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Core capture path. Soft-fail throughout; outer methods add smoke / password policy.
    /// When <paramref name="writeText"/> is non-null, attempts SetValue after pattern acquire.
    /// Always probes identity + ValuePattern and attempts CurrentValue for meta/tier.
    /// </summary>
    private static UiaFocusSnapshot? Capture(string? writeText, out bool writeOk)
    {
        writeOk = false;
        var notes = new StringBuilder();

        IUIAutomation? auto = TryGetAutomation();
        if (auto is null)
        {
            notes.Append("no CUIAutomation");
            return new UiaFocusSnapshot
            {
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }

        IUIAutomationElement? element;
        try
        {
            element = auto.GetFocusedElement();
        }
        catch (COMException ex)
        {
            notes.Append("GetFocusedElement COM 0x").Append(ex.HResult.ToString("X8"));
            LogSoft($"GetFocusedElement: COMException 0x{ex.HResult:X8}: {ex.Message}");
            return new UiaFocusSnapshot
            {
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }
        catch (Exception ex)
        {
            notes.Append("GetFocusedElement ").Append(ex.GetType().Name);
            LogSoft($"GetFocusedElement: {ex.GetType().Name}: {ex.Message}");
            return new UiaFocusSnapshot
            {
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }

        if (element is null)
        {
            notes.Append("no focused element");
            return new UiaFocusSnapshot
            {
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }

        // Best-effort identity props — each access isolated so one failure does not kill the probe.
        string? name = SafeGet(() => element.CurrentName, "Name", notes);
        int controlTypeId = SafeGet(() => element.CurrentControlType, "ControlType", notes, defaultValue: 0);
        string? localizedType = SafeGet(() => element.CurrentLocalizedControlType, "LocalizedControlType", notes);
        string? className = SafeGet(() => element.CurrentClassName, "ClassName", notes);
        string? automationId = SafeGet(() => element.CurrentAutomationId, "AutomationId", notes);
        int processId = SafeGet(() => element.CurrentProcessId, "ProcessId", notes, defaultValue: 0);
        bool isPassword = SafeGet(() => element.CurrentIsPassword, "IsPassword", notes, defaultValue: false);

        string controlLabel = !string.IsNullOrWhiteSpace(localizedType)
            ? localizedType!
            : ControlTypeLabel(controlTypeId);

        string? processName = TryResolveProcessName(processId);
        bool isOwn = processId > 0 && processId == Environment.ProcessId;

        if (isPassword)
        {
            AppendNote(notes, "password refused");
            return new UiaFocusSnapshot
            {
                Name = name,
                ControlType = controlLabel,
                ClassName = className,
                AutomationId = automationId,
                ProcessId = processId,
                ProcessName = processName,
                IsOwnProcess = isOwn,
                CanReadValue = false,
                CanWriteValue = false,
                IsPassword = true,
                Value = null,
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }

        IUIAutomationValuePattern? valuePattern = null;
        try
        {
            object? raw = element.GetCurrentPattern(UiaConstants.ValuePatternId);
            valuePattern = raw as IUIAutomationValuePattern;
            if (valuePattern is null && raw is not null)
            {
                // Some runtimes return a COM RCW that needs an explicit cast.
                try
                {
                    valuePattern = (IUIAutomationValuePattern)raw;
                }
                catch
                {
                    valuePattern = null;
                }
            }
        }
        catch (COMException ex)
        {
            AppendNote(notes, $"GetCurrentPattern COM 0x{ex.HResult:X8}");
            LogSoft($"GetCurrentPattern: COMException 0x{ex.HResult:X8}: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppendNote(notes, $"GetCurrentPattern {ex.GetType().Name}");
            LogSoft($"GetCurrentPattern: {ex.GetType().Name}: {ex.Message}");
        }

        if (valuePattern is null)
        {
            AppendNote(notes, "no ValuePattern");
            return new UiaFocusSnapshot
            {
                Name = name,
                ControlType = controlLabel,
                ClassName = className,
                AutomationId = automationId,
                ProcessId = processId,
                ProcessName = processName,
                IsOwnProcess = isOwn,
                CanReadValue = false,
                CanWriteValue = false,
                IsPassword = false,
                Value = null,
                Tier = UiaSupportTier.C,
                Notes = notes.ToString(),
            };
        }

        AppendNote(notes, "ValuePattern");

        bool isReadOnly = true;
        try
        {
            isReadOnly = valuePattern.CurrentIsReadOnly;
        }
        catch (Exception ex)
        {
            AppendNote(notes, $"IsReadOnly {ex.GetType().Name}");
            // Assume read-only on failure so we do not claim write capability.
            isReadOnly = true;
        }

        bool canWrite = !isReadOnly;
        string? value = null;
        bool canRead = false;

        // Always attempt CurrentValue for snapshot meta (probe/read/write).
        // null BSTR → empty string still counts as a successful ValuePattern read.
        try
        {
            value = valuePattern.CurrentValue ?? string.Empty;
            canRead = true;
        }
        catch (Exception ex)
        {
            AppendNote(notes, $"CurrentValue {ex.GetType().Name}");
            LogSoft($"CurrentValue: {ex.GetType().Name}: {ex.Message}");
            canRead = false;
            value = null;
        }

        if (writeText is not null)
        {
            if (!canWrite)
            {
                AppendNote(notes, "SetValue skipped (read-only)");
                writeOk = false;
            }
            else
            {
                try
                {
                    valuePattern.SetValue(writeText);
                    writeOk = true;
                    value = writeText;
                    canRead = true;
                    AppendNote(notes, "SetValue ok");
                }
                catch (COMException ex)
                {
                    writeOk = false;
                    AppendNote(notes, $"SetValue COM 0x{ex.HResult:X8}");
                    LogSoft($"SetValue: COMException 0x{ex.HResult:X8}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    writeOk = false;
                    AppendNote(notes, $"SetValue {ex.GetType().Name}");
                    LogSoft($"SetValue: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        UiaSupportTier tier;
        if (canRead && canWrite)
            tier = UiaSupportTier.A;
        else if (canRead)
            tier = UiaSupportTier.B;
        else
            tier = UiaSupportTier.C;

        // Write attempted but failed → demote at least to B if still readable, else C.
        if (writeText is not null && !writeOk && canRead)
            tier = UiaSupportTier.B;
        else if (writeText is not null && !writeOk && !canRead)
            tier = UiaSupportTier.C;

        return new UiaFocusSnapshot
        {
            Name = name,
            ControlType = controlLabel,
            ClassName = className,
            AutomationId = automationId,
            ProcessId = processId,
            ProcessName = processName,
            IsOwnProcess = isOwn,
            CanReadValue = canRead,
            CanWriteValue = canWrite,
            IsPassword = false,
            Value = value,
            Tier = tier,
            Notes = notes.ToString(),
        };
    }

    private static IUIAutomation? TryGetAutomation()
    {
        lock (AutomationGate)
        {
            if (s_automation is not null)
                return s_automation;
            if (s_automationFailed)
                return null;

            try
            {
                // CoCreate CUIAutomation coclass → IUIAutomation.
                s_automation = (IUIAutomation)new CUIAutomationCom();
                return s_automation;
            }
            catch (Exception ex)
            {
                s_automationFailed = true;
                LogSoft($"CUIAutomation activate: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }

    private static string ControlTypeLabel(int id) => id switch
    {
        UiaConstants.ButtonControlTypeId => "Button",
        UiaConstants.ComboBoxControlTypeId => "ComboBox",
        UiaConstants.EditControlTypeId => "Edit",
        UiaConstants.SpinnerControlTypeId => "Spinner",
        UiaConstants.TextControlTypeId => "Text",
        UiaConstants.DocumentControlTypeId => "Document",
        0 => "—",
        _ => $"ControlType:{id}",
    };

    private static string? TryResolveProcessName(int processId)
    {
        if (processId <= 0)
            return null;
        try
        {
            using var p = Process.GetProcessById(processId);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static T SafeGet<T>(Func<T> getter, string prop, StringBuilder notes, T defaultValue = default!)
    {
        try
        {
            return getter();
        }
        catch (Exception ex)
        {
            AppendNote(notes, $"{prop} {ex.GetType().Name}");
            return defaultValue;
        }
    }

    private static void AppendNote(StringBuilder notes, string fragment)
    {
        if (notes.Length > 0)
            notes.Append("; ");
        notes.Append(fragment);
    }

    private static void LogSoft(string message)
    {
        try
        {
            CrashLog.Write("uia: " + message);
        }
        catch
        {
            // Never throw from soft-fail paths.
        }
    }
}
