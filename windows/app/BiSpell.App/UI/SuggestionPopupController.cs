using System.Collections.ObjectModel;
using BiSpell.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace BiSpell.UI;

/// <summary>
/// Frozen GLUE-facing contract for the as-you-type suggestion popup (P4-POPUP).
/// Dual review picks a concrete implementation; method shapes stay stable.
/// </summary>
public interface ISuggestionPopup
{
    /// <summary>True while the popup is visible.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Show or refresh suggestions for <paramref name="miss"/>.
    /// Empty / null suggestions → <see cref="Hide"/> (no empty shell).
    /// Re-Show while open updates content in place (no stacked popups).
    /// </summary>
    void Show(MisspellingItem miss, IReadOnlyList<string> suggestions);

    /// <summary>Dismiss without applying. Idempotent; safe if never shown.</summary>
    void Hide();

    /// <summary>Raised after the user chooses a suggestion (click, Enter, or digit). Popup is already hidden.</summary>
    event EventHandler<string>? SuggestionChosen;

    /// <summary>Raised when the popup closes without a choice (Esc, light-dismiss, Hide).</summary>
    event EventHandler? Dismissed;
}

/// <summary>
/// <b>P4-POPUP Mandate B</b> — WinUI <see cref="Popup"/> with explicit offsets near a
/// best-effort caret / misspelling estimate (not pixel-perfect Mac overlay).
/// <para>
/// <b>Frozen host names (for dual freeze / GLUE):</b>
/// </para>
/// <list type="bullet">
/// <item><description><c>SuggestionPopupHost</c> — optional XAML overlay root; not required
/// (this controller builds its own <see cref="Popup"/> in code). GLUE may pass any
/// <see cref="FrameworkElement"/> placement target (prefer <c>EditorBox</c>).</description></item>
/// <item><description><c>SuggestionFlyout</c> — reserved for Mandate A (Flyout); unused here.</description></item>
/// </list>
/// <para>
/// <b>Keyboard contracts (when <see cref="IsOpen"/>):</b> Enter = selected or index 0;
/// keys 1–5 (and Numpad1–5) = that index if in range; Esc = dismiss without apply.
/// GLUE should call <see cref="TryHandleKey"/> from a root/preview handler with
/// <c>Handled=true</c> so digits do not insert into the multiline editor.
/// </para>
/// <para>
/// <b>Smoke:</b> <see cref="Show"/> no-ops (and hides) when <c>BISPELL_SMOKE=1</c>.
/// No engine, clipboard, or UIA coupling.
/// </para>
/// <para>
/// <b>Placement:</b> best-effort — estimates line/column from the misspelling UTF-16
/// location in a <see cref="TextBox"/>, offsets relative to the XamlRoot. Falls back
/// to the top-left of the placement target when transform fails.
/// </para>
/// <para>
/// <b>Empty suggestions policy:</b> <see cref="Show"/> calls <see cref="Hide"/> (no “No suggestions” shell).
/// GLUE normally only opens when count ≥ 1.
/// </para>
/// <para>
/// <b>Sample GLUE usage (do not wire here — P4-GLUE owns MainWindow):</b>
/// </para>
/// <code>
/// // ctor / init after InitializeComponent:
/// _suggestionPopup = new SuggestionPopupController(EditorBox);
/// _suggestionPopup.SuggestionChosen += (_, s) => ApplySuggestionFromPopup(s);
/// _suggestionPopup.Dismissed += (_, _) => { /* optional status */ };
///
/// // after as-you-type check selects nearest miss:
/// if (nearest is not null &amp;&amp; suggestions.Count &gt; 0)
///     _suggestionPopup.Show(nearest, suggestions);
/// else
///     _suggestionPopup.Hide();
///
/// // RootGrid_KeyDown / preview — before generic Enter-apply:
/// if (_suggestionPopup.TryHandleKey(e.Key)) { e.Handled = true; return; }
///
/// // Closed:
/// _suggestionPopup.Dispose();
/// </code>
/// </summary>
public sealed class SuggestionPopupController : ISuggestionPopup, IDisposable
{
    /// <summary>Digit keys apply at most this many leading suggestions.</summary>
    public const int MaxDigitChoices = 5;

    private const double PopupMinWidth = 220;
    private const double PopupMaxWidth = 320;
    private const double ListMaxHeight = 220;

