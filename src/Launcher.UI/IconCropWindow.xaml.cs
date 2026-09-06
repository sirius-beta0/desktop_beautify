using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.UI;

/// <summary>
/// 图标裁剪窗：选择一张图片后可在 300×300 圆形视口里缩放 + 拖拽平移，
/// 确认后输出为 256×256 圆形 PNG（外角透明）作为 Dock 中心按钮自定义图标。
/// </summary>
public partial class IconCropWindow : Window
{
    private readonly string _sourcePath;
    private bool _isDragging;
    private Point _lastMouse;
    private bool _initialized;

    /// <summary>裁剪后的 PNG 输出路径（由调用方在 DialogResult==true 后读取）。</summary>
    public string? OutputPath { get; private set; }

    public IconCropWindow(string sourcePath)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri(_sourcePath, UriKind.Absolute);
            var src = new BitmapImage(uri) { CacheOption = BitmapCacheOption.OnLoad };
            // 显式锁定到原始像素尺寸 + Stretch=Uniform，避免不同 DPI/测量下 ActualWidth 偏差
            SourceImage.Width = src.PixelWidth;
            SourceImage.Height = src.PixelHeight;
            SourceImage.Source = src;
            SourceImage.SizeChanged += OnSourceSizeChanged;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法加载图片：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private void OnSourceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_initialized) return;
        if (SourceImage.ActualWidth <= 0 || SourceImage.ActualHeight <= 0) return;
        _initialized = true;
        FitAndCenter();
    }

    /// <summary>
    /// 让图片完整落入 300×300 圆形视口并居中。
    /// 关键：缩放绕「视口中心 (150,150)」进行，平移量按 (150 - w/2)*s 计算，
    /// 使图片中心始终落在视口中心，缩放/拖拽都不会产生偏移。
    /// </summary>
    private void FitAndCenter()
    {
        double w = SourceImage.ActualWidth;
        double h = SourceImage.ActualHeight;
        double scale = Math.Min(300.0 / w, 300.0 / h);
        if (scale > 1.0) scale = 1.0;   // 小图默认不放大，避免模糊

        Canvas.SetLeft(SourceImage, 0);
        Canvas.SetTop(SourceImage, 0);
        // 缩放绕视口中心，天然保持中心内容不动
        ScaleTf.CenterX = 150;
        ScaleTf.CenterY = 150;
        ScaleTf.ScaleX = scale;
        ScaleTf.ScaleY = scale;
        // 平移使图片中心对齐视口中心（对称，无左右偏移）
        TranslateTf.X = (150 - w / 2) * scale;
        TranslateTf.Y = (150 - h / 2) * scale;
        ZoomSlider.Value = scale;
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitAndCenter();

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyZoom();

    /// <summary>缩放绕视口中心进行，平移量保持不变 → 当前中心内容始终固定，绝不偏移。</summary>
    private void ApplyZoom()
    {
        double s = ZoomSlider.Value;
        ScaleTf.ScaleX = s;
        ScaleTf.ScaleY = s;
    }

    // ---- 拖拽平移 ----

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        _lastMouse = e.GetPosition(CropCanvas);
        CropCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.GetPosition(CropCanvas);
        TranslateTf.X += pt.X - _lastMouse.X;
        TranslateTf.Y += pt.Y - _lastMouse.Y;
        _lastMouse = pt;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        CropCanvas.ReleaseMouseCapture();
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            CropCanvas.ReleaseMouseCapture();
        }
    }

    // ---- 确认 / 取消 ----

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        try
        {
            string outPath = SaveCroppedPng();
            OutputPath = outPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存图标失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 将当前圆形视口渲染为 256×256 圆形 PNG，保存到 %LocalAppData%/DesktopBeautify/center_icon/。
    /// 做法：先把 CropCanvas（含圆形裁剪）按原生 300×300 渲染，再用 DrawImage 等比缩放到 256，
    /// 这样绝不会因 RenderTransform 缩放引入偏移/裁剪错位。圆形裁剪由 CropCanvas.Clip 决定，外角天然透明。
    /// </summary>
    private string SaveCroppedPng()
    {
        const int viewSize = 300;
        const int outSize = 256;

        // 1) 原生尺寸渲染（不施加任何 RenderTransform，避免坐标偏移）
        var rtbView = new RenderTargetBitmap(viewSize, viewSize, 96, 96, PixelFormats.Pbgra32);
        rtbView.Render(CropCanvas);

        // 2) 等比缩放 300 -> 256（DrawingVisual 居中绘制，天然居中）
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(rtbView, new Rect(0, 0, outSize, outSize));
        }
        var rtb = new RenderTargetBitmap(outSize, outSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopBeautify", "center_icon");
        Directory.CreateDirectory(dir);
        string file = $"center_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        string path = Path.Combine(dir, file);

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(fs);
        }

        return path;
    }
}
