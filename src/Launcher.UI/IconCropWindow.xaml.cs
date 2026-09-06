using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.UI;

/// <summary>
/// 图标裁剪窗：选择一张图片后可在 300×300 正方形视口里缩放 + 拖拽平移，
/// 确认后输出为 256×256 PNG 作为 Dock 中心按钮自定义图标。
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

    /// <summary>让图片完整落入 300×300 视口并居中；小图不强制放大，保持原尺寸。</summary>
    private void FitAndCenter()
    {
        double w = SourceImage.ActualWidth;
        double h = SourceImage.ActualHeight;
        double scale = Math.Min(300.0 / w, 300.0 / h);
        if (scale > 1.0) scale = 1.0;   // 小图默认不放大，避免模糊
        ZoomSlider.Value = scale;
        ApplyZoom();
        Canvas.SetLeft(SourceImage, 150 - w / 2);
        Canvas.SetTop(SourceImage, 150 - h / 2);
        TranslateTf.X = 0;
        TranslateTf.Y = 0;
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitAndCenter();

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyZoom();

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
        var dx = pt.X - _lastMouse.X;
        var dy = pt.Y - _lastMouse.Y;
        TranslateTf.X += dx;
        TranslateTf.Y += dy;
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
    /// 将当前视口渲染为 256×256 PNG，保存到 %LocalAppData%/DesktopBeautify/center_icon/。
    /// 使用时间戳命名，避免浏览器/缓存旧图。
    /// </summary>
    private string SaveCroppedPng()
    {
        const int viewSize = 300;
        const int outSize = 256;

        // 临时对 CropCanvas 做缩放，使 300 视口渲染成 256 输出
        var oldTransform = CropCanvas.RenderTransform;
        double saveScale = (double)outSize / viewSize;
        CropCanvas.RenderTransform = new ScaleTransform(saveScale, saveScale);

        try
        {
            var rtb = new RenderTargetBitmap(outSize, outSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(CropCanvas);

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopBeautify", "center_icon");
            Directory.CreateDirectory(dir);
            string file = $"center_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string path = Path.Combine(dir, file);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(fs);

            return path;
        }
        finally
        {
            CropCanvas.RenderTransform = oldTransform;
        }
    }
}
