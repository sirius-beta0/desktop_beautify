using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core.Favorites;

/// <summary>
/// 收藏（固定）列表的持久化存储。仅保存 <see cref="AppEntry"/> 的稳定 Id（路径 / AUMID 哈希），
/// 与位置无关；应用重命名 / 移动快捷方式不丢收藏。顺序即用户在面板中拖拽得到的顺序。
/// </summary>
public sealed class FavoriteStore
{
    private readonly string _path;
    private readonly List<string> _ids = new();

    public FavoriteStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopBeautify");
        try { Directory.CreateDirectory(directory); } catch { /* 忽略建目录失败 */ }
        _path = Path.Combine(directory, "pinned.json");
        Load();
    }

    /// <summary>当前收藏 Id 列表（按展示顺序排列）。</summary>
    public IReadOnlyList<string> Ids => _ids;

    public bool Contains(string id) => _ids.Contains(id);

    public int IndexOf(string id) => _ids.IndexOf(id);

    /// <summary>新收藏追加到末尾。</summary>
    public void Add(string id)
    {
        if (_ids.Contains(id)) return;
        _ids.Add(id);
        Save();
    }

    public void Remove(string id)
    {
        if (_ids.Remove(id)) Save();
    }

    /// <summary>用拖拽得到的新顺序整体替换收藏列表并落盘。</summary>
    public void Replace(IEnumerable<string> ids)
    {
        _ids.Clear();
        _ids.AddRange(ids);
        Save();
    }

    /// <summary>收藏区内拖拽重排：把 from 位置的项移动到 to 位置。</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= _ids.Count) return;
        if (to < 0 || to >= _ids.Count) return;
        if (from == to) return;
        var item = _ids[from];
        _ids.RemoveAt(from);
        _ids.Insert(to, item);
        Save();
    }

    public void Load()
    {
        _ids.Clear();
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var doc = JsonSerializer.Deserialize<PinnedDoc>(json);
            if (doc?.Ids is not null) _ids.AddRange(doc.Ids);
        }
        catch
        {
            // 文件损坏则用空列表兜底
        }
    }

    public void Save()
    {
        try
        {
            var doc = new PinnedDoc { Version = 1, Ids = _ids.ToArray() };
            var json = JsonSerializer.Serialize(doc,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 忽略写失败（下次变更再试）
        }
    }

    private sealed class PinnedDoc
    {
        public int Version { get; set; }
        public string[] Ids { get; set; } = System.Array.Empty<string>();
    }
}
