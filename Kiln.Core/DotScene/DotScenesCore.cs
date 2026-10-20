namespace Kiln.Core.DotScene;

/// 画到屏幕前统一提一点饱和度与亮度，让点阵更鲜亮；每个像素铺满整格（对应原版 DotRenderer）
public static class DotRenderer
{
    public static uint Vivid(uint v)
    {
        var r = (double)((v >> 16) & 0xFF) / 255;
        var g = (double)((v >> 8) & 0xFF) / 255;
        var b = (double)(v & 0xFF) / 255;
        var l = 0.299 * r + 0.587 * g + 0.114 * b;
        const double sat = 1.28, lift = 1.1;
        r = (l + (r - l) * sat) * lift;
        g = (l + (g - l) * sat) * lift;
        b = (l + (b - l) * sat) * lift;
        uint C(double x) => (uint)Math.Clamp(x * 255, 0, 255);
        return (C(r) << 16) | (C(g) << 8) | C(b);
    }

    /// 渲染为 BGRA 像素（自顶向下），cell 为每点像素数
    public static byte[] Render(DotBuffer b, int cell, uint background)
    {
        var w = b.Cols * cell;
        var h = b.Rows * cell;
        var pixels = new byte[w * h * 4];
        byte bgB = (byte)(background & 0xFF), bgG = (byte)((background >> 8) & 0xFF), bgR = (byte)((background >> 16) & 0xFF);
        for (var i = 0; i < w * h; i++)
        {
            pixels[i * 4] = bgB;
            pixels[i * 4 + 1] = bgG;
            pixels[i * 4 + 2] = bgR;
            pixels[i * 4 + 3] = 255;
        }
        for (var y = 0; y < b.Rows; y++)
        {
            var v0 = y * cell;
            for (var x = 0; x < b.Cols; x++)
            {
                var v = b.Px[y * b.Cols + x];
                if (v == 0) continue;
                var vv = Vivid(v & 0xFFFFFF);
                byte blue = (byte)(vv & 0xFFu), green = (byte)((vv >> 8) & 0xFFu), red = (byte)((vv >> 16) & 0xFFu);
                for (var dy = 0; dy < cell; dy++)
                {
                    var row = (v0 + dy) * w;
                    for (var dx = 0; dx < cell; dx++)
                    {
                        var i = (row + x * cell + dx) * 4;
                        pixels[i] = blue;
                        pixels[i + 1] = green;
                        pixels[i + 2] = red;
                        pixels[i + 3] = 255;
                    }
                }
            }
        }
        return pixels;
    }

    /// 渲染为 BGR24（自顶向下），供 ffmpeg rawvideo 使用
    public static byte[] RenderBgr24(DotBuffer b, int cell, uint background)
    {
        var bgra = Render(b, cell, background);
        var rgb = new byte[bgra.Length / 4 * 3];
        for (var i = 0; i < rgb.Length / 3; i++)
        {
            rgb[i * 3] = bgra[i * 4];
            rgb[i * 3 + 1] = bgra[i * 4 + 1];
            rgb[i * 3 + 2] = bgra[i * 4 + 2];
        }
        return rgb;
    }
}

/// 故障效果：每隔几秒一次很短的爆发，几条横带错位，带青 / 品红的色边（对应原版 Glitch）
public static class Glitch
{
    public readonly record struct Band(int Y, int H, int Dx);

    public static List<Band> Bands(double t, int rows)
    {
        const double period = 5.7, burst = 0.32;
        var n = (int)(t / period);
        var p = t - n * period;
        if (p >= burst || rows <= 8) return new();
        var frame = (int)(p * 30);
        var count = 2 + (int)(DotMath.Noise(n, 0, 61) * 3);
        var result = new List<Band>();
        for (var i = 0; i < count; i++)
        {
            var y = (int)(DotMath.Noise(n, i, 62) * (rows - 6));
            var h = 2 + (int)(DotMath.Noise(n, i + 7, 63) * 7);
            // 每一帧方向和幅度都在变，看起来是闪的
            var dx = (int)((DotMath.Noise(n * 31 + frame, i, 64) - 0.5) * 22);
            result.Add(new Band(y, h, dx));
        }
        return result;
    }

