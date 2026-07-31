using Dock.Interop.Native;

namespace Dock.Interop.Windowing;

public static class CursorInfo
{
    public static (int X, int Y) GetPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return (point.X, point.Y);
    }
}
