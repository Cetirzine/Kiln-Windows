using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;
using Point = Windows.Foundation.Point;

namespace Kiln.App.DotScene;

/// 与游戏物品栏一致的等轴测投影：绕 Y 轴 45°、绕 X 轴 30°（对应原版 IsoGeometry）
public struct IsoGeometry
{
    public Point Top, Left, Right, Center, LeftBottom, RightBottom, Bottom;

    public IsoGeometry(double width, double height)
    {
        const double side = 0.6124;  // cos30° / (2·cos45°)
        const double ratio = 0.5 + side;
        var w = Math.Min(width, height / ratio) * 0.96;
        var h = w * ratio;
        var x0 = (width - w) / 2;
        var y0 = (height - h) / 2;
        Top = new Point(x0 + w / 2, y0);
        Left = new Point(x0, y0 + w / 4);
        Right = new Point(x0 + w, y0 + w / 4);
        Center = new Point(x0 + w / 2, y0 + w / 2);
        LeftBottom = new Point(Left.X, Left.Y + w * side);
        RightBottom = new Point(Right.X, Right.Y + w * side);
        Bottom = new Point(Center.X, Center.Y + w * side);
    }
}

/// 版本图标：有本机游戏材质时使用原版材质渲染的等轴测方块，否则使用代码绘制的方块（对应原版 BlockIcon）
public sealed class BlockIconControl : UserControl
{
    private readonly Grid _grid = new();
    private string _kind = "";
    private static readonly Dictionary<string, byte[]?> TextureCache = new();

    public string Kind
    {
        get => _kind;
        set { _kind = value; Rebuild(); }
    }

    public BlockIconControl()
    {
        Content = _grid;
        SizeChanged += (_, _) => Rebuild();
        Loaded += (_, _) => { BlockTextures.Shared.Changed += TexturesChanged; Rebuild(); };
        Unloaded += (_, _) => BlockTextures.Shared.Changed -= TexturesChanged;
    }
    private void TexturesChanged() => DispatcherQueue.TryEnqueue(Rebuild);

    public static BlockIconControl For(string kind, double size = 44) => new() { Kind = kind, Width = size, Height = size };

    private void Rebuild()
    {
        _grid.Children.Clear();
        if (ActualWidth < 4 || string.IsNullOrEmpty(_kind)) return;
        // 有本机游戏材质时用材质渲染（BlockTextures 提取），否则用代码绘制
        if (BlockTextures.Shared.GetTexture(_kind) is { } pixels && TryRenderTextured(pixels)) return;
        Drawn();
    }

