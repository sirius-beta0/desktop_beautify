using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Launcher.UI;

/// <summary>
/// 用 IShellItemImageFactory 异步提取应用高清图标。
/// 
/// 关键技术点：
/// 1. 必须使用 CreateBitmapSourceFromHBitmap 而非 Image.FromHbitmap。
///    Image.FromHbitmap 会把带 alpha 的 HBITMAP 降级为 Format32bppRgb，
///    透明区域被填成黑色，导致 WPF 里出现"方形黑底"。
/// 2. 请求尺寸取 96px（显示尺寸 48px × 2 倍 DPI 余量），并加 ScaleUp
///    让系统对低分辨率源做高质量放大，避免直接 256px 放大再缩小的锯齿。
/// 3. 针对极少数 alpha 全 0 的 HBITMAP 做保护：若整张图 alpha 都是 0，
///    则全部置 255，使其显示为不透明（露出图标本身而非黑底）。
/// </summary>
public static class IconExtractor
{
    // 96 = 界面显示 48 逻辑像素的 2 倍，覆盖 200% DPI；ScaleUp 会处理更小图标。
    private const int SourceSize = 96;
    private const int MaxCache = 200;
    private const int MaxConcurrent = 4;

    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new();
    private static readonly SemaphoreSlim Semaphore = new(MaxConcurrent, MaxConcurrent);

    /// <summary>
    /// 异步获取指定路径的图标，返回可在 WPF 线程安全使用的冻结 BitmapSource。
    /// </summary>
    public static async Task<BitmapSource?> GetAsync(string id, string path)
    {
        if (Cache.TryGetValue(id, out var cached))
            return cached;

        if (string.IsNullOrWhiteSpace(path))
        {
            Cache[id] = null;
            return null;
        }

        // UWP 图标路径是 shell: 命名空间（如 shell:appsFolder\<AUMID>），不走文件存在性检查
        if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
        {
            Cache[id] = null;
            return null;
        }

        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(id, out cached))
                return cached;

            var source = await Task.Run(() => Extract(path)).ConfigureAwait(false);
            if (source is null)
            {
                Cache[id] = null;
                return null;
            }

            source.Freeze();
            Cache[id] = source;
            TrimIfNeeded();
            return source;
        }
        catch
        {
            Cache[id] = null;
            return null;
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private static BitmapSource? Extract(string path)
    {
        try
        {
            using var item = ShellItem.Open(path);
            using var hbm = item.GetImage(
                new SIZE(SourceSize, SourceSize),
                ShellItemGetImageOptions.IconOnly | ShellItemGetImageOptions.ScaleUp);

            // 在 SafeHBITMAP 被释放前同步读取位图数据。
            return ConvertHBitmap(hbm.DangerousGetHandle());
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource ConvertHBitmap(IntPtr hbitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            hbitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        // 统一转到 BGRA32 以便逐像素修正 alpha。
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);

        bool allZero = true;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { allZero = false; break; }
        }
        if (allZero)
        {
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        // 经典小图标（老 .ico）常自带 1px 均匀灰框，放大后明显；剥掉外圈灰边。
        StripUniformGrayBorder(pixels, w, h, stride);

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// 剥掉经典小图标外圈的均匀灰框：逐圈检测最外环是否为「可见 + 低饱和度(灰) + 颜色均匀」，
    /// 满足则置透明，并继续向内，直到遇到非灰边内容。现代透明图标外环 alpha≈0 会直接停止，不受影响。
    /// </summary>
    private static void StripUniformGrayBorder(byte[] px, int w, int h, int stride)
    {
        const int MaxRings = 8;   // 最多剥几圈，避免误伤
        const int Tol = 30;       // 同圈颜色容差
        const int SatMax = 50;    // 灰边饱和度上限（区分灰框与彩色内容）
        const int AlphaMin = 40;  // 视为“可见边框”的最低 alpha

        for (int ring = 0; ring < MaxRings; ring++)
        {
            int x0 = ring, x1 = w - 1 - ring, y0 = ring, y1 = h - 1 - ring;
            if (x1 <= x0 || y1 <= y0) break;

            // 1) 统计本圈平均颜色
            long sr = 0, sg = 0, sb = 0, sa = 0; int n = 0;
            for (int x = x0; x <= x1; x++)
            {
                Accum(px, y0, x, stride, ref sr, ref sg, ref sb, ref sa, ref n);
                Accum(px, y1, x, stride, ref sr, ref sg, ref sb, ref sa, ref n);
            }
            for (int y = y0 + 1; y <= y1 - 1; y++)
            {
                Accum(px, y, x0, stride, ref sr, ref sg, ref sb, ref sa, ref n);
                Accum(px, y, x1, stride, ref sr, ref sg, ref sb, ref sa, ref n);
            }
            if (n == 0) break;

            int ar = (int)(sr / n), ag = (int)(sg / n), ab = (int)(sb / n), aa = (int)(sa / n);

            // 2) 可见？
            if (aa < AlphaMin) break;
            // 3) 灰边？（低饱和度）
            int maxc = Math.Max(ar, Math.Max(ag, ab));
            int minc = Math.Min(ar, Math.Min(ag, ab));
            if (maxc - minc > SatMax) break;

            // 4) 均匀？
            bool uniform = true;
            for (int x = x0; x <= x1 && uniform; x++)
            {
                if (!Near(px, y0, x, stride, ar, ag, ab, aa, Tol) ||
                    !Near(px, y1, x, stride, ar, ag, ab, aa, Tol)) uniform = false;
            }
            for (int y = y0 + 1; y <= y1 - 1 && uniform; y++)
            {
                if (!Near(px, y, x0, stride, ar, ag, ab, aa, Tol) ||
                    !Near(px, y, x1, stride, ar, ag, ab, aa, Tol)) uniform = false;
            }
            if (!uniform) break;

            // 5) 清除本圈（置透明）
            for (int x = x0; x <= x1; x++) { Clear(px, y0, x, stride); Clear(px, y1, x, stride); }
            for (int y = y0 + 1; y <= y1 - 1; y++) { Clear(px, y, x0, stride); Clear(px, y, x1, stride); }
        }
    }

    private static void Accum(byte[] px, int y, int x, int stride,
        ref long sr, ref long sg, ref long sb, ref long sa, ref int n)
    {
        int i = y * stride + x * 4;
        sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3]; n++;
    }

    private static bool Near(byte[] px, int y, int x, int stride,
        int ar, int ag, int ab, int aa, int tol)
    {
        int i = y * stride + x * 4;
        return Math.Abs(px[i] - ab) <= tol && Math.Abs(px[i + 1] - ag) <= tol &&
               Math.Abs(px[i + 2] - ar) <= tol && Math.Abs(px[i + 3] - aa) <= tol;
    }

    private static void Clear(byte[] px, int y, int x, int stride)
    {
        int i = y * stride + x * 4;
        px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0;
    }

    /// <summary>
    /// 简单清理：超出上限时随机移除约 10% 缓存条目。
    /// </summary>
    private static void TrimIfNeeded()
    {
        if (Cache.Count <= MaxCache) return;
        var victims = Cache.Keys.Take(Math.Max(1, Cache.Count / 10)).ToList();
        foreach (var key in victims)
            Cache.TryRemove(key, out _);
    }
}
