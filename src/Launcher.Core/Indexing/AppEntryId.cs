using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core.Indexing;

/// <summary>
/// 生成应用条目的稳定 Id。
/// Win32 用目标路径、UWP 用 AUMID；小写归一后取 SHA1 前 16 位。
/// </summary>
public static class AppEntryId
{
    public static string Compute(string key)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}