    /// 用原版材质渲染：顶面菱形 + 两个平行四边形侧面，最近邻采样（对应原版 TexturedBlock）
    private bool TryRenderTextured(BlockTextures.Faces faces)
    {
        var size = (int)Math.Max(8, Math.Min(ActualWidth, ActualHeight));
        if (size < 8) return false;
        var bitmap = new WriteableBitmap(size, size);
        var pixels = new byte[size * size * 4];
        var g = new IsoGeometry(size, size);
        // 每个面：origin + 两个基向量；对目标每像素反算 (u,v) 采样
        void Face(byte[] src, int sw, int sh, Point a, Point b, Point d, double shade)
        {
            var ux = b.X - a.X;
            var uy = b.Y - a.Y;
            var vx = d.X - a.X;
            var vy = d.Y - a.Y;
            var det = ux * vy - uy * vx;
            if (Math.Abs(det) < 1e-9) return;
            var minX = (int)Math.Floor(Math.Max(0, Math.Min(Math.Min(a.X, b.X), Math.Min(d.X, g.Right.X))));
            var maxX = (int)Math.Ceiling(Math.Min(size - 1, Math.Max(Math.Max(a.X, b.X), Math.Max(d.X, g.Right.X))));
            var minY = (int)Math.Floor(Math.Max(0, Math.Min(Math.Min(a.Y, b.Y), d.Y)));
            var maxY = (int)Math.Ceiling(Math.Min(size - 1, Math.Max(Math.Max(a.Y, b.Y), Math.Max(a.Y + vy, d.Y + vy))));
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x - a.X;
                    var py = y - a.Y;
                    var u = (px * vy - py * vx) / det;
                    var v = (py * ux - px * uy) / det;
                    if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                    var sx = Math.Min(sw - 1, (int)(u * sw));
                    var sy = Math.Min(sh - 1, (int)(v * sh));
                    var i = (sy * sw + sx) * 4;
                    byte B = src[i], G = src[i + 1], R = src[i + 2];
                    if (shade > 0)
                    {
                        B = (byte)(B * (1 - shade));
                        G = (byte)(G * (1 - shade));
                        R = (byte)(R * (1 - shade));
                    }
                    var di = (y * size + x) * 4;
                    pixels[di] = B;
                    pixels[di + 1] = G;
                    pixels[di + 2] = R;
                    pixels[di + 3] = 255;
                }
        }
        Face(faces.Top, faces.TopW, faces.TopH, g.Left, g.Top, g.Center, 0);
        Face(faces.Left, faces.LeftW, faces.LeftH, g.Left, g.Center, g.LeftBottom, 0.2);
        Face(faces.Right, faces.RightW, faces.RightH, g.Center, g.Right, g.Bottom, 0.4);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        _grid.Children.Add(new Microsoft.UI.Xaml.Controls.Image
        {
            Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform,
        });
        return true;
    }

    /// 代码绘制的等轴测方块（本机还没有游戏文件时使用；配色与原版 DrawnBlock 一致）
    private void Drawn()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var g = new IsoGeometry(w, h);
        var (top, left, right, mark) = Palette(_kind);
        static Polygon Poly(Windows.UI.Color c, params Point[] points)
        {
            var p = new Polygon { Fill = new SolidColorBrush(c) };
            foreach (var pt in points) p.Points.Add(pt);
            return p;
        }
        Windows.UI.Color C(uint hex) => Windows.UI.Color.FromArgb(255, (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
        _grid.Children.Add(Poly(C(top), g.Top, g.Right, g.Center, g.Left));
        _grid.Children.Add(Poly(C(left), g.Left, g.Center, g.Bottom, g.LeftBottom));
        _grid.Children.Add(Poly(C(right), g.Center, g.Right, g.RightBottom, g.Bottom));
        if (mark is { } m)
        {
            // 顶面中央的标记
            const double s = 0.32;
            var p1 = new Point(g.Center.X, g.Top.Y + (g.Center.Y - g.Top.Y) * s);
            var p2 = new Point(g.Center.X + (g.Right.X - g.Center.X) * (1 - s * 1.4), g.Left.Y);
            var p3 = new Point(g.Center.X, g.Center.Y - (g.Center.Y - g.Top.Y) * s);
            var p4 = new Point(g.Center.X - (g.Center.X - g.Left.X) * (1 - s * 1.4), g.Left.Y);
            _grid.Children.Add(Poly(Windows.UI.Color.FromArgb(217, (byte)((m >> 16) & 0xFF), (byte)((m >> 8) & 0xFF), (byte)(m & 0xFF)), p1, p2, p3, p4));
        }
    }

    private static (uint Top, uint Left, uint Right, uint? Mark) Palette(string kind) => kind switch
    {
        "Grass" => (0x6dbb45, 0x8a5a33, 0x6f4627, null),
        "CommandBlock" => (0xc98a4b, 0xb07339, 0x8f5c2c, 0xe8e2c9),
        "CobbleStone" => (0x9a9a9a, 0x7d7d7d, 0x636363, null),
        "GoldBlock" => (0xf7d84a, 0xe0b52c, 0xc39318, null),
        "Anvil" => (0x5b5f66, 0x44474d, 0x33363b, 0x8d939c),
        "Fabric" => (0xdcd3b8, 0xbfb391, 0xa39674, 0x38342a),
        "NeoForge" => (0xf08a3c, 0xd36d22, 0xae5615, 0xffe2c4),
        "Quilt" => (0xb58ee8, 0x9068cc, 0x7350ad, null),
        "GrassPath" => (0xa98b4f, 0x8a5a33, 0x6f4627, null),
        "RedstoneBlock" => (0xd8341f, 0xb02412, 0x8d1a0c, null),
        "Kiln" => (0xa3a39e, 0x7e7e79, 0x62625e, null),
        _ => (0x6aa7f7, 0x3f86ee, 0x1d6ae0, null),
    };
}

/// 方块图标使用 Minecraft 原版材质：从本机已安装的游戏客户端 jar 中读取并缓存（对应原版 BlockTextures）
public sealed class BlockTextures
{
    public sealed class Faces(byte[] top, int topW, int topH, byte[] left, int leftW, int leftH, byte[] right, int rightW, int rightH)
    {
        public readonly byte[] Top = top;
        public readonly int TopW = topW, TopH = topH;
        public readonly byte[] Left = left;
        public readonly int LeftW = leftW, LeftH = leftH;
        public readonly byte[] Right = right;
        public readonly int RightW = rightW, RightH = rightH;
    }

    public static readonly BlockTextures Shared = new();

