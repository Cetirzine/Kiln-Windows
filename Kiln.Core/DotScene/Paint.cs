namespace Kiln.Core.DotScene;

/// 各场景共用的绘制模板（对应原版 enum Paint）
public static class Paint
{
    /// 天空：从上到下按有序抖动分档
    public static void Sky(DotBuffer b, int ground, uint[] tones)
    {
        for (var y = 0; y < ground; y++)
            for (var x = 0; x < b.Cols; x++)
                b.Set(x, y, DotMath.DitherRamp(tones, (double)y / Math.Max(1, ground), x, y));
    }

    /// 方块云：从右往左飘
    public static void Clouds(DotBuffer b, double t, int maxY, int count = 5, uint color = 0xF7F9FFu, uint shadow = 0xD5E1FBu, int seed = 41)
    {
        for (var i = 0; i < count; i++)
        {
            var w = 22 + (int)(DotMath.Noise(i, 0, seed) * 30);
            var y = 4 + (int)(DotMath.Noise(i, 1, seed) * Math.Max(1, maxY - 8));
            var span = b.Cols + (double)w;
            var x = (int)(span - Mod(t * (1.6 + DotMath.Noise(i, 2, seed) * 1.8) + DotMath.Noise(i, 3, seed) * span, span)) - w;
            b.Fill(x, y, w, 4, color);
            b.Fill(x + 5, y - 3, Math.Max(4, w - 12), 3, color);
            for (var dx = 0; dx < w; dx += 2) b.Set(x + dx, y + 4, shadow);
        }
    }

    /// 地面以下的分层：表层、中层、深层，越深越暗
    public static void Strata(DotBuffer b, int ground, uint[] top, int topRows, uint[] mid, int midRows, uint[] deep, int seed = 0)
    {
        for (var y = ground; y < b.Rows; y++)
        {
            var d = y - ground;
            for (var x = 0; x < b.Cols; x++)
            {
                var c = d < topRows ? DotMath.Pick(top, x, y, seed)
                    : d < topRows + midRows ? DotMath.Pick(mid, x, y, seed + 1)
                    : DotMath.Pick(deep, x, y, seed + 2);
                var dark = (double)d / Math.Max(1, b.Rows - ground) * 0.8;
                b.Set(x, y, DotMath.Dither(x, y, dark) ? DotMath.Shade(c, 0.55) : c);
            }
        }
    }

    /// 飘落的东西（花瓣、树叶、雪）：慢慢往下飘，左右摆动
    public static void Falling(DotBuffer b, double t, int count, uint[] colors, int top, int bottom, double speed = 0.06, int seed = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var sp = speed * (0.6 + DotMath.Noise(i, 1, seed) * 0.8);
            var p = Mod(t * sp + DotMath.Noise(i, 2, seed), 1);
            var x = (int)(DotMath.Noise(i, 3, seed) * b.Cols) + (int)(Math.Sin(t * 1.3 + i) * 4) - (int)(p * 14);
            var y = top + (int)(p * (bottom - top));
            var c = colors[i % colors.Length];
            b.Set(x, y, c);
            if (i % 3 == 0) b.Set(x + 1, y, DotMath.Shade(c, 0.85));
        }
    }

    /// 往上飘的东西（火星、孢子）
    public static void Rising(DotBuffer b, double t, int count, uint[] colors, int top, int bottom, double speed = 0.05, int seed = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var sp = speed * (0.6 + DotMath.Noise(i, 1, seed) * 0.8);
            var p = Mod(t * sp + DotMath.Noise(i, 2, seed), 1);
            var x = (int)(DotMath.Noise(i, 3, seed) * b.Cols) + (int)(Math.Sin(t * 0.9 + i) * 3);
            var y = bottom - (int)(p * (bottom - top));
            if (Math.Sin(t * 2.4 + i * 1.3) > -0.4) b.Set(x, y, colors[i % colors.Length]);
        }
    }

    /// 一块 16×16 的方块：平涂加一圈明暗边，像游戏里的方块正面
    public static void Block(DotBuffer b, int x, int y, uint c, int size = 16, int seed = 0)
    {
        for (var dy = 0; dy < size; dy++)
            for (var dx = 0; dx < size; dx++)
            {
                var v = c;
                if (dy == 0 || dx == 0) v = DotMath.Shade(c, 1.12);
                else if (dy == size - 1 || dx == size - 1) v = DotMath.Shade(c, 0.8);
                else if (DotMath.Noise(x + dx, y + dy, seed) < 0.08) v = DotMath.Shade(c, 0.94);
                b.Set(x + dx, y + dy, v);
            }
    }

    /// 火焰：两三层颜色，随时间跳动
    public static void Flame(DotBuffer b, int x, int bottom, int width, int height, double t, uint[]? palette = null)
    {
        palette ??= new uint[] { 0xFF5A1Fu, 0xFF9A2Eu, 0xFFD54Au, 0xFFF6B0u };
        var reversed = palette.Reverse().ToArray();
        for (var dx = 0; dx < width; dx++)
        {
            var center = 1 - Math.Abs(dx - (width - 1) / 2.0) / (width / 2.0);
            var h = (int)(height * (0.45 + 0.55 * center) * (0.75 + 0.25 * Math.Sin(t * 9 + dx * 1.7)));
            for (var dy = 0; dy < Math.Max(1, h); dy++)
            {
                var k = (double)dy / Math.Max(1, h);
                b.Set(x + dx, bottom - dy, DotMath.DitherRamp(reversed, 1 - k * 0.9 - (1 - center) * 0.3, x + dx, bottom - dy));
            }
        }
    }

    public static double Mod(double a, double b) => ((a % b) + b) % b;
}



