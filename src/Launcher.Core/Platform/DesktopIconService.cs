using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
// Vanara 的 Shell32 是「类」而非命名空间，COM 接口与枚举都嵌套在它内部，
// 故用别名引入；不写 using Vanara.PInvoke 以免 IServiceProvider 与 System 的同名类型冲突。
using IFolderView2 = Vanara.PInvoke.Shell32.IFolderView2;
using IShellWindows = Vanara.PInvoke.Shell32.IShellWindows;
using IShellBrowser = Vanara.PInvoke.Shell32.IShellBrowser;
using IShellView = Vanara.PInvoke.Shell32.IShellView;
using IServiceProvider = Vanara.PInvoke.Shell32.IServiceProvider;
using FOLDERFLAGS = Vanara.PInvoke.Shell32.FOLDERFLAGS;
using CSIDL = Vanara.PInvoke.Shell32.CSIDL;
using ShellWindowTypeConstants = Vanara.PInvoke.Shell32.ShellWindowTypeConstants;
using ShellWindowFindWindowOptions = Vanara.PInvoke.Shell32.ShellWindowFindWindowOptions;

namespace Launcher.Core.Platform;

/// <summary>
/// 桌面图标显隐控制（需求 F18）。
/// <para>
/// 走官方 Shell COM：<c>IFolderView2::SetCurrentFolderFlags(FWF_NOICONS, ...)</c>，
/// 这正是系统右键菜单「显示桌面图标」背后的同一个标志，因此两者状态天然一致、不会打架。
/// </para>
/// <para>
/// 刻意不走「隐藏 SysListView32 窗口」那条路：它依赖 Progman / WorkerW / SHELLDLL_DefView
/// 这套未公开窗口层级，Win11 22621+ raised desktop 与 24H2 结构变化、多虚拟桌面、
/// explorer 重启都会失效，属于项目边界外的对抗性做法。
/// </para>
/// </summary>
public static class DesktopIconService
{
    // ShellWindows coclass：IShellWindows 的宿主
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    // SID_STopLevelBrowser：从桌面窗口的 IServiceProvider 取 IShellBrowser
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>桌面图标当前是否处于隐藏态；取不到桌面视图时返回 null（无法判定）。</summary>
    public static bool? AreIconsHidden()
    {
        var view = GetDesktopFolderView();
        if (view is null) return null;
        try
        {
            return (view.GetCurrentFolderFlags() & FOLDERFLAGS.FWF_NOICONS) != 0;
        }
        catch (Exception ex)
        {
            LogError(nameof(AreIconsHidden), ex);
            return null;
        }
        finally
        {
            SafeRelease(view);
        }
    }

    /// <summary>
    /// 隐藏 / 恢复桌面全部图标（含回收站、此电脑、用户文件与快捷方式）。
    /// explorer 刚启动等场景下取视图可能失败，故内置重试。
    /// </summary>
    /// <returns>是否成功</returns>
    public static bool SetIconsHidden(bool hide, int maxAttempts = 3, int delayMs = 200)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (TrySetIconsHidden(hide)) return true;
            if (attempt < maxAttempts - 1) Thread.Sleep(delayMs);
        }
        return false;
    }

    /// <summary>
    /// 在 STA 线程上执行显隐（Shell COM 要求 STA），不阻塞 UI。
    /// 用于启动阶段与设置页即时切换。
    /// </summary>
    public static Task<bool> SetIconsHiddenAsync(bool hide, int maxAttempts = 3, int delayMs = 200)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(SetIconsHidden(hide, maxAttempts, delayMs)); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "DesktopIconToggle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static bool TrySetIconsHidden(bool hide)
    {
        var view = GetDesktopFolderView();
        if (view is null) return false;
        try
        {
            var flags = view.GetCurrentFolderFlags();
            var desired = hide
                ? flags | FOLDERFLAGS.FWF_NOICONS
                : flags & ~FOLDERFLAGS.FWF_NOICONS;
            // dwMask 只给 FWF_NOICONS：仅改这一位，其余视图标志原样保留
            view.SetCurrentFolderFlags(FOLDERFLAGS.FWF_NOICONS, desired);
            return true;
        }
        catch (Exception ex)
        {
            LogError(nameof(TrySetIconsHidden), ex);
            return false;
        }
        finally
        {
            SafeRelease(view);
        }
    }

    /// <summary>
    /// 取桌面文件夹视图 IFolderView2（Raymond Chen FindDesktopFolderView 的 C# 版）。
    /// ShellWindows → FindWindowSW(桌面) → IServiceProvider → IShellBrowser → IShellView → IFolderView2
    /// <para>
    /// 返回的 RCW 由调用者负责释放（<see cref="SafeRelease"/>）。
    /// </para>
    /// </summary>
    private static IFolderView2? GetDesktopFolderView()
    {
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (shellWindowsType is null) return null;
            if (Activator.CreateInstance(shellWindowsType) is not IShellWindows shellWindows) return null;

            IShellView? shellView = null;
            IShellBrowser? browser = null;
            try
            {
                // pvarLoc：VARIANT(VT_I4) = CSIDL_DESKTOP(0)；pvarLocRoot 未用到，传 0
                object loc = (int)CSIDL.CSIDL_DESKTOP;
                object unused = 0;
                var hr = shellWindows.FindWindowSW(
                    in loc, in unused,
                    ShellWindowTypeConstants.SWC_DESKTOP,
                    out _,
                    ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH,
                    out var dispatch);
                if (hr.Failed || dispatch is null) return null;

                if (dispatch is not IServiceProvider provider) return null;

                var serviceId = SID_STopLevelBrowser;
                var browserIid = typeof(IShellBrowser).GUID;
                hr = provider.QueryService(in serviceId, in browserIid, out var browserPtr);
                if (hr.Failed || browserPtr == IntPtr.Zero) return null;

                browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
                Marshal.Release(browserPtr);   // GetObjectForIUnknown 已 AddRef

                hr = browser.QueryActiveShellView(out shellView);
                if (hr.Failed || shellView is null) return null;

                // 关键坑：.NET 的 RCW 按「COM 对象标识」缓存，IShellView 与由它 QI 出来的
                // IFolderView2 是同一个 RCW 实例（ReferenceEquals == true）。因此这里
                // 绝不能 Marshal.ReleaseComObject(shellView) —— 一旦释放，返回的 IFolderView2
                // 立刻变成 "separated from its underlying RCW"，后续调用抛
                // InvalidComObjectException，表现为功能静默失效。
                // 故成功路径把所有权移交给调用者：对整个 RCW 释放一次即可。
                var folderView = shellView as IFolderView2;
                if (folderView is not null) shellView = null;   // 交出所有权，finally 不再释放
                return folderView;
            }
            finally
            {
                if (shellView is not null) SafeRelease(shellView);
                if (browser is not null) SafeRelease(browser);
                SafeRelease(shellWindows);
            }
        }
        catch (Exception ex)
        {
            LogError(nameof(GetDesktopFolderView), ex);
            return null;
        }
    }

    private static void SafeRelease(object comObject)
    {
        try { Marshal.ReleaseComObject(comObject); }
        catch { /* 释放失败无需处理 */ }
    }

    /// <summary>把失败原因落到日志，避免 COM 链路出问题时静默失效、无从排查。</summary>
    private static void LogError(string where, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopBeautify");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "desktopicons.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {where}: {ex}\n");
        }
        catch
        {
            // 日志本身失败不能影响主流程
        }
    }
}