    /// 内置图标种类 → 顶面、左面（正面）、右面材质，以及顶面的着色
    private static readonly Dictionary<string, (string[] Top, string[] Left, string[] Right, uint? Tint)> Specs = new()
    {
        ["Grass"] = (new[] { "grass_block_top" }, new[] { "grass_block_side" }, new[] { "grass_block_side" }, 0x91BD59),
        ["CommandBlock"] = (new[] { "command_block_side" }, new[] { "command_block_front" }, new[] { "command_block_side" }, null),
        ["CobbleStone"] = (new[] { "cobblestone" }, new[] { "cobblestone" }, new[] { "cobblestone" }, null),
        ["GoldBlock"] = (new[] { "gold_block" }, new[] { "gold_block" }, new[] { "gold_block" }, null),
        ["Anvil"] = (new[] { "anvil_top" }, new[] { "anvil" }, new[] { "anvil" }, null),
        ["NeoForge"] = (new[] { "smithing_table_top" }, new[] { "smithing_table_front" }, new[] { "smithing_table_side" }, null),
        ["Fabric"] = (new[] { "loom_top" }, new[] { "loom_front" }, new[] { "loom_side" }, null),
        ["Quilt"] = (new[] { "purple_wool" }, new[] { "purple_wool" }, new[] { "purple_wool" }, null),
        ["GrassPath"] = (new[] { "dirt_path_top" }, new[] { "dirt_path_side" }, new[] { "dirt_path_side" }, null),
        ["RedstoneBlock"] = (new[] { "redstone_block" }, new[] { "redstone_block" }, new[] { "redstone_block" }, null),
        ["Kiln"] = (new[] { "furnace_top" }, new[] { "furnace_front_on" }, new[] { "furnace_side" }, null),
        ["block:bookshelf"] = (new[] { "oak_planks" }, new[] { "bookshelf" }, new[] { "bookshelf" }, null),
    };

    /// 图标选择器中列出的方块（按游戏创造模式物品栏的大致顺序）
    public static readonly string[] Picker =
    {
        "Grass", "block:dirt", "block:podzol", "block:mycelium", "block:stone", "block:cobblestone", "block:mossy_cobblestone",
        "block:deepslate", "block:sand", "block:gravel", "block:oak_log", "block:birch_log", "block:cherry_log", "block:oak_planks",
        "block:cherry_planks", "block:bricks", "block:stone_bricks", "block:moss_block", "block:snow", "block:ice", "block:packed_ice",
        "block:crafting_table", "block:furnace", "block:bookshelf", "block:jukebox", "block:note_block", "block:barrel", "block:tnt",
        "block:pumpkin", "block:melon", "block:hay_block", "block:honey_block", "block:slime_block", "block:glowstone",
        "block:sea_lantern", "block:amethyst_block", "block:iron_block", "block:gold_block", "block:diamond_block",
        "block:emerald_block", "block:lapis_block", "block:redstone_block", "block:copper_block", "block:netherite_block",
        "block:diamond_ore", "block:ancient_debris", "block:obsidian", "block:crying_obsidian", "block:netherrack",
        "block:soul_sand", "block:end_stone", "block:purpur_block", "block:prismarine", "block:sculk",
        "block:command_block", "block:anvil", "block:smithing_table", "block:loom",
    };

    public event Action? Changed;
    public bool HasTextures { get { lock (_gate) return _faces.Count > 0; } }
    private readonly Dictionary<string, Faces> _faces = new();
    private readonly HashSet<string> _missing = new();
    private readonly object _gate = new();
    private List<string> _jars = new();

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(Paths.Cache, "block-textures");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// 从缓存或本机的游戏客户端中加载内置图标的材质
    public async Task Load(IReadOnlyList<McInstance> instances)
    {
        // 优先使用较新的版本（1.13 起材质路径为 textures/block/）
        _jars = instances
            .Where(i => i.Kind != McInstance.Category.Error)
            .OrderByDescending(i => i.ReleaseTime)
            .Select(i => i.ClientJar)
            .Where(File.Exists)
            .ToList();
        var kinds = Specs.Keys.ToList();
        var custom = instances.Select(i => i.Setup.Icon).Where(icon => icon != null && icon.StartsWith("block:")).Select(icon => icon!).ToList();
        await Ensure(kinds.Concat(custom).ToList());
    }

