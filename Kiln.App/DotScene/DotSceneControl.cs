using System.Runtime.InteropServices.WindowsRuntime;
using Kiln.Core.DotScene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App.DotScene;

/// 主页像素场景控件：静态层缓存 + 每帧动态层 + 故障带，30fps（对应原版 DotSceneView）
public sealed class DotSceneControl : UserControl
{
    private readonly Microsoft.UI.Xaml.Controls.Image _image = new() { Stretch = Stretch.UniformToFill };
    private WriteableBitmap? _bitmap;
    private DotBuffer? _staticCache;
    private string _cacheKey = "";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    private IDotScene? _scene;
    private SceneCast _cast = new();
    private readonly DateTime _start = DateTime.UtcNow;
    private double _nextCaptionSample;
    public event Action<double>? CaptionLuminanceChanged;

    public IDotScene? Scene
    {
        get => _scene;
        set { _scene = value; _cacheKey = ""; }
    }

    public SceneCast Cast
    {
        get => _cast;
        set => _cast = value ?? new SceneCast();
    }

    public DotSceneControl()
    {
        Content = _image;
        SizeChanged += (_, _) => _cacheKey = "";
        _timer.Tick += OnFrame;
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnFrame(object? sender, object e)
    {
        if (_scene == null || ActualWidth < 10 || ActualHeight < 10) return;
        // 每格约 4px，宽度上大约 250 个点
        var scale = XamlRoot.RasterizationScale;
        var cellD = Math.Max(3.4, ActualWidth / 250);
        var cell = Math.Max(1, (int)Math.Ceiling(cellD * scale));
        var cols = (int)Math.Ceiling(ActualWidth * scale / cell);
        var rows = (int)Math.Ceiling(ActualHeight * scale / cell);
        if (cols <= 0 || rows <= 0) return;
        var key = $"{_scene.Id}-{cols}x{rows}-{cell}";
        if (key != _cacheKey || _staticCache == null)
        {
            var staticBuffer = new DotBuffer(cols, rows);
            _scene.PaintStatic(staticBuffer);
            _staticCache = staticBuffer;
            _cacheKey = key;
            _bitmap = new WriteableBitmap(cols * cell, rows * cell);
            _image.Source = _bitmap;
        }
        var t = (DateTime.UtcNow - _start).TotalSeconds;
        // 合成：静态层 + 动态层 + 闪烁亮点
        var dyn = new DotBuffer(cols, rows);
        _scene.PaintDynamic(dyn, t, _cast);
        for (var i = 0; i < 14; i++)
            if (Math.Sin(t * 3 + i * 1.7) > 0.93)
                dyn.Set((int)(DotMath.Noise(i, (int)(t / 2), 71) * cols), (int)(DotMath.Noise(i, (int)(t / 2), 72) * rows * 0.6), 0xFFFFFFu);
        var composed = new DotBuffer(cols, rows);
        Array.Copy(_staticCache.Px, composed.Px, composed.Px.Length);
        for (var i = 0; i < dyn.Px.Length; i++)
            if (dyn.Px[i] != 0) composed.Px[i] = dyn.Px[i];
        // 故障带：把几条横带错开，两侧加青色和品红的色边（BGRA）
        var bands = Glitch.Bands(t, rows);
        var pixels = DotRenderer.Render(composed, cell, _scene.Background);
        var w = cols * cell;
        if (bands.Count > 0)
        {
            var stride = w * 4;
            foreach (var band in bands)
            {
                var buffer = (byte[])pixels.Clone();
                for (var y = band.Y; y < band.Y + band.H && y < rows; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var sx = x + band.Dx * cell;
                        var dIdx = y * stride + x * 4;
                        if (sx >= 0 && sx < w)
                        {
                            var sIdx = y * stride + sx * 4;
                            pixels[dIdx] = buffer[sIdx];
                            pixels[dIdx + 1] = buffer[sIdx + 1];
                            pixels[dIdx + 2] = buffer[sIdx + 2];
                        }
                        else
                        {
                            pixels[dIdx] = pixels[dIdx + 1] = pixels[dIdx + 2] = 8;
                        }
                    }
                void Tint(int edgeX, byte r, byte g, byte b)
                {
                    if (edgeX < 0 || edgeX >= w) return;
                    for (var y = band.Y; y < band.Y + band.H && y < rows; y++)
                    {
                        var i = y * stride + edgeX * 4;
                        pixels[i] = (byte)Math.Min(255, pixels[i] + b);
                        pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + g);
                        pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + r);
                    }
                }
                Tint((band.Dx + 2) * cell, 0, 230, 255);
                Tint((band.Dx - 2) * cell, 255, 26, 204);
            }
        }
        using var stream = _bitmap!.PixelBuffer.AsStream();
        stream.Seek(0, SeekOrigin.Begin);
        stream.Write(pixels, 0, pixels.Length);
        _bitmap.Invalidate();
        if (t >= _nextCaptionSample)
        {
            _nextCaptionSample = t + .25;
            // Sample the rendered pixels behind the native caption buttons, including animation.
            var inset = MainWindow.Current?.AppWindow.TitleBar.RightInset ?? (int)(138 * scale);
            var sampleWidth = Math.Min(w, Math.Max(1, inset));
            var sampleHeight = Math.Min(rows * cell, Math.Max(1, (int)(32 * scale)));
            double luminance = 0; var samples = 0;
            static double Linear(byte value) { var srgb = value / 255d; return srgb <= .04045 ? srgb / 12.92 : Math.Pow((srgb + .055) / 1.055, 2.4); }
            for (var y = 0; y < sampleHeight; y += 4)
                for (var x = w - sampleWidth; x < w; x += 4)
                {
                    var offset = (y * w + x) * 4;
                    luminance += .2126 * Linear(pixels[offset + 2]) + .7152 * Linear(pixels[offset + 1]) + .0722 * Linear(pixels[offset]);
                    samples++;
                }
            if (samples > 0) CaptionLuminanceChanged?.Invoke(luminance / samples);
        }
    }
}
