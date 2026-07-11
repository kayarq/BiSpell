using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace BiSpell;

/// <summary>
/// Explicit entry point so we can surface startup failures (WinUI often dies silently).
/// DISABLE_XAML_GENERATED_MAIN is set in the csproj.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            CrashLog.Write("Main enter cwd=" + Environment.CurrentDirectory
                + " base=" + AppContext.BaseDirectory);
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                try
                {
                    CrashLog.Write("Application.Start callback");
                    _ = new App();
                    CrashLog.Write("App constructed");
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    CrashLog.MessageBox("BiSpell failed to start (App)", ex.Message);
                    Environment.Exit(1);
                }
            });
            CrashLog.Write("Application.Start returned (message loop ended)");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            CrashLog.MessageBox("BiSpell failed to start", ex.Message);
            Environment.Exit(1);
        }
    }
}
