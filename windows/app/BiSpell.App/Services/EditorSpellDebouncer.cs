using Microsoft.UI.Dispatching;

namespace BiSpell.Services;

/// <summary>
/// Single-shot as-you-type debounce scheduler (P4-DEBOUNCE mandate A).
/// <para>
/// Uses WinUI <see cref="DispatcherQueueTimer"/> with <c>IsRepeating = false</c>.
/// Each <see cref="Schedule"/> supersedes any pending work (stop + restart interval).
/// Pure schedule only — no engine, UIA, or hotkey coupling. GLUE owns TextChanged wiring
/// and supplies the <see cref="Action"/> that runs a spell check.
/// </para>
/// <para>
/// <b>Contracts:</b>
/// <list type="bullet">
/// <item><description><b>Supersede:</b> N rapid <see cref="Schedule"/> calls → at most one fire after the quiet period.</description></item>
/// <item><description><b>Cancel:</b> drops pending fire; safe if none pending; safe to call twice.</description></item>
/// <item><description><b>Smoke:</b> <see cref="Schedule"/> is a no-op when <see cref="CrashLog.IsSmokeMode"/> (never arms the timer).</description></item>
/// <item><description><b>Thread:</b> the scheduled action always runs on the UI dispatcher.</description></item>
/// <item><description><b>Dispose:</b> cancels pending work; no fire after dispose; double-dispose is a no-op.</description></item>
/// <item><description><b>debounceMs ≤ 0:</b> enqueues the action immediately (still supersedes prior pending work).</description></item>
/// </list>
/// Call from the UI thread that owns <paramref name="dispatcherQueue"/> (same pattern as other shell services).
/// </para>
/// </summary>
public sealed class EditorSpellDebouncer : IDisposable
{
    /// <summary>Default near-caret threshold (UTF-16 length) for large editor documents.</summary>
    public const int DefaultNearCaretThreshold = 4000;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;

    /// <summary>
    /// Generation counter: bumped on every <see cref="Schedule"/> / <see cref="Cancel"/> / <see cref="Dispose"/>
    /// so superseded immediate <see cref="DispatcherQueue.TryEnqueue"/> work is dropped.
    /// </summary>
    private int _generation;

    private Action? _pendingAction;
    private int _debounceMilliseconds = 250;
    private bool _armed;
    private bool _disposed;
    private bool _loggedSmokeSkip;

