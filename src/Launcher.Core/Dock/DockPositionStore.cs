using System.IO;
using System.Text.Json;

namespace Launcher.Core.Dock;

/// <summary>
/// Dock 栏位置的持久化（自由拖动后的记忆）。保存 WPF 逻辑坐标（1/96 英寸），
/// 加载时直接作为 Window.Left/Top 使用，跨 DPI 一致。
/// </summary>
public sealed class DockPositionStore
{
    private readonly string _path;

    public DockPositionStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopBeautify");
        try { Directory.CreateDirectory(directory); } catch { /* 忽略建目录失败 */ }
        _path = Path.Combine(directory, "dock.json");
        Load();
    }

    /// <summary>Dock 栏左上角的屏幕逻辑坐标（WPF 设备无关单位）。</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public void Save()
    {
        try
        {
            var doc = new Doc { Left = Left, Top = Top };
            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 忽略写失败（下次变更再试）
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var doc = JsonSerializer.Deserialize<Doc>(File.ReadAllText(_path));
            Left = doc?.Left;
            Top = doc?.Top;
        }
        catch
        {
            // 文件损坏则用空（使用默认位置）兜底
        }
    }

    private sealed class Doc
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
    }
}
