using System.Runtime.InteropServices;

namespace Launcher.Core.Platform;

/// <summary>
/// 任务栏贴边的方向。值与 Win32 APPBARDATA.uEdge 保持一致。
/// </summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}