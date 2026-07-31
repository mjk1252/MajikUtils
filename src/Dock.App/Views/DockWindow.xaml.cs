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

    public DockWindow(DockViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
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
        PositionAtBottomCenter();
        ApplyPillRegion();
        SizeChanged += (_, _) =>
        {
            PositionAtBottomCenter();
            ApplyPillRegion();
        };
    }

    private void PositionAtBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private void ApplyPillRegion()
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

        item.LaunchCommand.Execute(null);
        AnimateBounce(element);
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
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