    /// 确保这些图标的材质已加载（自定义方块按需从游戏文件中读取）
    public async Task Ensure(IReadOnlyList<string> kinds)
    {
        var todo = kinds.Distinct().Where(k => !_faces.ContainsKey(k) && !_missing.Contains(k)
            && (Specs.ContainsKey(k) || k.StartsWith("block:"))).ToList();
        if (todo.Count == 0) return;
        var result = await Task.Run(() => LoadSync(todo));
        lock (_gate)
        {
            foreach (var (kind, faces) in result.Loaded) _faces[kind] = faces;
            if (_jars.Count > 0) _missing.UnionWith(result.NotFound);
        }
        Changed?.Invoke();
    }

    private (Dictionary<string, Faces> Loaded, List<string> NotFound) LoadSync(List<string> todo)
    {
        // 每个图标的候选材质
        var candidates = new Dictionary<string, string[][]>();
        foreach (var kind in todo)
        {
            if (Specs.TryGetValue(kind, out var s))
            {
                candidates[kind] = new[] { s.Top, s.Left, s.Right };
            }
            else
            {
                var n = kind["block:".Length..].ToLowerInvariant();
                candidates[kind] = new[]
                {
                    new[] { $"{n}_top", n, $"{n}_side" },
                    new[] { $"{n}_front", $"{n}_side", n },
                    new[] { $"{n}_side", n },
                };
            }
        }
        var names = candidates.Values.SelectMany(c => c.SelectMany(x => x)).ToHashSet();
        var cacheDir = CacheDir;
        // 缓存中没有的，从客户端 jar 中一次性解压
        var absent = names.Where(n => !File.Exists(Path.Combine(cacheDir, n + ".png"))).ToList();
        if (absent.Count > 0 && _jars.Count > 0)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(_jars[0]);
                foreach (var n in absent)
                {
                    var entry = zip.GetEntry($"assets/minecraft/textures/block/{n}.png");
                    if (entry == null) continue;
                    entry.ExtractToFile(Path.Combine(cacheDir, n + ".png"), overwrite: true);
                }
            }
            catch { }
        }
        var images = new Dictionary<string, (byte[] Px, int W, int H)>();
        foreach (var n in names)
        {
            var img = DecodePng(Path.Combine(cacheDir, n + ".png"));
            if (img != null) images[n] = img.Value;
        }
        var loaded = new Dictionary<string, Faces>();
        var notFound = new List<string>();
        foreach (var (kind, c) in candidates)
        {
            var t = SelectFirst(c[0], images);
            var l = SelectFirst(c[1], images);
            var r = SelectFirst(c[2], images);
            if (t == null || l == null || r == null) { notFound.Add(kind); continue; }
            if (Specs.TryGetValue(kind, out var spec) && spec.Tint is { } tint)
                t = Tint(t.Value, tint);
            loaded[kind] = new Faces(t.Value.Px, t.Value.W, t.Value.H, l.Value.Px, l.Value.W, l.Value.H, r.Value.Px, r.Value.W, r.Value.H);
        }
        return (loaded, notFound);
    }

    private static (byte[] Px, int W, int H)? SelectFirst(string[] names, Dictionary<string, (byte[] Px, int W, int H)> images)
    {
        foreach (var n in names)
            if (images.TryGetValue(n, out var img)) return img;
        return null;
    }

    public Faces? GetTexture(string kind)
    {
        lock (_gate) return _faces.GetValueOrDefault(kind);
    }

    public bool Exists(string kind) => GetTexture(kind) != null;

    /// 解码 PNG → BGRA 像素（动画材质只取第一帧）
    private static (byte[] Px, int W, int H)? DecodePng(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fs.AsRandomAccessStream()).AsTask().GetAwaiter().GetResult();
            var data = decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, 0).AsTask().GetAwaiter().GetResult();
            var px = data.DetachPixelData()!;
            var w = (int)decoder.PixelWidth;
            var h = (int)decoder.PixelHeight;
            if (h > w)
            {
                // 纵向排列的多帧，只取第一帧
                var first = new byte[w * w * 4];
                for (var y = 0; y < w; y++)
                    Array.Copy(px, y * w * 4, first, y * w * 4, w * 4);
                return (first, w, w);
            }
            return (px, w, h);
        }
        catch { return null; }
    }

    /// 按生物群系颜色给灰度材质着色（与游戏相同的正片叠底）
    private static (byte[] Px, int W, int H) Tint((byte[] Px, int W, int H) img, uint color)
    {
        var tr = (int)((color >> 16) & 0xFF);
        var tg = (int)((color >> 8) & 0xFF);
        var tb = (int)(color & 0xFF);
        var px = (byte[])img.Px.Clone();
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte)(px[i] * tb / 255);
            px[i + 1] = (byte)(px[i + 1] * tg / 255);
            px[i + 2] = (byte)(px[i + 2] * tr / 255);
        }
        return (px, img.W, img.H);
    }
}




