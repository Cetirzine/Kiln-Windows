using System.Numerics;

namespace Kiln.Core.DotScene;

/// 点阵缓冲：cols × rows 个点，值为 0xRRGGBB | 0xFF000000；0 表示空（对应原版 DotBuffer）
public class DotBuffer
{
    public readonly int Cols;
    public readonly int Rows;
    public readonly uint[] Px;

    public DotBuffer(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        Px = new uint[cols * rows];
    }

    public uint this[int x, int y]
    {
        get => x >= 0 && y >= 0 && x < Cols && y < Rows ? Px[y * Cols + x] : 0;
        set { if (x >= 0 && y >= 0 && x < Cols && y < Rows) Px[y * Cols + x] = value; }
    }

    public void Set(int x, int y, uint rgb) => this[x, y] = 0xFF00_0000u | rgb;

    public void Fill(int x0, int y0, int w, int h, uint rgb)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                Set(x, y, rgb);
    }

    /// 把精灵画到 (x, 底边 bottom)，可缩放成 w×h（最近邻），用于落地时的压扁效果
    public void Blit(Sprite s, int x, int bottom, int? w = null, int? h = null, bool flip = false)
    {
        var tw = w ?? s.Width;
        var th = h ?? s.Height;
        for (var dy = 0; dy < th; dy++)
        {
            var sy = dy * s.Height / th;
            for (var dx = 0; dx < tw; dx++)
            {
                var sx = dx * s.Width / tw;
                var c = s.Px[sy * s.Width + (flip ? s.Width - 1 - sx : sx)];
                if (c != 0) this[x + dx, bottom - th + 1 + dy] = c;
            }
        }
    }

    /// 绕 pivot 旋转后画精灵（最近邻，逆向映射避免空洞）。angle 为向前（画面左方）转动的角度，0 = 不转
    public void BlitRotated(Sprite s, double pivotX, double pivotY, (double X, double Y) origin, double angle)
    {
        var c = Math.Cos(angle);
        var sn = Math.Sin(angle);
        var r = (int)(Math.Max(s.Width, s.Height) * 1.5) + 2;
        for (var ty = -r; ty <= r; ty++)
        {
            for (var tx = -r; tx <= r; tx++)
            {
                // 屏幕坐标里的偏移 → 精灵坐标里的偏移
                var ox = tx * c + ty * sn;
                var oy = -tx * sn + ty * c;
                var sx = (int)Math.Floor(ox + origin.X);
                var sy = (int)Math.Floor(oy + origin.Y);
                if (sx < 0 || sy < 0 || sx >= s.Width || sy >= s.Height) continue;
                var v = s.Px[sy * s.Width + sx];
                if (v != 0) this[(int)pivotX + tx, (int)pivotY + ty] = v;
            }
        }
    }
}

/// 像素精灵：字符图 + 调色板
public class Sprite
{
    public readonly int Width;
    public readonly int Height;
    public readonly uint[] Px;

    public Sprite(IReadOnlyList<string> rows, IReadOnlyDictionary<char, uint> palette)
    {
        Width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        Height = rows.Count;
        Px = new uint[Width * Height];
        for (var y = 0; y < rows.Count; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (palette.TryGetValue(rows[y][x], out var c))
                    Px[y * Width + x] = 0xFF00_0000u | c;
    }

    public Sprite(int width, int height, Func<int, int, uint?> f)
    {
        Width = width;
        Height = height;
        Px = new uint[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (f(x, y) is { } c)
                    Px[y * width + x] = 0xFF00_0000u | c;
    }

    /// 在精灵上逐点改色：f 返回 null 表示保持原样
    public Sprite Painted(Func<int, int, uint?> f) =>
        new(Width, Height, (x, y) =>
        {
            if (f(x, y) is { } c) return c;
            var v = Px[y * Width + x];
            return v == 0 ? null : v & 0xFFFFFFu;
        });
}

/// 可复现的伪随机噪声，用来给方块加纹理
public static class DotMath
{
    public static double Noise(int x, int y, int seed = 0)
    {
        unchecked
        {
            var h = (ulong)(long)(x * 374_761_393 + y * 668_265_263 + seed * 2_147_483_647);
            h = (h ^ (h >> 13)) * 1_274_126_177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65_535.0;
        }
    }

    public static uint Pick(uint[] tones, int x, int y, int seed = 0) =>
        tones[Math.Min(tones.Length - 1, (int)(Noise(x, y, seed) * tones.Length))];

    private static readonly double[] Bayer4 = new double[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 }.Select(v => (v + 0.5) / 16).ToArray();

    /// 4×4 Bayer 有序抖动：frac 落在 [0,1]，返回这个点该用较亮的一侧
    public static bool Dither(int x, int y, double frac) => Bayer4[(y & 3) * 4 + (x & 3)] < frac;

    /// 按 frac（0…1）在一组颜色之间做有序抖动的渐变
    public static uint DitherRamp(uint[] tones, double frac, int x, int y)
    {
        var f = Math.Clamp(frac, 0, 0.9999) * (tones.Length - 1);
        var i = (int)f;
        return Dither(x, y, f - i) ? tones[Math.Min(i + 1, tones.Length - 1)] : tones[i];
    }

    /// 把 0xRRGGBB 调亮或调暗（k > 1 变亮）
    public static uint Shade(uint c, double k)
    {
        uint Ch(uint v) => (uint)Math.Clamp(v * k, 0, 255);
        return (Ch((c >> 16) & 0xFF) << 16) | (Ch((c >> 8) & 0xFF) << 8) | Ch(c & 0xFF);
    }

    /// 0…1 之间的缓入缓出
    public static double Ease(double p)
    {
        var q = Math.Clamp(p, 0, 1);
        return q * q * (3 - 2 * q);
    }

    /// 在 [a, b] 时段内从 0 走到 1，之后保持 1
    public static double Ramp(double t, double a, double b) => Ease((t - a) / (b - a));
}

/// 场景里的人物：登录的玩家（用他自己的皮肤，没有自定义皮肤就是默认外观），以及其他所有角色（一律用作者 NelsonPence 的皮肤）
public class SceneCast
{
    public PlayerLook Player = PlayerLook.Steve;
    public PlayerLook Author = PlayerLook.Alex;
}

/// 一个版本的场景：静态部分画一次，动态部分每帧画
public interface IDotScene
{
    string Id { get; }
    uint Background { get; }
    void PaintStatic(DotBuffer b);
    void PaintDynamic(DotBuffer b, double t, SceneCast cast);
}



