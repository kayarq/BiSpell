using Microsoft.UI.Xaml;

namespace BiSpell;

/// <summary>
/// Explicit entry point so we can surface startup failures (WinUI often dies silently).
/// DISABLE_XAML_GENERATED_MAIN is set in the csproj.
///
/// Exit codes (Mandate B / W2):
/// <list type="bullet">
/// <item><description>0 — clean message-loop end or App.Quit()</description></item>
/// <item><description>1 — fatal startup failure (App ctor / MainWindow / outer Main)</description></item>
/// </list>
/// Session log truncated at entry; see CrashLog.BeginSession.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            // B4: truncate prior-run log so smoke cannot match stale fatal strings.
            CrashLog.BeginSession();
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
                    CrashLog.WriteFatal(ex, "App construction");
                    CrashLog.MessageBox("BiSpell failed to start (App)", ex.Message);
                    Environment.Exit(CrashLog.ExitStartupFailure);
                }
            });

            CrashLog.Write("Application.Start returned (message loop ended)");
            return CrashLog.ExitOk;
        }
        catch (Exception ex)
        {
            CrashLog.WriteFatal(ex, "Main");
            CrashLog.MessageBox("BiSpell failed to start", ex.Message);
            return CrashLog.ExitStartupFailure;
        }
    }
}
