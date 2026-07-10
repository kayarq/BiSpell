using Microsoft.UI.Xaml;

namespace BiSpell;

/// <summary>WinUI 3 application entry — thin shell over bispell_core (P/Invoke).</summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
