using System.Windows;

namespace DBLC;

public sealed class PaperWindowManager
{
    private readonly Dictionary<string, PaperPopoutWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private Window? _owner;
    private IServiceProvider? _services;
    private bool _shuttingDown;
    private readonly HashSet<string> _hiddenWithOwner = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? Docked;

    public bool IsOpen(string viewName) => _windows.ContainsKey(viewName);

    public void Initialize(Window owner, IServiceProvider services)
    {
        _owner = owner;
        _services = services;
    }

    public void OpenOrActivate(string viewName)
    {
        if (_owner is null || _services is null || _shuttingDown || !_owner.IsVisible) return;
        if (_windows.TryGetValue(viewName, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = new PaperPopoutWindow(viewName, _services, _owner);
        _windows[viewName] = window;
        window.Closed += (_, _) =>
        {
            _windows.Remove(viewName);
            _hiddenWithOwner.Remove(viewName);
            if (!_shuttingDown) Docked?.Invoke(viewName);
        };
        window.Show();
    }

    public void HideWithOwner()
    {
        foreach (var (name, window) in _windows)
            if (window.IsVisible) { _hiddenWithOwner.Add(name); window.Hide(); }
    }

    public void RestoreWithOwner()
    {
        foreach (var name in _hiddenWithOwner.ToArray())
            if (_windows.TryGetValue(name, out var window)) window.Show();
        _hiddenWithOwner.Clear();
    }

    public void CloseAll()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        foreach (var window in _windows.Values.ToList()) window.Close();
        _windows.Clear();
        _hiddenWithOwner.Clear();
    }
}
