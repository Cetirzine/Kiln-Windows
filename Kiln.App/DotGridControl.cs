using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Kiln.App;

// SwiftUI DotGrid: 1 point dots at 6 point intervals, 9% text color.
public sealed class DotGridControl : UserControl
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, Opacity = .09, IsHitTestVisible = false };
    public DotGridControl()
    {
        IsHitTestVisible = false;
        Content = _image;
        SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) => Draw();
    }
    private void Draw()
    {
        var width = (int)Math.Ceiling(ActualWidth); var height = (int)Math.Ceiling(ActualHeight);
        if (width < 1 || height < 1) return;
        var data = new byte[width * height * 4];
        var color = ActualTheme == ElementTheme.Dark ? (byte)236 : (byte)30;
        for (var y = 3; y < height; y += 6)
            for (var x = 3; x < width; x += 6)
            {
                var p = (y * width + x) * 4;
                data[p] = data[p + 1] = data[p + 2] = color; data[p + 3] = 255;
            }
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(data);
        bitmap.Invalidate(); _image.Source = bitmap;
    }
}