    private readonly FrameworkElement _placementTarget;
    private readonly Popup _popup;
    private readonly Border _chrome;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _hintBlock;
    private readonly ListView _listView;
    private readonly ObservableCollection<string> _displayRows = new();

    private IReadOnlyList<string> _suggestions = Array.Empty<string>();
    private MisspellingItem? _currentMiss;
    /// <summary>When true, next Closed must not raise <see cref="Dismissed"/> (choice or dispose).</summary>
    private bool _suppressDismissed;
    private bool _choiceInFlight;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<string>? SuggestionChosen;

    /// <inheritdoc />
    public event EventHandler? Dismissed;

    /// <inheritdoc />
    public bool IsOpen => !_disposed && _popup.IsOpen;

    /// <summary>Misspelling currently shown, or null when closed / after hide.</summary>
    public MisspellingItem? CurrentMisspelling => _currentMiss;

    /// <summary>
    /// Create a popup controller anchored near <paramref name="placementTarget"/>
    /// (typically the editor <c>TextBox</c> named <c>EditorBox</c>).
    /// </summary>
    public SuggestionPopupController(FrameworkElement placementTarget)
    {
        _placementTarget = placementTarget ?? throw new ArgumentNullException(nameof(placementTarget));

        _titleBlock = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };

        _hintBlock = new TextBlock
        {
            Opacity = 0.65,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Text = "Enter apply · 1–5 choose · Esc dismiss",
        };

