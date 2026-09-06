namespace Launcher.Core.Platform;

/// <summary>
/// 物理像素矩形。Core 层不依赖 WPF，所以用自己的结构。
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// 任务栏及所在显示器的相关信息。坐标均为物理像素。
/// </summary>
public sealed record TaskbarInfo(
    PixelRect Bounds,
    TaskbarEdge Edge,
    PixelRect MonitorBounds,
    PixelRect MonitorWorkArea,
    double ScaleFactor);