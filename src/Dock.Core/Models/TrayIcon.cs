namespace Dock.Core.Models;

public sealed class TrayIcon
{
    public required string Name { get; init; }
    public byte[]? IconPng { get; init; }

    // Classic relay path (Explorer's ToolbarWindow32, pre-XAML tray). Absent on newer builds.
    public IntPtr? OwnerHandle { get; init; }
    public uint? IconId { get; init; }
    public uint? CallbackMessage { get; init; }

    // UI Automation relay path (XAML-hosted tray). Absent when the classic path worked.
    public int? ClickX { get; init; }
    public int? ClickY { get; init; }
}
