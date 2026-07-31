using System.Drawing;

namespace Dock.Interop.Windowing;

public sealed record MonitorSnapshot(Rectangle Bounds, Rectangle WorkArea, bool IsPrimary);
