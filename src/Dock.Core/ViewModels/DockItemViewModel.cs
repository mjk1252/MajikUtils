using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class DockItemViewModel : ObservableObject
{
    private readonly IAppLauncher _launcher;
    private IWindowActivator? _activator;
    private List<RunningWindow> _windows = [];

    public PinnedApp? App { get; }
    public string ExecutablePath { get; }
    public bool IsPinned => App is not null;
    public IReadOnlyList<RunningWindow> Windows => _windows;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private byte[]? iconPng;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private int windowCount;

    public DockItemViewModel(PinnedApp app, IAppLauncher launcher)
    {
        App = app;
        ExecutablePath = app.ExecutablePath;
        name = app.Name;
        _launcher = launcher;
    }

    public DockItemViewModel(string executablePath, string displayName, IAppLauncher launcher)
    {
        App = null;
        ExecutablePath = executablePath;
        name = displayName;
        _launcher = launcher;
    }

    internal void SetRunningState(List<RunningWindow> windows, IWindowActivator activator)
    {
        _activator = activator;
        _windows = windows;
        IsRunning = windows.Count > 0;
        WindowCount = windows.Count;
    }

    [RelayCommand]
    private void Launch()
    {
        if (IsRunning && _windows.Count == 1)
        {
            _activator?.ToggleActivate(_windows[0].Handle);
        }
        else if (!IsRunning && App is not null)
        {
            _launcher.Launch(App.ExecutablePath, App.Arguments);
        }
    }

    public void ActivateWindow(IntPtr handle) => _activator?.Activate(handle);

    [RelayCommand]
    private void EndTask()
    {
        if (_activator is null || _windows.Count == 0)
            return;

        var handles = _windows.Select(w => w.Handle).ToList();
        var processIds = _windows.Select(w => w.ProcessId).Distinct().ToList();
        _activator.EndTask(handles, processIds);
    }
}
