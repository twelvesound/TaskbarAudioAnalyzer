namespace TaskbarAudioAnalyzer;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? instanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Setup invokes this as the original (non-elevated) user, without a window.
        if (e.Args.Contains("--configure-install", StringComparer.Ordinal))
        {
            try
            {
                StartupRegistration.RefreshIfEnabled();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }
            return;
        }

        instanceMutex = new System.Threading.Mutex(true, @"Local\12sound.TaskbarAudioAnalyzer", out var createdNew);
        if (!createdNew)
        {
            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