    /// 把横带错位 + 青 / 品红色边应用到已渲染的帧上（BGR24）
    public static byte[] Apply(byte[] frame, int cols, int rows, IReadOnlyList<Band> bands)
    {
        if (bands.Count == 0) return frame;
        var shifted = (byte[])frame.Clone();
        var stride = cols * 3;
        foreach (var band in bands)
        {
            for (var y = band.Y; y < band.Y + band.H && y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var sx = x + band.Dx;
                    var di = y * stride + x * 3;
                    if (sx >= 0 && sx < cols)
                    {
                        var si = y * stride + sx * 3;
                        shifted[di] = frame[si];
                        shifted[di + 1] = frame[si + 1];
                        shifted[di + 2] = frame[si + 2];
                    }
                    else
                    {
                        shifted[di] = shifted[di + 1] = shifted[di + 2] = 8;
                    }
                }
            }
            // 青（右边）与品红（左边）色边
            void Tint(int edgeX, byte r, byte g, byte b)
            {
                if (edgeX < 0 || edgeX >= cols) return;
                for (var y = band.Y; y < band.Y + band.H && y < rows; y++)
                {
                    var i = y * stride + edgeX * 3;
                    shifted[i] = (byte)Math.Min(255, shifted[i] + b);
                    shifted[i + 1] = (byte)Math.Min(255, shifted[i + 1] + g);
                    shifted[i + 2] = (byte)Math.Min(255, shifted[i + 2] + r);
                }
            }
            Tint(band.Dx + 2, 0, 230, 255);
            Tint(band.Dx - 2, 255, 26, 204);
        }
        return shifted;
    }
}

/// 按游戏版本选场景：同一个大版本（以及它的快照、预发布）都用同一个场景（对应原版 DotScenes）
public static class DotScenes
{
    public static IDotScene SceneFor(string version)
    {
        var family = Family(version);
        if (family == null) return new Plains();
        var (major, minor) = family.Value;
        // 1.21 之后每个小版本都是一次独立的更新
        if (major == 1 && minor == 21)
        {
            return Patch(version) switch
            {
                >= 11 => new MountsOfMayhem(),
                >= 9 => new CopperAge(),
                >= 6 => new ChaseTheSkies(),
                5 => new SpringToLife(),
                4 => new GardenAwakens(),
                _ => new TrickyTrials(),
            };
        }
        return (major, minor) switch
        {
            (1, 0) or (1, 1) => new TheEnd(),
            (1, 2) => new JungleUpdate(),
            (1, 3) => new DesertTrade(),
            (1, 4) => new PrettyScary(),
            (1, 5) => new RedstoneUpdate(),
            (1, 6) => new HorseUpdate(),
            (1, 7) => new ChangedTheWorld(),
            (1, 8) => new PvpArena(),
            (1, 9) => new CombatUpdate(),
            (1, 10) => new Frostburn(),
            (1, 11) => new Exploration(),
            (1, 12) => new WorldOfColor(),
            (1, 13) => new UpdateAquatic(),
            (1, 14) => new VillageAndPillage(),
            (1, 15) => new BuzzyBees(),
            (1, 16) => new NetherUpdate(),
            (1, 17) => new CavesAndCliffsI(),
            (1, 18) => new CavesAndCliffsII(),
            (1, 19) => new WildUpdate(),
            (1, 20) => new TrailsAndTales(),
            (1, 21) => new TrickyTrials(),
            (26, 1) => new TinyTakeover(),
            (26, 2) => new SulfurCaves(),
            (26, 3) => new WildernessBound(),
            (26, 4) => new IceCaves(),
            _ => new Plains(),
        };
    }

    /// 版本号的前两段：1.8.9 → (1, 8)，26.4-snapshot-3 → (26, 4)
    public static (int Major, int Minor)? Family(string version)
    {
        var parts = Numbers(version);
        return parts.Length >= 2 ? (parts[0], parts[1]) : null;
    }

    /// 版本号的第三段：1.21.4 → 4，1.21 → 0
    public static int Patch(string version)
    {
        var parts = Numbers(version);
        return parts.Length >= 3 ? parts[2] : 0;
    }

    /// 开头那串数字和点：1.21.4-rc1 → [1, 21, 4]；b1.7.3 这类远古版本不以数字开头，返回空
    private static int[] Numbers(string version)
    {
        var head = new string(version.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return head.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : (int?)null)
            .Where(n => n != null).Select(n => n!.Value).ToArray();
    }
}