        _listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxHeight = ListMaxHeight,
            MinWidth = PopupMinWidth - 24,
            // Keep keyboard focus in the editor while the popup is open (keys via TryHandleKey).
            IsTabStop = false,
            AllowFocusOnInteraction = false,
            AllowFocusWhenDisabled = false,
        };
        _listView.ItemsSource = _displayRows;
        _listView.ItemClick += ListView_ItemClick;
        _listView.KeyDown += Content_KeyDown;

        var stack = new StackPanel
        {
            Spacing = 2,
            Children = { _titleBlock, _hintBlock, _listView },
        };

        _chrome = new Border
        {
            Child = stack,
            Padding = new Thickness(12, 10, 12, 10),
            MinWidth = PopupMinWidth,
            MaxWidth = PopupMaxWidth,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = ResolveBrush("CardBackgroundFillColorDefaultBrush", fallbackLight: true),
            BorderBrush = ResolveBrush("CardStrokeColorDefaultBrush", fallbackLight: false),
            IsTabStop = false,
            AllowFocusOnInteraction = false,
        };
        _chrome.KeyDown += Content_KeyDown;

        _popup = new Popup
        {
            Child = _chrome,
            IsLightDismissEnabled = true,
            // HorizontalOffset / VerticalOffset are relative to XamlRoot (set on each Show).
            ShouldConstrainToRootBounds = true,
            // Do not steal keyboard focus from the editor while typing.
            AllowFocusOnInteraction = false,
        };
        _popup.Closed += Popup_Closed;
    }

    /// <inheritdoc />
    public void Show(MisspellingItem miss, IReadOnlyList<string> suggestions)
    {
        if (_disposed) return;

        // Smoke: never open suggestion UI in headless CI (GLUE should also skip).
        if (CrashLog.IsSmokeMode)
        {
            Hide();
            return;
        }

        if (miss is null)
        {
            Hide();
            return;
        }

        var cleaned = NormalizeSuggestions(suggestions);
        // Contract: empty suggestions → Hide (prefer Hide over a “No suggestions” shell).
        if (cleaned.Count == 0)
        {
            Hide();
            return;
        }

        _currentMiss = miss;
        _suggestions = cleaned;
        RebuildRows(miss, cleaned);
        EnsureXamlRoot();
        UpdatePosition(miss);

        if (_popup.IsOpen)
        {
            // Re-Show: content already replaced on the single popup instance.
            return;
        }

        try
        {
            _popup.IsOpen = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("SuggestionPopupController.Show failed:");
            CrashLog.Write(ex);
            try { _popup.IsOpen = false; } catch { /* ignore */ }
        }
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_disposed) return;

        if (!_popup.IsOpen)
        {
            _currentMiss = null;
            _suggestions = Array.Empty<string>();
            return;
        }

        try
        {
            _popup.IsOpen = false;
        }
        catch
        {
            /* ignore */
        }

        _currentMiss = null;
        _suggestions = Array.Empty<string>();
    }

    /// <summary>
    /// Handle Enter / digit 1–5 / Esc while open. Returns true when the key was consumed
    /// (GLUE must set <c>e.Handled = true</c>). Safe when closed (returns false).
    /// </summary>
    public bool TryHandleKey(VirtualKey key)
    {
        if (_disposed || !_popup.IsOpen) return false;

        if (key == VirtualKey.Escape)
        {
            Hide();
            return true;
        }

        if (key == VirtualKey.Enter)
        {
            ApplySelectedOrTop();
            return true;
        }

        int digit = DigitIndex(key);
        if (digit >= 0)
        {
            if (digit < _suggestions.Count && digit < MaxDigitChoices)
                Choose(_suggestions[digit]);
            // Swallow digits while open so they do not insert into the editor.
            return true;
        }

        return false;
    }

    /// <summary>Release popup resources. Idempotent; safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _suppressDismissed = true;
            if (_popup.IsOpen)
                _popup.IsOpen = false;
        }
        catch { /* ignore */ }

        try
        {
            _popup.Closed -= Popup_Closed;
            _listView.ItemClick -= ListView_ItemClick;
            _listView.KeyDown -= Content_KeyDown;
            _chrome.KeyDown -= Content_KeyDown;
        }
        catch { /* ignore */ }

        _currentMiss = null;
        _suggestions = Array.Empty<string>();
        _displayRows.Clear();
    }

    // ── internals ──────────────────────────────────────────────────────────

    private static List<string> NormalizeSuggestions(IReadOnlyList<string>? suggestions)
    {
        if (suggestions is null || suggestions.Count == 0)
            return new List<string>();

        var list = new List<string>(Math.Min(suggestions.Count, 20));
        foreach (var s in suggestions)
        {
            if (string.IsNullOrEmpty(s)) continue;
            list.Add(s);
            if (list.Count >= 20) break;
        }

        return list;
    }

    private void RebuildRows(MisspellingItem miss, IReadOnlyList<string> cleaned)
    {
        _titleBlock.Text = string.IsNullOrEmpty(miss.LanguageLabel)
            ? miss.Word
            : $"{miss.Word}  [{miss.LanguageLabel}]";

        _displayRows.Clear();
        for (int i = 0; i < cleaned.Count; i++)
        {
            // Mac-like numbered rows for the first MaxDigitChoices entries.
            string prefix = i < MaxDigitChoices ? $"{i + 1}. " : "   ";
            _displayRows.Add(prefix + cleaned[i]);
        }

        if (_displayRows.Count > 0)
            _listView.SelectedIndex = 0;
    }

    private void EnsureXamlRoot()
    {
        var root = _placementTarget.XamlRoot;
        if (root is not null)
            _popup.XamlRoot = root;
    }

    /// <summary>
    /// Best-effort caret / word placement: line+column from UTF-16 location in a TextBox,
    /// else top-left of the placement target. Documented non-goal: Mac overlay parity.
    /// </summary>
    private void UpdatePosition(MisspellingItem miss)
    {
        EnsureXamlRoot();
        if (_popup.XamlRoot is null) return;

        Point origin;
        try
        {
            UIElement? rootVisual = _popup.XamlRoot.Content as UIElement;
            if (rootVisual is not null)
            {
                var t = _placementTarget.TransformToVisual(rootVisual);
                origin = t.TransformPoint(new Point(0, 0));
            }
            else
            {
                origin = new Point(0, 0);
            }
        }
        catch
        {
            origin = new Point(0, 0);
        }

        double lineHeight = 22;
        double charWidth = 8;
        string text = string.Empty;
        if (_placementTarget is TextBox tb)
        {
            lineHeight = Math.Max(16, tb.FontSize * 1.45);
            charWidth = Math.Max(6, tb.FontSize * 0.55);
            text = tb.Text ?? string.Empty;
        }

        int loc = (int)miss.Utf16Location;
        if (loc < 0) loc = 0;
        if (loc > text.Length) loc = text.Length;

        int line = 0;
        int col = 0;
        for (int i = 0; i < loc; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }

        const double pad = 8;
        double estimatedWidth = PopupMinWidth;
        double estimatedHeight = 48 + Math.Min(_displayRows.Count, 6) * 32;

        double x = origin.X + pad + col * charWidth;
        double y = origin.Y + pad + (line + 1) * lineHeight; // prefer below the line

        double targetRight = origin.X + Math.Max(0, _placementTarget.ActualWidth);
        double targetBottom = origin.Y + Math.Max(0, _placementTarget.ActualHeight);

        if (x + estimatedWidth > targetRight)
            x = Math.Max(origin.X, targetRight - estimatedWidth);

        if (y + estimatedHeight > targetBottom + 40)
        {
            // Flip above the estimated line when near the bottom of the editor.
            y = origin.Y + pad + line * lineHeight - estimatedHeight;
            if (y < origin.Y)
                y = origin.Y + pad;
        }

        double rootW = _popup.XamlRoot.Size.Width;
        double rootH = _popup.XamlRoot.Size.Height;
        if (rootW > 0 && x + estimatedWidth > rootW - 4)
            x = Math.Max(4, rootW - estimatedWidth - 4);
        if (rootH > 0 && y + estimatedHeight > rootH - 4)
            y = Math.Max(4, rootH - estimatedHeight - 4);
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
    }

    private void ListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string) return;

        int idx = _listView.Items.IndexOf(e.ClickedItem);
        if (idx < 0)
        {
            // Fallback: match display string.
            for (int i = 0; i < _displayRows.Count; i++)
            {
                if (ReferenceEquals(_displayRows[i], e.ClickedItem)
                    || string.Equals(_displayRows[i], e.ClickedItem as string, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx >= 0 && idx < _suggestions.Count)
            Choose(_suggestions[idx]);
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryHandleKey(e.Key))
            e.Handled = true;
    }

    private void ApplySelectedOrTop()
    {
        if (_suggestions.Count == 0) return;

        int idx = _listView.SelectedIndex;
        if (idx < 0 || idx >= _suggestions.Count)
            idx = 0;

        Choose(_suggestions[idx]);
    }

    private void Choose(string suggestion)
    {
        if (_choiceInFlight || string.IsNullOrEmpty(suggestion)) return;
        _choiceInFlight = true;
        try
        {
            // Leave _suppressDismissed true across Closed (may be async) so choice
            // does not also raise Dismissed. Cleared in Popup_Closed.
            _suppressDismissed = true;
            try
            {
                if (_popup.IsOpen)
                    _popup.IsOpen = false;
                else
                    _suppressDismissed = false; // nothing will close
            }
            catch
            {
                _suppressDismissed = false;
            }

            _currentMiss = null;
            _suggestions = Array.Empty<string>();
            _displayRows.Clear();

            try
            {
                SuggestionChosen?.Invoke(this, suggestion);
            }
            catch (Exception ex)
            {
                CrashLog.Write("SuggestionPopupController.SuggestionChosen handler failed:");
                CrashLog.Write(ex);
            }
        }
        finally
        {
            _choiceInFlight = false;
        }
    }

    private void Popup_Closed(object? sender, object e)
    {
        _currentMiss = null;
        _suggestions = Array.Empty<string>();

        bool suppress = _suppressDismissed || _disposed;
        _suppressDismissed = false;
        if (suppress) return;

        try
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            CrashLog.Write("SuggestionPopupController.Dismissed handler failed:");
            CrashLog.Write(ex);
        }
    }

    /// <summary>Map Number1–5 / NumberPad1–5 → 0-based index; else -1.</summary>
    private static int DigitIndex(VirtualKey key) => key switch
    {
        VirtualKey.Number1 or VirtualKey.NumberPad1 => 0,
        VirtualKey.Number2 or VirtualKey.NumberPad2 => 1,
        VirtualKey.Number3 or VirtualKey.NumberPad3 => 2,
        VirtualKey.Number4 or VirtualKey.NumberPad4 => 3,
        VirtualKey.Number5 or VirtualKey.NumberPad5 => 4,
        _ => -1,
    };

    private static Brush ResolveBrush(string resourceKey, bool fallbackLight)
    {
        try
        {
            if (Application.Current?.Resources is { } res
                && res.TryGetValue(resourceKey, out object value)
                && value is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            /* fall through */
        }

        // Solid fallback when theme resources are unavailable (unit / early init).
        byte gray = fallbackLight ? (byte)250 : (byte)180;
        return new SolidColorBrush(Color.FromArgb(255, gray, gray, gray));
    }
}