    /// <summary>
    /// Create a debouncer bound to the window's dispatcher queue.
    /// Timer is single-shot; interval is set on each <see cref="Schedule"/>.
    /// </summary>
    /// <param name="dispatcherQueue">UI dispatcher from <c>MainWindow.DispatcherQueue</c>.</param>
    public EditorSpellDebouncer(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _timer = _dispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Debounce wait in milliseconds (default 250). Clamped to 0–5000 to match
    /// <c>AppUserSettings.Normalize</c>. Read by GLUE from settings before/while scheduling;
    /// may also be passed per call via <see cref="Schedule(System.Action,int)"/>.
    /// </summary>
    public int DebounceMilliseconds
    {
        get => _debounceMilliseconds;
        set => _debounceMilliseconds = ClampDebounce(value);
    }

    /// <summary>True when a timer is running or an immediate enqueue is outstanding.</summary>
    public bool IsPending => !_disposed && _armed;

    /// <summary>Current generation id (increments on Schedule/Cancel/Dispose); useful for tests.</summary>
    public int Generation => _generation;

    /// <summary>
    /// Schedule <paramref name="action"/> after <see cref="DebounceMilliseconds"/>.
    /// Supersedes any previously scheduled work. No-op in smoke mode and after dispose.
    /// </summary>
    public void Schedule(Action action)
    {
        ScheduleCore(action, _debounceMilliseconds);
    }

    /// <summary>
    /// Schedule <paramref name="action"/> after <paramref name="debounceMilliseconds"/>
    /// (also updates <see cref="DebounceMilliseconds"/>). Supersedes pending work.
    /// </summary>
    public void Schedule(Action action, int debounceMilliseconds)
    {
        _debounceMilliseconds = ClampDebounce(debounceMilliseconds);
        ScheduleCore(action, _debounceMilliseconds);
    }

    /// <summary>
    /// Optional plan-shaped configure: set debounce + remember action for <see cref="Ping"/>.
    /// Equivalent to assigning <see cref="DebounceMilliseconds"/> and stashing the action
    /// without arming the timer until <see cref="Ping"/>.
    /// </summary>
    public void Configure(int debounceMilliseconds, Action onFire)
    {
        if (_disposed) return;
        if (onFire is null) throw new ArgumentNullException(nameof(onFire));
        _debounceMilliseconds = ClampDebounce(debounceMilliseconds);
        // Stash without arming — next Ping/Schedule reuses this action.
        // Do not cancel an already-armed timer here; GLUE typically Configure once then Ping.
        _pendingAction = onFire;
    }

    /// <summary>
    /// Plan-shaped alias: re-arm the timer using the last action from
    /// <see cref="Configure"/> or <see cref="Schedule"/>. No-op if no action is stashed.
    /// </summary>
    public void Ping()
    {
        if (_disposed) return;
        var action = _pendingAction;
        if (action is null) return;
        ScheduleCore(action, _debounceMilliseconds);
    }

    /// <summary>
    /// Drop any pending fire. Safe if none pending; safe after dispose (no-op).
    /// </summary>
    public void Cancel()
    {
        if (_disposed) return;
        CancelCore(clearPendingAction: true);
    }

    /// <summary>
    /// Cancel pending work, detach timer, and refuse further schedules.
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCore(clearPendingAction: true);
        try
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
        }
        catch
        {
            // Timer teardown is best-effort on teardown paths.
        }
    }

    /// <summary>
    /// Pure helper: prefer near-caret spell checks when document length exceeds threshold.
    /// Optional convenience for GLUE (not dual-critical).
    /// </summary>
    public static bool ShouldUseNearCaret(int textLength, int threshold = DefaultNearCaretThreshold)
        => textLength > threshold;

    private void ScheduleCore(Action action, int debounceMilliseconds)
    {
        if (_disposed) return;
        if (action is null) throw new ArgumentNullException(nameof(action));

        if (CrashLog.IsSmokeMode)
        {
            // Hard no-op: never arm timer / never enqueue fire (headless CI safety).
            if (!_loggedSmokeSkip)
            {
                _loggedSmokeSkip = true;
                CrashLog.Write("EditorSpellDebouncer: Schedule skipped (BISPELL_SMOKE)");
            }
            CancelCore(clearPendingAction: true);
            return;
        }

        // Supersede: bump generation so any prior immediate enqueue is stale;
        // stop the single-shot timer before restarting with a fresh interval.
        _generation++;
        int gen = _generation;
        try { _timer.Stop(); } catch { /* ignore */ }
        _pendingAction = action;
        _armed = true;

        int ms = ClampDebounce(debounceMilliseconds);
        if (ms <= 0)
        {
            // Immediate path still goes through the dispatcher so onFire is UI-thread and
            // re-entrancy from TextChanged handlers does not run nested checks mid-event.
            // Keep _pendingAction stashed for Ping(); clear _armed only when this gen fires.
            bool enqueued = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (_disposed || gen != _generation) return;
                _armed = false;
                var toRun = _pendingAction;
                if (toRun is null) return;
                InvokeSafe(toRun);
            });
            if (!enqueued)
            {
                _armed = false;
                CrashLog.Write("EditorSpellDebouncer: DispatcherQueue.TryEnqueue returned false (immediate)");
            }
            return;
        }

        try
        {
            _timer.Interval = TimeSpan.FromMilliseconds(ms);
            _timer.Start();
        }
        catch (Exception ex)
        {
            CrashLog.Write("EditorSpellDebouncer: failed to start timer:");
            CrashLog.Write(ex);
            // Fall back to immediate enqueue so GLUE still gets a check rather than silence.
            bool enqueued = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (_disposed || gen != _generation) return;
                _armed = false;
                InvokeSafe(action);
            });
            if (!enqueued)
            {
                _armed = false;
                CrashLog.Write("EditorSpellDebouncer: fallback TryEnqueue returned false");
            }
        }
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            sender.Stop();
        }
        catch
        {
            /* ignore */
        }

        if (_disposed) return;

        _armed = false;
        var action = _pendingAction;
        // Keep last action stashed for Ping(); not armed until next Schedule/Ping.
        if (action is null) return;

        InvokeSafe(action);
    }

    private void CancelCore(bool clearPendingAction)
    {
        _generation++;
        _armed = false;
        try
        {
            _timer.Stop();
        }
        catch
        {
            /* ignore */
        }

        if (clearPendingAction)
            _pendingAction = null;
    }

    private static void InvokeSafe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // As-you-type must never take down the window; log and continue.
            CrashLog.Write("EditorSpellDebouncer: onFire threw:");
            CrashLog.Write(ex);
        }
    }

    private static int ClampDebounce(int ms)
    {
        if (ms < 0) return 0;
        if (ms > 5000) return 5000;
        return ms;
    }
}
