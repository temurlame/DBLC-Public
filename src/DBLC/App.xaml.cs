using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace DBLC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private DataRootWriterLock? _writerLock;
    private MainWindow? _desktop;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try { _writerLock = new DataRootWriterLock(AppDataPaths.Root); }
        catch (IOException)
        {
            System.Windows.MessageBox.Show("DBLC is already using this campaign data folder. Close the other DBLC window before starting another instance.", "DBLC is already running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        try
        {
            try { SkillLibrary.InitializeDefaults(); }
            catch (IOException error) { System.Diagnostics.Trace.TraceWarning("Default Agent Skills could not be initialized: " + error.Message); }
            catch (UnauthorizedAccessException error) { System.Diagnostics.Trace.TraceWarning("Default Agent Skills could not be initialized: " + error.Message); }
            var privacy = new PrivacyLockStore();
            if (e.Args.Contains("--reset-pin", StringComparer.OrdinalIgnoreCase))
            {
                if (System.Windows.MessageBox.Show("Reset the local DBLC privacy PIN? Campaigns and other settings will stay unchanged.",
                    "DBLC · Reset PIN", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    privacy.ResetPin();
                    System.Windows.MessageBox.Show("The local privacy PIN has been removed. Reopen DBLC normally.", "DBLC · PIN reset", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Shutdown();
                return;
            }
            _desktop = new MainWindow(privacy);
            MainWindow = _desktop;
            SessionEnding += (_, _) => _desktop?.MarkSystemEnding();
            if (!privacy.HasPin || PinPromptWindow.Unlock(privacy)) _desktop.Reveal();
            else if (!privacy.CloseToTrayEnabled)
            {
                await _desktop.StopBeforeFirstShowAsync();
                Shutdown();
                return;
            }
            // With tray mode enabled, cancellation leaves the shared host alive and the window hidden.
        }
        catch (Exception error)
        {
            _writerLock?.Dispose();
            _writerLock = null;
            System.Windows.MessageBox.Show("DBLC could not complete startup. " + error.Message,
                "DBLC · Startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _writerLock?.Dispose();
        base.OnExit(e);
    }
}

