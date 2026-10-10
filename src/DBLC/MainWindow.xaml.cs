using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;

namespace DBLC;

public partial class MainWindow : Window
{
    private readonly ServiceProvider _provider;
    private readonly MobileWebHostManager _mobileHost;
    private readonly PaperWindowManager _paperWindows;
    private readonly CampaignWindowManager _campaignWindows;
    private readonly PrivacyLockStore _privacy;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly System.Windows.Forms.ContextMenuStrip _trayMenu;
    private readonly System.Drawing.Icon _trayDrawing;
    private bool _shutdownStarted;
    private bool _allowClose;
    private bool _explicitExit;
    private bool _systemEnding;
    private bool _unlockPromptOpen;
    private bool _servicesDisposed;

    public MainWindow(PrivacyLockStore privacy)
    {
        _privacy = privacy;
        InitializeComponent();
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddSingleton(privacy);
        services.AddSingleton<AppState>();
        services.AddSingleton<ConversationStore>();
        services.AddSingleton<ChronicleArchiveStore>();
        services.AddSingleton<ChronicleImageAssetStore>();
        services.AddSingleton<CampaignAgendaAuditStore>();
        services.AddSingleton<WorkspaceNotesStore>();
        services.AddSingleton<PlayerSheetStore>();
        services.AddSingleton<NpcPortraitStore>();
        services.AddSingleton<CampaignMapAssetStore>();
        services.AddSingleton<PlayerImageAssetStore>();
        services.AddSingleton<PaperDocumentState>();
        services.AddSingleton<PaperWindowManager>();
        services.AddSingleton<CampaignWindowManager>();
        services.AddSingleton<GmActivityCatalog>();
        services.AddSingleton<ViewOrderStore>();
        services.AddSingleton<LlamaModelInfoClient>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<LlamaClient>();
        services.AddSingleton<MobileWebHostManager>();
        _provider = services.BuildServiceProvider();
        BlazorHost.Services = _provider;
        _paperWindows = _provider.GetRequiredService<PaperWindowManager>();
        _campaignWindows = _provider.GetRequiredService<CampaignWindowManager>();
        _paperWindows.Initialize(this, _provider);
        _campaignWindows.Initialize(this, _provider);
        _mobileHost = _provider.GetRequiredService<MobileWebHostManager>();
        _mobileHost.Initialize(_provider, Dispatcher);

        _trayDrawing = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _trayMenu = new System.Windows.Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Open DBLC", null, (_, _) => Dispatcher.BeginInvoke(OpenFromTray));
        _trayMenu.Items.Add("Exit DBLC", null, (_, _) => Dispatcher.BeginInvoke(RequestRealExit));
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _trayDrawing, Text = "DBLC", ContextMenuStrip = _trayMenu, Visible = privacy.CloseToTrayEnabled };
        _privacy.Changed += OnPrivacyChanged;
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenFromTray);
        Closing += OnClosing;
        BlazorHost.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(AppShell) });
    }

    public void Reveal()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _paperWindows.RestoreWithOwner();
        _campaignWindows.RestoreWithOwner();
        Activate();
    }

    private void OpenFromTray()
    {
        if (_shutdownStarted || _unlockPromptOpen) return;
        if (IsVisible) { Activate(); return; }
        if (_privacy.HasPin)
        {
            _unlockPromptOpen = true;
            try { if (!PinPromptWindow.Unlock(_privacy)) return; }
            finally { _unlockPromptOpen = false; }
        }
        Reveal();
    }

    public void RequestRealExit()
    {
        if (_shutdownStarted) return;
        _explicitExit = true;
        Close();
    }

    public void MarkSystemEnding() => _systemEnding = true;

    public async Task StopBeforeFirstShowAsync()
    {
        await _mobileHost.StopAsync();
        _privacy.Changed -= OnPrivacyChanged;
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _trayDrawing.Dispose();
        _allowClose = true;
        Close();
        await DisposeServicesAsync();
    }

    private void OnPrivacyChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnPrivacyChanged); return; }
        _tray.Visible = _privacy.CloseToTrayEnabled || !IsVisible;
    }

    private void HideToTray()
    {
        _paperWindows.HideWithOwner();
        _campaignWindows.HideWithOwner();
        ShowInTaskbar = false;
        Hide();
        _tray.Visible = true;
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_shutdownStarted) return;
        if (DesktopClosePolicy.Decide(_privacy.CloseToTrayEnabled, _explicitExit, _systemEnding) == DesktopCloseAction.Hide)
        {
            HideToTray();
            return;
        }

        _shutdownStarted = true;
        var closeAfterShutdown = false;
        var papersSaved = false;
        try
        {
            papersSaved = _provider.GetRequiredService<PaperDocumentState>().SaveAll();
            if (!papersSaved)
            {
                ShowSaveFailure("The Player Sheet or Workspace still has unsaved changes. Restore file access and retry closing DBLC.");
                return;
            }
            await _mobileHost.StopAsync();
            closeAfterShutdown = true;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("DBLC shutdown did not stop cleanly: {0}", error);
            closeAfterShutdown = papersSaved;
            if (!papersSaved) ShowSaveFailure("DBLC could not confirm that the Player Sheet and Workspace were saved. The window will remain open so you can retry.");
        }
        finally
        {
            if (closeAfterShutdown)
            {
                _paperWindows.CloseAll();
                _campaignWindows.CloseAll();
                _tray.Visible = false;
                _privacy.Changed -= OnPrivacyChanged;
                _tray.Dispose();
                _trayMenu.Dispose();
                _trayDrawing.Dispose();
                // Closing is asynchronous here: let the original WPF Closing
                // event unwind before asking the window to close for real.
                _ = Dispatcher.BeginInvoke(new Action(CompleteShutdownAsync));
            }
            else
            {
                _shutdownStarted = false;
                _explicitExit = false;
            }
        }
    }

    private void ShowSaveFailure(string message)
    {
        // A tray exit must not reveal a hidden campaign through an owned dialog.
        if (IsVisible) System.Windows.MessageBox.Show(this, message, "DBLC — unsaved papers", MessageBoxButton.OK, MessageBoxImage.Warning);
        else System.Windows.MessageBox.Show(message, "DBLC — unsaved papers", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task DisposeServicesAsync()
    {
        if (_servicesDisposed) return;
        await _provider.DisposeAsync();
        _servicesDisposed = true;
    }

    private async void CompleteShutdownAsync()
    {
        _allowClose = true;
        Close();
        try { await DisposeServicesAsync(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("DBLC service cleanup did not finish cleanly: {0}", error); }
        System.Windows.Application.Current.Shutdown();
    }
}
