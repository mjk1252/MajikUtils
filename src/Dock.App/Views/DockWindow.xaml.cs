using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Dock.Core.Models;
using Dock.Core.ViewModels;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class DockWindow : Window
{
    private const int PanicHotkeyId = 1;

    private readonly DockViewModel _viewModel;
    private readonly MonitorSnapshot _monitor;
    private readonly DockPosition _position;
    private readonly bool _enableGlobalHooks;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private uint _taskbarCreatedMessage;
    private bool _appBarRegistered;

    public event Action? PanicHotkeyPressed;
    public event Action? ExplorerRestarted;

    public DockWindow(DockViewModel viewModel, MonitorSnapshot monitor, DockPosition position = DockPosition.Bottom, bool enableGlobalHooks = false)
    {
        _viewModel = viewModel;
        _monitor = monitor;
        _position = position;
        _enableGlobalHooks = enableGlobalHooks;
        DataContext = viewModel;

        // Land within this monitor's own bounds (converted using ITS OWN DPI, not the primary
        // monitor's) rather than some arbitrary off-screen point. WPF picks a window's initial
        // per-monitor DPI context from where it's created; landing outside every monitor risks
        // it defaulting to the primary monitor's scale, which is what made the dock render at
        // the wrong size on a secondary monitor with different DPI/resolution.
        Left = _monitor.Bounds.Left / _monitor.DpiScale;
        Top = _monitor.Bounds.Top / _monitor.DpiScale;

        InitializeComponent();
        ApplyOrientation();

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        Closed += (_, _) => _clockTimer.Stop();
    }

    private void ApplyOrientation()
    {
        if (_position == DockPosition.Bottom)
            return;

        ContentStack.Orientation = Orientation.Vertical;

        var verticalPanel = (ItemsPanelTemplate)FindResource("VerticalPanel");
        PinnedItemsControl.ItemsPanel = verticalPanel;

        foreach (var separator in new[] { Separator1, Separator2, Separator3 })
        {
            var width = separator.Width;
            separator.Width = double.NaN;
            separator.Height = width;
            separator.Margin = new Thickness(10, 6, 10, 6);
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTimeText.Text = now.ToString("h:mm tt");
        ClockDateText.Text = now.ToString("ddd, MMM d");
        FlyoutTimeText.Text = now.ToString("h:mm tt");
        FlyoutDateText.Text = now.ToString("dddd, MMMM d, yyyy");
    }

    private void OnClockClick(object sender, MouseButtonEventArgs e)
    {
        // Prefer relaying to the real taskbar clock, which opens Windows' actual Notification
        // Center (calendar + notifications) -- falls back to our own simple flyout if tray
        // reading hasn't found it (e.g. this build's automation path failed).
        if (_viewModel.ClockTrayIcon is { } clockIcon)
            clockIcon.ClickCommand.Execute(null);
        else
            ClockFlyout.IsOpen = !ClockFlyout.IsOpen;
    }

    private void OnOverflowChevronClick(object sender, MouseButtonEventArgs e)
    {
        // Prefer relaying to the real "Show Hidden Icons" chevron, which opens Windows' actual
        // overflow flyout -- falls back to our own icon-grid popup if it wasn't found.
        if (_viewModel.ChevronTrayIcon is { } chevronIcon)
            chevronIcon.ClickCommand.Execute(null);
        else
            OverflowFlyout.IsOpen = !OverflowFlyout.IsOpen;
    }

    private void OnGlassMouseMove(object sender, MouseEventArgs e)
    {
        if (ActualWidth <= 0)
            return;

        var position = e.GetPosition(GlassBorder);
        var ratio = Math.Clamp(position.X / ActualWidth, 0.05, 0.95);
        RimStopMid.Offset = ratio;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyler.MakeNonActivatingToolWindow(hwnd);
        WindowStyler.ApplyAcrylicBackdrop(hwnd);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        if (_enableGlobalHooks)
        {
            WindowStyler.RegisterPanicHotkey(hwnd, PanicHotkeyId);
            _taskbarCreatedMessage = WindowStyler.RegisterTaskbarCreatedMessage();
        }

        Closed += (_, _) =>
        {
            if (_enableGlobalHooks)
                WindowStyler.UnregisterHotkey(hwnd, PanicHotkeyId);

            if (_appBarRegistered)
                AppBarService.Unregister(hwnd);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;

        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        if (_enableGlobalHooks)
        {
            if (msg == WindowStyler.WM_HOTKEY && wParam.ToInt32() == PanicHotkeyId)
            {
                PanicHotkeyPressed?.Invoke();
            }
            else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            {
                ExplorerRestarted?.Invoke();
            }
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPillRegionAndPosition();
        SizeChanged += (_, _) => ApplyPillRegionAndPosition();

        // Popups have their own NameScope, so ElementName bindings inside one can't resolve a
        // sibling outside it -- assign the placement target directly instead.
        ClockFlyout.PlacementTarget = ClockWidget;
        OverflowFlyout.PlacementTarget = OverflowChevron;
    }

    private void ApplyPillRegionAndPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var widthPx = (int)(ActualWidth * dpiScale);
        var heightPx = (int)(ActualHeight * dpiScale);

        if (widthPx <= 0 || heightPx <= 0)
            return;

        var cornerRadiusPx = (int)(22 * dpiScale);
        WindowStyler.ApplyRoundedRegion(hwnd, widthPx, heightPx, cornerRadiusPx);

        var bounds = _monitor.Bounds;
        var marginPx = (int)(12 * dpiScale);
        int x, y;
        AppBarEdge edge;
        int thicknessPx;

        switch (_position)
        {
            case DockPosition.Left:
                x = bounds.Left + marginPx;
                y = bounds.Top + (bounds.Height - heightPx) / 2;
                edge = AppBarEdge.Left;
                thicknessPx = widthPx + marginPx * 2;
                break;
            case DockPosition.Right:
                x = bounds.Right - widthPx - marginPx;
                y = bounds.Top + (bounds.Height - heightPx) / 2;
                edge = AppBarEdge.Right;
                thicknessPx = widthPx + marginPx * 2;
                break;
            default:
                x = bounds.Left + (bounds.Width - widthPx) / 2;
                y = bounds.Bottom - heightPx - marginPx;
                edge = AppBarEdge.Bottom;
                thicknessPx = heightPx + marginPx * 2;
                break;
        }

        WindowStyler.SetWindowPosition(hwnd, x, y);

        if (!_appBarRegistered)
        {
            AppBarService.Register(hwnd);
            _appBarRegistered = true;
        }

        AppBarService.Reposition(hwnd, bounds, edge, thicknessPx);
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
                _viewModel.AddPinned(path);
        }
    }

    private void OnAddClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            Title = "Pin an application"
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.AddPinned(dialog.FileName);
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
            return;

        if (item.IsRunning && item.Windows.Count > 1)
        {
            ShowWindowChooser(item, element);
        }
        else
        {
            item.LaunchCommand.Execute(null);
            AnimateBounce(element);
        }
    }

    private void ShowWindowChooser(DockItemViewModel item, FrameworkElement anchor)
    {
        var menu = new ContextMenu();

        foreach (var window in item.Windows)
        {
            var handle = window.Handle;
            var menuItem = new MenuItem { Header = window.Title };
            menuItem.Click += (_, _) => item.ActivateWindow(handle);
            menu.Items.Add(menuItem);
        }

        anchor.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
            return;

        if (!item.IsPinned)
            return;

        var menu = new ContextMenu();
        var unpin = new MenuItem { Header = "Unpin" };
        unpin.Click += (_, _) => _viewModel.UnpinCommand.Execute(item);
        menu.Items.Add(unpin);

        element.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnTrayIconClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Core.ViewModels.TrayIconViewModel icon })
            icon.ClickCommand.Execute(null);
    }

    private void OnTrayIconRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Core.ViewModels.TrayIconViewModel icon })
            icon.RightClickCommand.Execute(null);
    }

    private static void AnimateBounce(FrameworkElement element)
    {
        if (element.RenderTransform is not TransformGroup { Children.Count: >= 2 })
            return;

        var keyFrames = new DoubleAnimationUsingKeyFrames();
        keyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        keyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)))
        {
            EasingFunction = new BounceEase { Bounces = 2, Bounciness = 2, EasingMode = EasingMode.EaseOut }
        });

        var storyboard = new Storyboard();
        Storyboard.SetTarget(keyFrames, element);
        Storyboard.SetTargetProperty(keyFrames, new PropertyPath("RenderTransform.Children[1].Y"));
        storyboard.Children.Add(keyFrames);
        storyboard.Begin();
    }
}
