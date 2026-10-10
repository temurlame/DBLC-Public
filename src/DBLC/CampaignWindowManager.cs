using System.Windows;

namespace DBLC;

public sealed class CampaignWindowManager
{
    private CampaignWindow? _window;
    private Window? _owner;
    private IServiceProvider? _services;
    private bool _shuttingDown;
    private bool _hiddenWithOwner;

    public void Initialize(Window owner, IServiceProvider services)
    {
        _owner = owner;
        _services = services;
    }

    public void OpenOrActivate()
    {
        if (_owner is null || _services is null || _shuttingDown || !_owner.IsVisible) return;
        if (_window is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        _window = new CampaignWindow(_services, _owner);
        _window.Closed += (_, _) => { _window = null; _hiddenWithOwner = false; };
        _window.Show();
    }

    public void HideWithOwner()
    {
        if (_window?.IsVisible == true) { _hiddenWithOwner = true; _window.Hide(); }
    }

    public void RestoreWithOwner()
    {
        if (_hiddenWithOwner && _window is not null) _window.Show();
        _hiddenWithOwner = false;
    }

    public void CloseAll()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _window?.Close();
        _hiddenWithOwner = false;
    }
}
