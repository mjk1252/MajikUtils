using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dock.Core.ViewModels;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

public partial class DockWindow : Window
{
    private readonly DockViewModel _viewModel;
    private readonly Rectangle _workArea;

    public DockWindow(DockViewModel viewModel, Rectangle workArea)
    {
        _viewModel = viewModel;
        _workArea = workArea;
        DataContext = viewModel;

        // Off-screen until we can precisely position the HWND in physical pixels (avoids a visible jump).
        Left = -10000;
        Top = -10000;

        InitializeComponent();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyler.MakeNonActivatingToolWindow(hwnd);
        WindowStyler.ApplyAcrylicBackdrop(hwnd);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;

        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPillRegionAndPosition();
        SizeChanged += (_, _) => ApplyPillRegionAndPosition();
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

        WindowStyler.ApplyPillRegion(hwnd, widthPx, heightPx);

        var marginPx = (int)(12 * dpiScale);
        var x = _workArea.Left + (_workArea.Width - widthPx) / 2;
        var y = _workArea.Bottom - heightPx - marginPx;
        WindowStyler.SetWindowPosition(hwnd, x, y);
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

    private static void AnimateBounce(FrameworkElement element)
    {
        if (element.RenderTransform is not ScaleTransform)
            return;

        var animation = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.ScaleY"));
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
