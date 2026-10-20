namespace Kiln.Core.DotScene;

// 1.21 之后的小版本（Game Drop）与 2026 年的版本（对照原版 DotSceneDrops.swift）
//
// 1.21.x 每个小版本都是一次独立的更新（苍白之园、春意盎然、追逐天空、铜器时代、坐骑乱斗），各自一个场景；
// 画法与 DotSceneVersions.swift 相同：先查 minecraft.wiki 的渲染图和贴图，按真实尺寸（1 格 = 16 点）画。

/// 1.21.4 The Garden Awakens：苍白之园里的嘎枝
///
/// 1.21.4 加入了苍白之园：深灰的苍白橡木、垂下来的苍白垂须、地上的苍白苔藓、夜里睁开的眼眸花，还有嘎枝与嘎枝之心。
/// 起雾的夜里：左边大树里嵌着嘎枝之心，一明一暗；嘎枝只在玩家没看着它时才动——玩家一回头，它就往前挪几步，
/// 玩家转回来，它立刻僵住。挨到跟前时玩家打它一下，嘎枝化成一串橙色的树脂粒子飞回心脏，再从树边冒出来
public sealed class GardenAwakens : IDotScene
{
    public string Id => "1.21.4";
    public uint Background => 0x121418u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);
    private int HeartTree(DotBuffer b) => (int)(b.Cols * 0.06);
    private int HeartY(DotBuffer b) => Ground(b) - 40;

    private static readonly uint[] Bark = { 0x5A5250u, 0x4A4240u, 0x3E3634u, 0x6A625Eu };
    private static readonly uint[] PaleLeaves = { 0x5A645Au, 0x6E786Eu, 0x8A948Au, 0xA4ACA2u };

    /// 嘎枝（朝右略侧，16×44，约 2.7 格高）：深灰褐的树皮身子，头顶一圈枝杈，脸上几道橙色的眼光，
    /// 头顶和肩上各一块苍白的树皮，细长的手臂垂到膝盖，脚是散开的根。awake 时眼睛发亮；step 为迈步（0 或 1）
    public static Sprite Creaking(bool awake, int step)
    {
        var bark = new uint[] { 0x483C3Cu, 0x3C3030u, 0x544848u, 0x302424u };
        var eye = awake ? 0xFF8A1Eu : 0x6A4630u;
        var twigs = new (int Tx, int Top)[] { (3, 2), (5, 0), (7, 3), (9, 1), (11, 0), (12, 3) };
        return new Sprite(16, 44, (x, y) =>
        {
            foreach (var (tx, top) in twigs)
                if (x == tx && y >= top && y < 6) return 0x302424u;
            if (y == 1 && (x == 4 || x == 10)) return 0x3C3030u;
            // 头：8 宽 10 高
            if (x is >= 4 and <= 11 && y is >= 6 and <= 15)
            {
                if ((y == 9 && x is >= 5 and <= 6) || (y == 12 && x is >= 5 and <= 6) || (y == 10 && x is >= 9 and <= 10)) return eye;
                if (y <= 7 && x is >= 8 and <= 10) return 0xC8C0B8u;
                return DotMath.Pick(bark, x, y, 3);
            }
            // 身子：细，肩上一块苍白的树皮，偶尔带一点苔
            if (x is >= 5 and <= 10 && y is >= 16 and <= 29)
            {
                if (y is >= 17 and <= 19 && x is >= 9 and <= 10) return 0xD8D0C8u;
                if (DotMath.Noise(x, y, 4) < 0.12) return 0x545448u;
                return DotMath.Pick(bark, x, y, 5);
            }
            // 细长的手臂，垂到膝盖，指头是几根小枝
            if (y is >= 16 and <= 33 && (x == 3 || x == 4 || x == 11 || x == 12))
            {
                var c = DotMath.Pick(bark, x, y, 6);
                return x == 3 || x == 12 ? DotMath.Shade(c, 0.78) : c;
            }
            if (y == 34 && (x == 2 || x == 4 || x == 11 || x == 13)) return 0x302424u;
            // 两条腿，迈步时一条抬起一点；脚底是散开的根
            foreach (var (leg, c0) in new (int Leg, int C0)[] { (0, 5), (1, 9) })
            {
                var bottom = 43 - (leg == step ? 2 : 0);
                if (x >= c0 && x <= c0 + 1 && y >= 30 && y <= bottom - 2) return DotMath.Pick(bark, x, y, 7 + leg);
                if (y >= bottom - 1 && y <= bottom && x >= c0 - 1 && x <= c0 + 2)
                {
                    if (y == bottom && (x == c0 || x == c0 + 1)) return null;
                    return 0x302424u;
                }
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x2A2E34u, 0x343A40u, 0x40464Cu, 0x50565Au, 0x62686Au });
        // 远处雾里的树：越远越淡
        for (var i = 0; i < 9; i++)
        {
            var x = (int)(DotMath.Noise(i, 0, 3) * b.Cols); var w = 6 + (int)(DotMath.Noise(i, 1, 3) * 6);
            for (var y = 0; y < g; y++)
                for (var dx = 0; dx < w; dx++)
                    if (DotMath.Dither(x + dx, y, 0.6)) b.Set(x + dx, y, 0x3A3E44u);
        }
        // 近处的苍白橡木：粗树干（树皮有竖纹），顶上一片灰绿的树冠
        var trunks = new (double Cx, int W)[] { (0.06, 24), (0.42, 16), (0.93, 20) };
        for (var k = 0; k < trunks.Length; k++)
        {
            var (cx, w) = trunks[k];
            var x0 = (int)(b.Cols * cx) - w / 2;
            for (var y = 0; y < g; y++)
                for (var dx = 0; dx < w; dx++)
                {
                    var c = DotMath.Pick(Bark, (x0 + dx) / 2, y / 3, 4 + k);
                    if (dx == 0 || dx == w - 1) { c = DotMath.Shade(c, 0.75); }
                    b.Set(x0 + dx, y, c);
                }
            // 树根往两边散开
            for (var r = 0; r < 4; r++) { b.Fill(x0 - 3 + r, g - 4 + r, 2, 1, Bark[2]); b.Fill(x0 + w + 1 - r, g - 4 + r, 2, 1, Bark[2]); }
        }
        // 树冠：盖住画面顶上
        var canopy = (int)(g * 0.24);
        for (var x = 0; x < b.Cols; x++)
        {
            var h = canopy + (int)(Math.Sin(x * 0.09) * 5 + DotMath.Noise(x / 4, 0, 6) * 7);
            for (var y = 0; y < h; y++)
            {
                var c = DotMath.DitherRamp(PaleLeaves, 1 - (double)y / h * 0.9, x, y);
                b.Set(x, y, DotMath.Noise(x, y, 7) < 0.1 ? DotMath.Shade(c, 0.8) : c);
            }
            // 苍白垂须：从树冠垂下来，长短不一
            if (DotMath.Noise(x, 1, 8) > 0.82)
            {
                var len = 6 + (int)(DotMath.Noise(x, 2, 8) * 22);
                for (var y = h; y < h + len; y++)
                    if (DotMath.Noise(x, y, 9) > 0.15) b.Set(x, y, DotMath.Pick(new uint[] { 0x8A948Au, 0x6E786Eu, 0xA4ACA2u }, x, y, 10));
            }
        }
        // 嘎枝之心：嵌在左边的树干里，暗时是一块深色的树心，中间一道竖缝（亮光在动态层）
        var hx = HeartTree(b) - 6; var hy = HeartY(b);
        for (var y = 0; y < 12; y++)
            for (var x = 0; x < 12; x++)
            {
                var edge = x == 0 || y == 0 || x == 11 || y == 11;
                b.Set(hx + x, hy + y, edge ? 0x241818u : DotMath.Pick(new uint[] { 0x302424u, 0x3C3024u, 0x544848u, 0x241818u }, x, y, 11));
            }
        // 地面：苍白苔藓 → 泥土 → 深层
        Paint.Strata(b, g, new uint[] { 0x6E786Eu, 0x86907Eu, 0x5E685Eu, 0x9AA294u }, 5,
            new uint[] { 0x4A3A2Eu, 0x3E3026u, 0x54422Fu }, 26, new uint[] { 0x3A3A3Eu, 0x2E2E32u }, 12);
        // 眼眸花的茎和叶（花在动态层）
        for (var x = 14; x < b.Cols - 4; x++)
            if (DotMath.Noise(x, 0, 13) > 0.9)
            {
                b.Fill(x, g - 4, 1, 4, 0x3C3030u);
                b.Set(x - 1, g - 2, 0x544848u); b.Set(x + 1, g - 3, 0x544848u);
            }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        var px = (int)(b.Cols * 0.76);
        // 夜里睁开的眼眸花：灰色花瓣中间一点橙光，一明一暗
        for (var x = 14; x < b.Cols - 4; x++)
            if (DotMath.Noise(x, 0, 13) > 0.9)
            {
                var glow = Math.Sin(t * 1.6 + x * 0.7) > 0 ? 0xFF9A2Eu : 0xC8642Au;
                b.Set(x - 1, g - 6, 0x9C9090u); b.Set(x + 1, g - 6, 0x9C9090u); b.Set(x, g - 7, 0xA89CA8u);
                b.Set(x, g - 6, glow); b.Set(x, g - 5, 0x786C78u);
            }
        // 地面上缓缓飘的雾
        for (var y = g - 16; y < g + 2; y++)
        {
            var k = 1 - Math.Abs(y - (g - 6)) / 10.0;
            if (k <= 0) continue;
            for (var x = 0; x < b.Cols; x++)
                if (DotMath.Noise((x + (int)(t * 3)) / 5, y / 2, 21) * k > 0.42 && DotMath.Dither(x, y, 0.35)) b.Set(x, y, 0x7A8282u);
        }

        // 时间线（12 秒一轮）
        var p = Paint.Mod(t, 12);
        var a = (double)(HeartTree(b) + 16); var c = (double)(px - 26); var m = (a + c) / 2;
        var cx = a;
        var moving = false;
        if (p < 2) cx = a;
        else if (p < 3.6) { cx = a + (m - a) * (p - 2) / 1.6; moving = true; }
        else if (p < 5.5) cx = m;
        else if (p < 7) { cx = m + (c - m) * (p - 5.5) / 1.5; moving = true; }
        else if (p < 11.4) cx = c;
        else cx = a;   // 粒子飞回心脏以后，从树边重新冒出来
        // 嘎枝之心：嘎枝在外面时中间那道缝发橙光，粒子飞回来时更亮
        var hx = HeartTree(b) - 6; var hy = HeartY(b);
        var returning = p is >= 9.4 and <= 11.4;
        var beat = returning ? 1.0 : (Math.Sin(t * 2.2) > 0.2 ? 0.7 : 0.35);
        for (var y = 2; y < 10; y++)
        {
            uint col = beat > 0.8 ? 0xFFC04Au : (beat > 0.5 ? 0xFF8A1Eu : 0xA8501Eu);
            b.Set(hx + 5, hy + y, col); b.Set(hx + 6, hy + y, y % 3 == 0 ? DotMath.Shade(col, 0.8) : col);
        }
        // 嘎枝：看着它时僵住；被打以后化成树脂粒子飞回心脏
        if (p < 9.4 || p > 11.4)
        {
            var sprite = Creaking(!moving, moving ? (int)(t * 5) % 2 : 0);
            if (p is >= 8.6 and <= 9.4)
            {
                // 挨打：闪白
                var k = 1 - (p - 8.6) / 0.8;
                var src = sprite;
                sprite = new Sprite(src.Width, src.Height, (x, y) =>
                {
                    var v = src.Px[y * src.Width + x];
                    return v == 0 ? null : (DotMath.Dither(x, y, k * 0.6) ? 0xF4E8D8u : v & 0xFFFFFFu);
                });
            }
            b.Blit(sprite, (int)cx, g - 1);
        }
        if (returning)
        {
            var q = (p - 9.4) / 2;
            for (var i = 0; i < 16; i++)
            {
                var f = Math.Max(0, Math.Min(1, q * 1.6 - i * 0.04));
                if (!(f > 0 && f < 1)) continue;
                var x = c + 8 + ((hx + 6) - c - 8) * f;
                var y = (double)(g - 22) + ((hy + 6) - (double)(g - 22)) * f - Math.Sin(f * Math.PI) * 14 + Math.Sin(i * 2.1) * 2;
                b.Set((int)x, (int)y, i % 3 == 0 ? 0xFFC04Au : 0xFF8A1Eu);
            }
        }
        // 玩家：不看嘎枝的时候它才会动；挨到跟前时挥手打它一下
        var pose = Player.Idle(t, new PlayerProps(), 3);
        pose.LookBack = (p is >= 2 and <= 3.6) || (p is >= 5.5 and <= 7);
        if (p is >= 8.2 and <= 9.2)
        {
            var k = (p - 8.2) / 1.0;
            pose.ArmAngle = k < 0.4 ? 1.8 * k / 0.4 : 1.8 * (1 - (k - 0.4) / 0.6);
        }
        Player.Draw(b, cast.Player, null, pose, px, g - 1, t);
    }
}

/// 1.21.5 Spring to Life：萤火虫与新的动物变种
///
/// 1.21.5 让主世界更有生气：萤火虫灌木、野花、落叶堆、会飘落的树叶，还有按气候区分的猪、牛、鸡变种。
/// 黄昏的森林边上：萤火虫在灌木丛边一闪一闪，树叶慢慢飘落；温暖型的鸡在啄地、时不时下一颗棕色的蛋，
/// 寒冷型的猪（一身奶油色的卷毛）慢悠悠地走来走去；玩家手里捧着一束野花
public sealed class SpringToLife : IDotScene
{
    public string Id => "1.21.5";
    public uint Background => 0x2A2850u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 温暖型的鸡（朝右，12×12，约 0.7 格高）：橙黄的身子、棕色的鸡冠和喙、红色的肉垂；peck 时低头
    public static Sprite WarmChicken(bool peck)
    {
        var body = new uint[] { 0xE8A848u, 0xD89438u, 0xF0BC5Cu };
        return new Sprite(13, 12, (x, y) =>
        {
            // 身子 8 长 6 高，尾巴翘起一点
            if (x is >= 0 and <= 7 && y is >= 4 and <= 9)
            {
                if (y == 9 || x == 0) return 0xB87428u;
                if (x is >= 2 and <= 5 && y is >= 6 and <= 7) return 0xC88430u;   // 翅膀
                return DotMath.Pick(body, x, y, 3);
            }
            if (x == 0 && y == 3) return 0xB87428u;
            // 头：4 宽 6 高，低头时整个往下、往前
            var hy = peck ? y - 3 : y; var hx = peck ? x - 1 : x;
            if (hx is >= 6 and <= 9 && hy is >= 0 and <= 5)
            {
                if (hy == 2 && hx == 8) return 0x1E1E1Du;
                return hy == 0 ? 0xF4D88Au : 0xF0C878u;
            }
            if (hx is >= 10 and <= 11 && hy is >= 2 and <= 3) return 0x6A3A2Au;   // 喙
            if (hx == 10 && hy is >= 4 and <= 5) return 0xD8341Fu;                 // 肉垂
            // 腿
            if ((x == 3 || x == 5) && y is >= 10 and <= 11) return 0xA06A50u;
            if (y == 11 && (x == 2 || x == 6)) return 0xA06A50u;
            return null;
        });
    }

    /// 寒冷型的猪（朝右，18×12）：奶油色的卷毛，头上的毛垂到眼睛，粉褐色的鼻子和蹄子；step 为迈步
    public static Sprite ColdPig(int step)
    {
        var wool = new uint[] { 0xF0DCB0u, 0xE4CC9Cu, 0xF8E8C4u, 0xD8BC88u };
        return new Sprite(18, 12, (x, y) =>
        {
            if (x is >= 0 and <= 11 && y is >= 1 and <= 8)
            {
                if (y == 8) return 0xC8AC78u;
                return DotMath.Pick(wool, x / 2, y, 4);
            }
            // 头 6×7，鼻子往前凸
            if (x is >= 11 and <= 16 && y is >= 0 and <= 6)
            {
                if (y == 3 && x == 14) return 0x1E1E1Du;
                if (y <= 2) return DotMath.Pick(wool, x, y, 5);
                return 0xE8C8A0u;
            }
            if (x == 17 && y is >= 4 and <= 5) return 0xB07A68u;
            if (x == 16 && y is >= 4 and <= 5) return 0xC8907Au;
            // 四条腿，迈步时前后两对交替抬起
            var legs = new[] { 1, 4, 8, 11 };
            for (var i = 0; i < legs.Length; i++)
            {
                var lx = legs[i];
                if (lx <= x && x <= lx + 1)
                {
                    var up = (i % 2 == step) ? 1 : 0;
                    if (y >= 9 && y <= 11 - up) return y == 11 - up ? 0x8A5A48u : 0xD8BC88u;
                }
            }
            return null;
        });
    }

    /// 一小束野花（拿在手里，不转）
    private static readonly HeldItem Bouquet = new(
        new Sprite(new[]
        {
            ".y.w.", "yOyWw", ".yoW.", "..g..", ".gGg.", "..G..", "..G..",
        }, new Dictionary<char, uint> { ['y'] = 0xFCCC30u, ['O'] = 0xF0B424u, ['o'] = 0xE49C24u, ['w'] = 0xF0FCFCu, ['W'] = 0xCCE4E4u, ['g'] = 0x48783Cu, ['G'] = 0x3C6C30u }),
        swings: false);

    /// 萤火虫灌木（14×12）：橄榄绿的叶子，叶尖发黄
    private static readonly Sprite FireflyBush = new(14, 12, (x, y) =>
    {
        var r = Math.Pow(x - 6.5, 2) / 49 + Math.Pow(y - 12, 2) / 150;
        if (!(r < 1) || !(DotMath.Noise(x, y, 31) > 0.18)) return null;
        return y < 4 && DotMath.Noise(x, y, 32) > 0.5 ? 0x9C7830u : DotMath.Pick(new uint[] { 0x3C4824u, 0x545430u, 0x6C6C3Cu }, x, y, 33);
    });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x2A2858u, 0x4A3A78u, 0x8A5A8Au, 0xD8786Au, 0xF4A462u, 0xF8D088u });
        // 快落山的太阳
        var sx = (int)(b.Cols * 0.52); var sy = g - 14;
        for (var y = sy - 12; y <= sy + 12; y++)
            for (var x = sx - 12; x <= sx + 12; x++)
                if (Math.Abs(x - sx) <= 12 && Math.Abs(y - sy) <= 12)
                    b.Set(x, y, Math.Abs(x - sx) > 9 || Math.Abs(y - sy) > 9 ? 0xFCC86Au : 0xFFE89Au);
        // 远处的丘陵剪影
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.025 + 1) * 0.5 + 0.5) * 14 + DotMath.Noise(x / 7, 0, 2) * 5) + 4;
            for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Dither(x, y, 0.5) ? 0x5A4A6Au : 0x6A5070u);
        }
        // 橡树：树干和树冠，树下是落叶堆
        var oaks = new[] { 0.05, 0.36, 0.95 };
        for (var k = 0; k < oaks.Length; k++)
        {
            var cx = oaks[k];
            var x = (int)(b.Cols * cx); var h = 40 + k * 4;
            b.Fill(x - 2, g - h, 5, h, 0x6B5133u);
            for (var y = g - h; y < g; y += 4) b.Set(x - 1 + (y / 4) % 3, y, 0x5A4228u);
            for (var dy = -22; dy <= 4; dy++)
                for (var dx = -16; dx <= 16; dx++)
                {
                    var r = (double)(dx * dx) / 256 + (double)(dy * dy) / 200;
                    if (!(r < 1) || !(DotMath.Noise(x + dx, dy, 34 + k) > 0.1)) continue;
                    var c = DotMath.DitherRamp(new uint[] { 0x2E5A22u, 0x3E7A2Cu, 0x4A8A34u, 0x6AA040u }, 1 - r * 0.8 - (double)(dy + 22) / 80, x + dx, dy);
                    b.Set(x + dx, g - h + dy, c);
                }
            for (var dx = -14; dx <= 14; dx++)
                if (DotMath.Noise(x + dx, 0, 36) > 0.35) b.Set(x + dx, g - 1, DotMath.Pick(new uint[] { 0x604824u, 0x785430u, 0x603C24u }, dx, k, 37));
        }
        // 草地 → 泥土；地上一丛丛野花
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u, 0x76B850u }, 4,
            new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30, new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, 38);
        for (var x = 0; x < b.Cols; x++)
            if (DotMath.Noise(x, 0, 39) > 0.72)
            {
                b.Set(x, g - 1, 0x48783Cu);
                b.Set(x, g - 2, DotMath.Pick(new uint[] { 0xFCCC30u, 0xF0FCFCu, 0xE49C24u, 0xFCCC30u }, x, 0, 40));
            }
        // 萤火虫灌木
        foreach (var cx in new[] { 0.18, 0.47, 0.64 }) b.Blit(FireflyBush, (int)(b.Cols * cx), g - 1);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 萤火虫：在灌木附近绕小圈，忽明忽暗
        var homes = new[] { 0.18, 0.47, 0.64, 0.3, 0.84 };
        for (var i = 0; i < 48; i++)
        {
            var home = homes[i % 5];
            var x = (int)(b.Cols * home) + 7 + (int)(Math.Sin(t * (0.5 + DotMath.Noise(i, 0, 41) * 0.6) + i) * (8 + DotMath.Noise(i, 1, 41) * 14));
            var y = g - 6 - (int)(DotMath.Noise(i, 2, 41) * 34) + (int)(Math.Cos(t * 0.7 + i * 1.3) * 5);
            var on = Math.Sin(t * (2 + DotMath.Noise(i, 3, 41) * 2) + i * 2.3);
            if (on > 0.1) { b.Set(x, y, on > 0.6 ? 0xFCFFA0u : 0xD8F05Au); }
            if (on > 0.75) { b.Set(x + 1, y, 0xE8F870u); b.Set(x, y - 1, 0xE8F870u); }
        }
        // 树叶慢慢飘落
        Paint.Falling(b, t, 14, new uint[] { 0x4A8A34u, 0x6AA040u, 0x785430u }, 10, g, 0.035, 42);

        // 温暖型的鸡：啄地，每 9 秒下一颗蛋
        var cx = (int)(b.Cols * 0.1);
        var peck = Math.Sin(t * 2.6) > 0.55;
        var p = Paint.Mod(t, 9);
        var hop = (p is >= 6.0 and <= 6.3) ? 2 : 0;
        b.Blit(WarmChicken(peck), cx, g - 1 - hop);
        if (p > 6.2)
        {
            // 棕色的蛋：落下来停在鸡屁股后面
            var ey = Math.Min(g - 1, g - 6 + (int)((p - 6.2) * 20));
            b.Blit(new Sprite(new[] { ".oo.", "oOOo", "oOOo", ".oo." }, new Dictionary<char, uint> { ['o'] = 0x9A6A3Au, ['O'] = 0xC08A54u }), cx - 4, ey);
        }

        // 寒冷型的猪：在中间慢慢踱步，走到头掉头
        var span = b.Cols * 0.2; var x0 = b.Cols * 0.26;
        var q = Paint.Mod(t * 0.06, 2);
        var forward = q < 1;
        var walk = forward ? q : 2 - q;
        var pigX = x0 + span * walk;
        var pause = Math.Abs(Math.Sin(t * 0.6)) < 0.25;
        b.Blit(ColdPig(pause ? 2 : (int)(t * 4) % 2), (int)pigX, g - 1, flip: !forward);

        var props = new PlayerProps { Held = Bouquet };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 5), (int)(b.Cols * 0.76), g - 1, t);
    }
}

/// 1.21.6 Chase the Skies：骑着快乐恶魂飞
///
/// 1.21.6 加入了快乐恶魂（4 格见方的白色恶魂，性情温顺，能载 4 个人）、恶魂幼体、干枯的恶魂和挽具。
/// 云海上空：快乐恶魂戴着挽具飞，被人骑着时护目镜放下来盖住眼睛；玩家和作者站在它的背上，
/// 小小的恶魂幼体跟在后面，云一层层往后飘；玩家时不时一拳把作者打下恶魂，作者过一会儿又从天上掉回来
public sealed class ChaseTheSkies : IDotScene
{
    public string Id => "1.21.6";
    public uint Background => 0x5288E6u;

    private int CloudTop(DotBuffer b) => (int)(b.Rows * 0.8);

    /// 快乐恶魂（正面，64×64 加触手；贴图 16×16 放大 4 倍，每个贴图像素占 4×4 个点）：
    /// 白色身子带几道浅灰的裂纹，弯弯的嘴在笑；挽具在头顶是一块棕色的皮鞍，两侧垂下白色的带子（中间一道橙线），
    /// 被骑着时护目镜放下来盖住眼睛。wiggle 让触手轮流摆动
    public static Sprite HappyGhast(double t)
    {
        var white = new uint[] { 0xF4F4F4u, 0xECECECu, 0xF8F8F8u, 0xE4E4E4u };
        uint? Texel(int u, int v)
        {
            // 护目镜（放下来）：两个深棕色的镜框，浅蓝的镜片
            foreach (var gx in new[] { 2, 9 })
                if (u >= gx && u <= gx + 4 && v is >= 5 and <= 9)
                {
                    var frame = u == gx || u == gx + 4 || v == 5 || v == 9;
                    if (frame) return 0x3C240Cu;
                    return (u == gx + 1 && v == 6) ? 0xD8F4FCu : 0x9CD8E8u;
                }
            if (u is >= 7 and <= 8 && v == 7) return 0x543018u;
            if ((u <= 1 || u >= 14) && v == 7) return 0x543018u;
            // 笑着的嘴
            if (v == 12 && u is >= 6 and <= 9) return 0x8A8A8Au;
            if (v == 11 && (u == 5 || u == 10)) return 0x8A8A8Au;
            // 两侧垂下的挽具带子
            if ((u == 0 || u == 15) && v <= 11) return v % 4 == 2 ? 0xE8A020u : 0xC8C8C8u;
            return null;
        }
        return new Sprite(64, 78, (x, y) =>
        {
            if (y < 64)
            {
                var u = x / 4; var v = y / 4;
                if (Texel(u, v) is { } c) return c;
                // 几道浅灰的裂纹
                var crack = (x - y / 2 == 40 && y is >= 8 and <= 30) || (x + y / 3 == 20 && y is >= 30 and <= 50);
                if (crack) return 0xC8C8C8u;
                var c2 = DotMath.Pick(white, u, v, 51);
                if (y >= 60 || x >= 60) { c2 = DotMath.Shade(c2, 0.86); }
                return c2;
            }
            // 触手：9 根里看得到 5 根，各自长短摆动
            var tents = new[] { 1, 4, 7, 10, 13 };
            for (var i = 0; i < tents.Length; i++)
            {
                var u = tents[i];
                if (u * 4 <= x && x <= u * 4 + 5)
                {
                    var len = 6 + (int)((Math.Sin(t * 2.2 + i * 1.4) * 0.5 + 0.5) * 8);
                    if (y - 64 < len) return y - 64 == len - 1 ? 0xC8C8C8u : (x == u * 4 + 5 ? 0xD8D8D8u : 0xEAEAEAu);
                }
            }
            return null;
        });
    }

    /// 快乐恶魂背上的皮鞍（64×5）
    private static readonly Sprite Saddle = new(64, 5, (x, y) =>
    {
        if (x < 2 || x > 61) return y >= 2 ? 0x543018u : null;
        if (y == 0) return x % 6 == 0 ? 0x6C3C24u : 0x8A5A30u;
        return y == 4 ? 0x543018u : (DotMath.Noise(x, y, 52) < 0.2 ? 0x6C3C24u : 0x784824u);
    });

    /// 恶魂幼体（16×16 加触手）：白色的小方块，闭着眼睛在笑
    public static Sprite Ghastling(double t)
    {
        return new Sprite(16, 22, (x, y) =>
        {
            if (y < 16)
            {
                if (y == 7 && (x is >= 3 and <= 5 || x is >= 10 and <= 12)) return 0x8A8A8Au;
                if (y == 11 && x is >= 6 and <= 9) return 0x8A8A8Au;
                if (y == 10 && (x == 5 || x == 10)) return 0x8A8A8Au;
                if ((y == 9 || y == 10) && x == 14) return 0xC84A4Au;
                return y == 15 || x == 15 ? 0xDCDCDCu : DotMath.Pick(new uint[] { 0xF4F4F4u, 0xECECECu }, x, y, 53);
            }
            var tents = new[] { 2, 7, 12 };
            for (var i = 0; i < tents.Length; i++)
            {
                var u = tents[i];
                if (u <= x && x <= u + 1)
                {
                    var len = 3 + (int)((Math.Sin(t * 3 + i * 2) * 0.5 + 0.5) * 3);
                    if (y - 16 < len) return 0xE4E4E4u;
                }
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var top = CloudTop(b);
        Paint.Sky(b, b.Rows, new uint[] { 0x3A6ED8u, 0x5288E6u, 0x74A4F0u, 0xA0C4F8u, 0xD4E6FCu });
        // 远处的云海：顶上一个个鼓包
        for (var x = 0; x < b.Cols; x++)
        {
            var bump = (int)(Math.Abs(Math.Sin(x * 0.07)) * 7 + Math.Abs(Math.Sin(x * 0.19 + 1)) * 4 + DotMath.Noise(x / 5, 0, 54) * 3);
            for (var y = top - bump; y < b.Rows; y++)
            {
                var d = y - (top - bump);
                uint c = d < 2 ? 0xFFFFFFu : (d < 8 ? 0xEEF4FEu : (DotMath.Dither(x, y, 0.5) ? 0xD4E2F8u : 0xE2ECFAu));
                b.Set(x, y, c);
            }
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        // 两层云往后飘，近的快、远的慢，看起来在往前飞
        Paint.Clouds(b, t * 3, (int)(b.Rows * 0.3), 4, 0xF4F8FFu, 0xD5E1FBu, 55);
        Paint.Clouds(b, t * 9, (int)(b.Rows * 0.75), 3, 0xFFFFFFu, 0xC8D8F0u, 56);
        // 风：几道很快的白线
        for (var i = 0; i < 7; i++)
        {
            var y = 10 + (int)(DotMath.Noise(i, 0, 57) * (b.Rows - 30));
            var span = (double)(b.Cols + 40);
            var x = (int)(span - Paint.Mod(t * 120 + DotMath.Noise(i, 1, 57) * span, span)) - 20;
            for (var dx = 0; dx < 8 + i % 3 * 4; dx++)
                if (DotMath.Dither(x + dx, y, 0.7)) b.Set(x + dx, y, 0xFFFFFFu);
        }
        // 快乐恶魂：上下轻轻浮动
        var bob = (int)Math.Round(Math.Sin(t * 1.1) * 3);
        var gx = (int)(b.Cols * 0.5) - 32; var top = (int)(b.Rows * 0.36) + bob;
        b.Blit(HappyGhast(t), gx, top + 77);
        b.Blit(Saddle, gx, top + 2);
        // 恶魂幼体跟在后面
        var lb = (int)Math.Round(Math.Sin(t * 1.5 + 1) * 4);
        b.Blit(Ghastling(t), (int)(b.Cols * 0.14), top + 60 + lb);
        // 作者和玩家站在背上（10 秒一轮）：玩家走过去一拳把作者打下去，作者翻下恶魂、穿过云层掉下去，
        // 过一会儿又从天上掉回鞍上，弹一下
        var p = Paint.Mod(t, 10);
        var stand = top - 3;
        var me = Player.Idle(t, new PlayerProps(), 2);
        var approach = DotMath.Ramp(p, 1.6, 2.4) * (1 - DotMath.Ramp(p, 4.2, 5.2));
        me.Dx = -(int)(8 * approach);
        if ((p is >= 1.6 and <= 2.4) || (p is >= 4.2 and <= 5.2)) { me.Walk = t * 9; }
        if (p is >= 2.5 and <= 3.0)
        {
            // 出拳：手往前一挥
            var q = (p - 2.5) / 0.5;
            me.ArmAngle = q < 0.4 ? 1.7 * q / 0.4 : 1.7 * (1 - (q - 0.4) / 0.6);
            me.LookBack = false;
        }
        var foe = Player.Idle(t, new PlayerProps(), 9);
        var foeX = (double)(gx + 14); var foeFeet = (double)stand;
        if (p is >= 2.75 and <= 6.5)
        {
            // 被打中：变红，往左后方飞出去，过了恶魂的边缘就往下掉
            var q = p - 2.75;
            foe.Hurt = Math.Max(0, 1 - q / 0.3);
            foe.ArmAngle = 2.6 + Math.Sin(t * 18) * 0.4;
            foeX -= q * 40;
            foeFeet = stand - 18 * q + 40 * q * q;
        }
        else if (p > 8.2)
        {
            // 从天上掉回来，落在鞍上弹一下
            var q = p - 8.2;
            foeFeet = q < 0.6 ? stand - (1 - q / 0.6) * (stand + 40) : stand - Math.Abs(Math.Sin((q - 0.6) * 9)) * 3 * Math.Max(0, 1 - (q - 0.6) / 0.8);
        }
        else if (p > 6.5)
        {
            foeFeet = -100;
        }
        if (foeFeet > -60 && foeFeet < b.Rows + 40)
        {
            Player.Draw(b, cast.Author, null, foe, (int)foeX, (int)foeFeet, t, true);
        }
        Player.Draw(b, cast.Player, null, me, gx + 40, stand, t);
    }
}

/// 1.21.9 The Copper Age：铜傀儡搬箱子
///
/// 1.21.9 加入了铜傀儡（会在铜箱子和普通箱子之间搬东西）、铜箱子、铜制工具与盔甲、铜火把（绿色火焰）和架子。
/// 一座铜顶的小仓库前：铜傀儡抱着东西在两个铜箱子之间来回搬，每趟都氧化一点（铜色 → 斑驳 → 锈蚀 → 氧化），
/// 完全氧化后变成雕像一动不动；穿着铜盔甲的玩家走过去用铜斧把锈刮掉，它又亮晶晶地动起来
public sealed class CopperAge : IDotScene
{
    public string Id => "1.21.9";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 铜的四个氧化阶段：亮、中、暗、深
    private static readonly uint[][] Stages =
    {
        new uint[] { 0xE08A6Cu, 0xC06A50u, 0x9C5440u, 0x7A3E30u },
        new uint[] { 0xB89070u, 0xA07A5Eu, 0x84644Cu, 0x6A4E3Cu },
        new uint[] { 0x7AA884u, 0x5E9070u, 0x4E7A60u, 0x3E6450u },
        new uint[] { 0x5ABE9Cu, 0x48A486u, 0x3A8A70u, 0x2E6E5Au },
    };

    /// 铜傀儡（正面，12×22，约 1 格高加一根天线）：大方头上是铜棒天线和小球，两只发光的眼睛、长长的鼻子，
    /// 小身子、短手短腿。statue 时眼睛不亮；carrying 为抱着的东西
    public static Sprite Golem(uint[] tones, bool statue, int step, uint? carrying)
    {
        return new Sprite(14, 22, (x, y) =>
        {
            var hi = tones[0]; var mid = tones[1]; var dark = tones[2]; var deep = tones[3];
            if (x is >= 5 and <= 6 && y is >= 0 and <= 1) return hi;
            if (x == 6 && y is >= 2 and <= 4) return mid;
            // 头 8×7
            if (x is >= 3 and <= 10 && y is >= 5 and <= 11)
            {
                if (y == 8 && (x == 4 || x == 9)) return statue ? deep : 0xF8D86Au;
                if (y == 8 && (x == 5 || x == 8)) return statue ? dark : 0xD8A84Au;
                if (x is >= 6 and <= 7 && y is >= 8 and <= 11) return x == 7 ? dark : mid;
                if (y == 5 || x == 3) return hi;
                return x == 10 ? dark : mid;
            }
            // 抱着的东西（挡在身前）
            if (carrying is { } item && x is >= 5 and <= 8 && y is >= 13 and <= 15) return y == 13 ? DotMath.Shade(item, 1.15) : item;
            // 身子与手
            if (x is >= 4 and <= 9 && y is >= 12 and <= 17) return y == 17 ? deep : (x == 4 ? hi : mid);
            if ((x == 2 || x == 3 || x == 10 || x == 11) && y >= 12 && y <= (carrying == null ? 16 : 14)) return x == 2 || x == 11 ? dark : mid;
            if (carrying != null && (x == 4 || x == 9) && y is >= 14 and <= 15) return dark;
            // 短腿，迈步时轮流抬起
            foreach (var (leg, c0) in new (int Leg, int C0)[] { (0, 4), (1, 7) })
                if (c0 <= x && x <= c0 + 2)
                {
                    var bottom = 21 - (leg == step ? 1 : 0);
                    if (y >= 18 && y <= bottom) return y == bottom ? deep : dark;
                }
            return null;
        });
    }

    /// 铜箱子（14×13）：铜色的箱体，盖子上一圈深色的边，中间一个灰色的锁扣；open 时盖子掀起来
    public static Sprite Chest(bool open)
    {
        var c = new uint[] { 0xC06048u, 0x9C543Cu, 0xD8786Cu, 0x904830u };
        return new Sprite(14, 16, (x, y) =>
        {
            var lidTop = open ? 0 : 3;
            if (y >= lidTop && y <= lidTop + 4)
            {
                if (y == lidTop || x == 0 || x == 13) return 0x783C30u;
                if (x is >= 6 and <= 7 && y >= lidTop + 2 && !open) return 0xB8B8B8u;
                return DotMath.Pick(c, x, y, 58);
            }
            if (open && y is >= 5 and <= 7 && x is >= 1 and <= 12) return 0x3A1C12u;
            if (y is >= 8 and <= 15)
            {
                if (y == 15 || x == 0 || x == 13 || y == 8) return 0x783C30u;
                if (x is >= 6 and <= 7 && y <= 10) return 0xB8B8B8u;
                return DotMath.Pick(c, x, y, 59);
            }
            return null;
        });
    }

    private static readonly HeldItem CopperAxe = new(
        new Sprite(new[]
        {
            ".hHH.", "hHHHH", "hHHh.", "hHw..", "..w..", "..W..", "..w..", "..W..", "..w..",
        }, new Dictionary<char, uint> { ['h'] = 0xC06048u, ['H'] = 0xE08A6Cu, ['w'] = 0x6B4A2Cu, ['W'] = 0x8A6238u }));

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u });
        // 仓库：切制铜块的墙，铜顶上立着避雷针；墙上是架子和窗户
        var x1 = (int)(b.Cols * 0.52); var wallTop = g - 58;
        for (var y = wallTop; y < g; y++)
            for (var x = 0; x < x1; x++)
            {
                var bx = x / 16; var by = (y - wallTop) / 16;
                var stage = 2 + (int)(DotMath.Noise(bx, by, 60) * 1.9);
                var tones = Stages[Math.Min(3, stage)];
                var lx = x % 8; var ly = (y - wallTop) % 8;
                var c = (lx == 0 || ly == 0) ? tones[1] : ((lx == 7 || ly == 7) ? tones[3] : tones[2]);
                if (lx == 1 && ly == 1) { c = tones[0]; }
                if (DotMath.Noise(x, y, 61) < 0.04) { c = Stages[1][2]; }
                b.Set(x, y, c);
            }
        // 屋檐和避雷针
        b.Fill(0, wallTop - 4, x1 + 6, 4, Stages[1][2]);
        b.Fill(0, wallTop - 5, x1 + 6, 1, Stages[1][0]);
        var rod = (int)(b.Cols * 0.3);
        b.Fill(rod, wallTop - 24, 2, 20, 0xC06A50u);
        b.Fill(rod - 1, wallTop - 26, 4, 3, 0xE08A6Cu);
        // 窗户
        foreach (var wx in new[] { 0.06, 0.38 })
        {
            var x = (int)(b.Cols * wx);
            b.Fill(x, wallTop + 10, 18, 14, 0x3A2A22u);
            b.Fill(x + 1, wallTop + 11, 16, 12, 0x9CC8E8u);
            b.Fill(x + 8, wallTop + 11, 1, 12, 0x3A2A22u); b.Fill(x + 1, wallTop + 17, 16, 1, 0x3A2A22u);
        }
        // 架子（1.21.9）：一块木板上摆着三样东西
        var sx = (int)(b.Cols * 0.17);
        b.Fill(sx, wallTop + 30, 36, 3, 0x8A6238u); b.Fill(sx, wallTop + 33, 36, 1, 0x5E3E22u);
        b.Blit(new Sprite(new[] { "..c..", ".gGg.", "gGGGg", "gGWGg", ".ggg." }, new Dictionary<char, uint> { ['c'] = 0x8A6238u, ['g'] = 0xB8D8F0u, ['G'] = 0xD84A8Au, ['W'] = 0xF8B8D8u }), sx + 4, wallTop + 29);
        b.Blit(new Sprite(new[] { "..IIII", ".IiiiI", "IIIIII" }, new Dictionary<char, uint> { ['I'] = 0xD8D8D8u, ['i'] = 0xF0F0F0u }), sx + 15, wallTop + 29);
        b.Blit(new Sprite(new[] { "...o", "..oo", ".oO.", "wO..", "w..." }, new Dictionary<char, uint> { ['o'] = 0xE08A6Cu, ['O'] = 0xC06048u, ['w'] = 0x6B4A2Cu }), sx + 27, wallTop + 29);
        // 地面：草 → 泥土
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, 4,
            new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30, new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, 62);
        // 两支铜火把的木棍（火焰在动态层）
        foreach (var fx in new[] { 0.03, 0.5 }) { b.Fill((int)(b.Cols * fx), wallTop + 36, 2, 9, 0x6B5133u); }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g - 70, 3);
        var wallTop = g - 58;
        // 铜火把：绿色的火
        foreach (var fx in new[] { 0.03, 0.5 })
        {
            Paint.Flame(b, (int)(b.Cols * fx) - 1, wallTop + 35, 4, 5, t + fx * 10,
                new uint[] { 0x3E9A3Au, 0x5AC85Au, 0x9CF07Au, 0xE4FFC8u });
        }

        // 时间线（30 秒一轮）：4 趟搬运（每趟氧化一级），然后变成雕像，玩家走过去刮锈
        var p = Paint.Mod(t, 30);
        var ax = (int)(b.Cols * 0.08); var bx = (int)(b.Cols * 0.62);
        b.Blit(Chest(false), ax, g - 1);
        var from = (double)(ax + 16); var to = (double)(bx - 15);
        // 最后一趟放完东西后走到箱子右边，在那里变成雕像（离玩家近，好刮锈）
        var rest = (double)(bx + 18);
        var trip = (int)(p / 5); var tp = Paint.Mod(p, 5);
        var gx = rest; var step = 0; uint? carrying = null; var flip = false; var openB = false;
        var stage = Math.Min(3, trip);
        var statue = trip >= 4;
        if (!statue)
        {
            var items = new uint[] { 0xD8D8D8u, 0xF5D74Au, 0x4AEDD9u, 0xE08A6Cu };
            if (tp < 2.2) { gx = from + (to - from) * tp / 2.2; carrying = items[trip]; step = (int)(t * 5) % 2; }
            else if (tp < 2.8) { gx = to; openB = true; }
            else
            {
                if (trip == 3)
                {
                    // 绕到箱子前面，往右走到停下的地方
                    gx = to + (rest - to) * Math.Min(1, (tp - 2.8) / 1.6); step = tp < 4.4 ? (int)(t * 5) % 2 : 0;
                }
                else
                {
                    gx = to - (to - from) * (tp - 2.8) / 2.2; flip = true; step = (int)(t * 5) % 2;
                }
            }
        }
        // 刮锈：玩家走过去，挥两下斧头，铜傀儡一级级变回铜色
        var props = new PlayerProps { Held = CopperAxe, Armor = Armor.Full(ArmorMaterial.Copper) };
        var pose = Player.Idle(t, props, 4);
        var px = (int)(b.Cols * 0.8);
        if (statue)
        {
            var sp = p - 20;
            var walk = DotMath.Ramp(sp, 1, 2.5) * (1 - DotMath.Ramp(sp, 8, 9.5));
            pose.Dx = -(int)Math.Round((double)(px - (int)rest - 26) * walk);
            if ((sp is >= 1 and <= 2.5) || (sp is >= 8 and <= 9.5)) { pose.Walk = t * 9; }
            var ats = new[] { 3.0, 4.2, 5.4 };
            for (var k = 0; k < ats.Length; k++)
            {
                var at = ats[k];
                if (!(sp >= at && sp <= at + 0.6)) continue;
                var q = (sp - at) / 0.6;
                pose.ArmAngle = q < 0.35 ? 0.3 + 1.6 * (q / 0.35) : 1.9 - 1.6 * ((q - 0.35) / 0.65);
                // 刮下来的碎屑
                if (q > 0.3)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        var f = (q - 0.3) / 0.7;
                        b.Set((int)rest + 8 + i * 2 - (int)(f * i * 3), g - 14 - (int)(Math.Sin(f * Math.PI) * (4 + i)), k == 2 ? 0xFFFFFFu : Stages[3 - k][0]);
                    }
                }
            }
            stage = sp < 3.3 ? 3 : (sp < 4.5 ? 2 : (sp < 5.7 ? 1 : 0));
            if (sp > 6) { stage = 0; }
        }
        b.Blit(Chest(openB), bx, g - 1);
        b.Blit(Golem(Stages[stage], statue && p - 20 < 5.7, step, carrying), (int)gx, g - 1, flip: flip);
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

/// 1.21.11 Mounts of Mayhem：沙漠里的骆驼尸壳
///
/// 1.21.11 加入了长矛（带冲刺附魔）、鹦鹉螺、僵尸马、骆驼尸壳和焦骸（沙漠里的骷髅变种），焦骸骑着骆驼尸壳出现。
/// 黄昏的沙漠：焦骸骑着骆驼尸壳从左边走过来；玩家拿着铁长矛，看准时机往前一冲刺，把骆驼尸壳顶退，
/// 它掉头走开，过一会儿又走回来
public sealed class MountsOfMayhem : IDotScene
{
    public string Id => "1.21.11";
    public uint Background => 0x3A2E6Au;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 骆驼尸壳（朝右，36×38，约 2.4 格高）：深褐色的干皮，驼峰上一块破鞍，肚子上露出一道道肋骨，
    /// 细长的脖子往上弯，头往前伸。step 为迈步
    public static Sprite CamelHusk(int step)
    {
        var hide = new uint[] { 0x3C3024u, 0x302418u, 0x4A3C2Au, 0x241818u };
        return new Sprite(36, 38, (x, y) =>
        {
            // 头（往前伸的长脸）
            if (x is >= 25 and <= 34 && y is >= 0 and <= 5)
            {
                if (y == 2 && x == 29) return 0xD8C890u;
                if (x >= 32 && y >= 3) return 0x241818u;
                return DotMath.Pick(hide, x, y, 63);
            }
            if (x is >= 27 and <= 28 && y == 0) return null;
            // 脖子：从胸前往上弯，比驼峰高出一截
            if (x is >= 24 and <= 28 && y is >= 5 and <= 18) return DotMath.Pick(hide, x, y, 64);
            // 驼峰和破鞍
            if (x is >= 9 and <= 19 && y is >= 10 and <= 13) return y == 10 ? 0x5A4A36u : (y >= 12 ? 0x6E5A3Eu : 0x5A4A36u);
            // 身子
            if (x is >= 3 and <= 26 && y is >= 14 and <= 24)
            {
                if (y is >= 16 and <= 22 && x % 4 == 1 && x is >= 9 and <= 21 && y != 19) return 0x7A6A4Eu;   // 肋骨
                if (y == 24) return 0x241818u;
                return DotMath.Pick(hide, x, y, 65);
            }
            if (x is >= 1 and <= 2 && y is >= 15 and <= 21) return 0x302418u;   // 尾巴
            // 四条细长的腿
            var legs = new[] { 3, 7, 19, 23 };
            for (var i = 0; i < legs.Length; i++)
            {
                var lx = legs[i];
                if (lx <= x && x <= lx + 3)
                {
                    var up = i % 2 == step ? 2 : 0;
                    if (y >= 25 && y <= 37 - up) return y >= 36 - up ? 0x18180Cu : DotMath.Pick(hide, x, y, 66 + i);
                }
            }
            return null;
        });
    }

    /// 焦骸（1.21.11）：沙黄色的骷髅，头上的骨头发灰，深色的眼窝，瘦得只剩骨头的四肢
    private static readonly PlayerLook Parched = new()
    {
        HeadFront = new Sprite(
            new[] { "TTTTTTTT", "TtTTTtTT", "TTTTTTTT", "TkkTTkkT", "TkkTTkkT", "TTTggTTT", "TgkgkgkT", "tTTTTTTt" },
            new Dictionary<char, uint> { ['T'] = 0x9C9060u, ['t'] = 0x847848u, ['k'] = 0x302418u, ['g'] = 0x6A6A5Au }),
        HeadSide = new Sprite(
            new[] { "TTTt", "TTTt", "TTtt", "TTTt", "TTTt", "TtTt", "TTtt", "tttt" },
            new Dictionary<char, uint> { ['T'] = 0x847848u, ['t'] = 0x6C603Cu }),
        BodyFront = new Sprite(
            new[] { "TTTTTTTT", "dTkTTkTd", "dTTTTTTd", "dTkTTkTd", "dTTTTTTd", "ddTkkTdd", "dddTTddd", "dddTTddd", "ddTTTTdd", "dTtTTtTd", "dTTTTTTd", "dddddddd" },
            new Dictionary<char, uint> { ['T'] = 0x9C9060u, ['t'] = 0x786C48u, ['k'] = 0x302418u, ['d'] = 0x54482Eu }),
        BodySide = new Sprite(
            Enumerable.Repeat("Tt", 12).ToArray(),
            new Dictionary<char, uint> { ['T'] = 0x847848u, ['t'] = 0x6C603Cu }),
        NearArm = new Sprite(
            Enumerable.Repeat("dTTdd", 11).Concat(new[] { "dttdd" }).ToArray(),
            new Dictionary<char, uint> { ['T'] = 0x9C9060u, ['t'] = 0x786C48u, ['d'] = 0x54482Eu }),
        FarArm = new Sprite(
            Enumerable.Repeat("dTd", 12).ToArray(),
            new Dictionary<char, uint> { ['T'] = 0x786C48u, ['d'] = 0x4A3E28u }),
        Legs = new Sprite(
            Enumerable.Repeat("dTTdTTTdtd", 10).Concat(new[] { "dggdgggdgd", "dggdgggdgd" }).ToArray(),
            new Dictionary<char, uint> { ['T'] = 0x9C9060u, ['t'] = 0x786C48u, ['d'] = 0x54482Eu, ['g'] = 0x3C4824u }),
    };

    /// 铁长矛（竖着画，矛尖朝上；比剑长得多）
    private static readonly HeldItem IronSpear = new(new Sprite(SpearRows(),
        new Dictionary<char, uint> { ['l'] = 0xF0F0F0u, ['L'] = 0xD8D8D8u, ['M'] = 0xA0A0A0u, ['b'] = 0x604818u, ['B'] = 0x846024u, ['w'] = 0x604818u, ['W'] = 0x846024u }));

    private static string[] SpearRows()
    {
        var rows = new List<string> { "..l..", ".lLl.", ".LML.", ".LML.", "..M..", ".bBb.", "..w.." };
        for (var i = 0; i < 12; i++)
        {
            rows.Add("..W..");
            if (i % 3 == 0) rows.Add("..w..");
        }
        rows.AddRange(new[] { "..W..", "..w..", "..W.." });
        return rows.ToArray();
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x2E2660u, 0x5A3A78u, 0x9A4A72u, 0xD8705Au, 0xF4A85Au, 0xF8D890u });
        // 远处的沙丘，两层
        var dunes = new (double Amp, double Base, uint Col)[] { (10.0, 0.22, 0xC88A5Au), (7.0, 0.1, 0xE0A86Au) };
        for (var layer = 0; layer < dunes.Length; layer++)
        {
            var amp = dunes[layer].Amp;
            var baseFrac = dunes[layer].Base;
            var col = dunes[layer].Col;
            for (var x = 0; x < b.Cols; x++)
            {
                var h = (int)(g * baseFrac + (Math.Sin(x * (0.03 + 0.02 * (double)layer) + layer) * 0.5 + 0.5) * amp);
                for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Dither(x, y, 0.3) ? DotMath.Shade(col, 0.92) : col);
            }
        }
        // 仙人掌和枯灌木
        var cacti = new[] { 0.04, 0.55, 0.97 };
        for (var k = 0; k < cacti.Length; k++)
        {
            var cx = cacti[k];
            var x = (int)(b.Cols * cx); var h = 18 + k * 5;
            b.Fill(x, g - h, 5, h, 0x3E7A2Cu);
            for (var y = g - h + 1; y < g; y += 3) { b.Set(x + 1, y, 0x2E5A22u); b.Set(x + 3, y + 1, 0x5DA03Fu); }
            if (k != 1) { b.Fill(x + 5, g - h + 6, 3, 2, 0x3E7A2Cu); b.Fill(x + 7, g - h + 2, 2, 6, 0x3E7A2Cu); }
        }
        foreach (var cx in new[] { 0.2, 0.44, 0.68 })
        {
            var x = (int)(b.Cols * cx);
            foreach (var (dx, dy) in new (int Dx, int Dy)[] { (0, 0), (1, -1), (-1, -1), (2, -2), (-2, -3), (0, -2), (3, -4), (-3, -4) }) { b.Set(x + dx, g - 1 + dy, 0x8A6238u); }
        }
        // 沙地
        Paint.Strata(b, g, new uint[] { 0xDBCFA3u, 0xE3D7AEu, 0xCFC294u, 0xD6C99Bu }, 16,
            new uint[] { 0xD8C78Eu, 0xC9B67Au, 0xD1BF85u }, 16, new uint[] { 0xB5A26Au, 0xA69360u }, 67);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 风吹起的沙粒
        Paint.Falling(b, t, 16, new uint[] { 0xF4E4B8u, 0xE8D49Au }, g - 30, g, 0.12, 68);

        // 时间线（9 秒一轮）：骆驼走过来 → 停下 → 玩家冲刺把它顶退 → 它掉头走开
        var p = Paint.Mod(t, 9);
        var px = (int)(b.Cols * 0.76);
        var start = -40.0; var stop = (double)px - 70;
        var cx = start; var flip = false; var walking = true; var hurt = 0.0;
        if (p < 3) cx = start + (stop - start) * p / 3;
        else if (p < 4.3) { cx = stop; walking = false; }
        else if (p < 5.4)
        {
            cx = stop - 8 * DotMath.Ramp(p, 4.3, 4.7); walking = false;
            hurt = Math.Max(0, 1 - (p - 4.3) / 0.4);
        }
        else { cx = stop - 8 - (stop - 8 - start) * (p - 5.4) / 3.6; flip = true; }
        var camel = CamelHusk(walking || flip ? (int)(t * 4) % 2 : 2);
        if (hurt > 0)
        {
            var src = camel;
            camel = new Sprite(src.Width, src.Height, (x, y) =>
            {
                var v = src.Px[y * src.Width + x];
                return v == 0 ? null : (DotMath.Dither(x, y, hurt * 0.7) ? 0xC83A2Au : v & 0xFFFFFFu);
            });
        }
        var bob = (walking || flip) && (int)(t * 4) % 2 == 0 ? 1 : 0;
        b.Blit(camel, (int)cx, g - 1, flip: flip);
        // 焦骸坐在驼峰上
        var rider = new PlayerPose();
        rider.Sitting = true;
        rider.Hurt = hurt;
        Player.Draw(b, Parched, null, rider, (int)cx + (flip ? 14 : 12), g - 19 - bob, t, !flip);

        // 玩家：平端长矛往前冲刺
        var props = new PlayerProps { Held = IronSpear };
        var pose = Player.Idle(t, props, 6);
        if (p is >= 3.6 and <= 5.6)
        {
            var aim = DotMath.Ramp(p, 3.6, 3.9) * (1 - DotMath.Ramp(p, 5.2, 5.6));
            pose.ArmAngle = 0.06 + 1.45 * aim;
            // 让矛身转到水平、矛尖朝前（朝着骆驼）：总转角 = armAngle - π/2 + 0.45 + itemTilt ≈ -π/2
            pose.ItemTilt = -1.95 * aim;
            var dash = DotMath.Ramp(p, 4.0, 4.3) * (1 - DotMath.Ramp(p, 4.6, 5.4));
            pose.Dx = -(int)((px - stop - 56) * dash);
            if ((p is >= 4.0 and <= 4.3) || (p is >= 4.6 and <= 5.4)) { pose.Walk = t * 14; }
            // 冲刺扬起的沙尘
            if (p is >= 4.0 and <= 4.8)
            {
                for (var i = 0; i < 10; i++)
                {
                    var f = (p - 4.0) / 0.8;
                    b.Set(px + 12 + pose.Dx + i * 2 + (int)(f * 6), g - 1 - (int)(Math.Sin(f * Math.PI + i) * 3 + 2), i % 2 == 0 ? 0xF4E4B8u : 0xD8C78Eu);
                }
            }
        }
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

/// 26.1 Tiny Takeover：小动物们
///
/// 26.1 给几乎所有幼年生物换上了自己的模型（圆滚滚的大脑袋、小身子），还加入了金蒲公英——喂给幼崽，它就不再长大。
/// 晴天的草地上：方块小鸡、小猪、小牛、小狼蹦蹦跳跳；玩家拿着金蒲公英，喂给跑到跟前的小鸡，金色的星星冒出来
public sealed class TinyTakeover : IDotScene
{
    public string Id => "26.1";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 小鸡（朝右，10×10）：一个黄色的小方块，大黑眼睛，棕色的小喙，两根细腿
    private static readonly Sprite Chick = new(
        new[]
        {
            "YYYYYYYY..",
            "YYYYYYYY..",
            "YYYYYkYY..",
            "YYYYYkYbb.",
            "YYYYYYYbB.",
            "yYYYYYYY..",
            "yyYYYYYY..",
            "yyyyyyyy..",
            "..l..l....",
            ".ll.ll....",
        }, new Dictionary<char, uint> { ['Y'] = 0xFCD830u, ['y'] = 0xCC900Cu, ['k'] = 0x1E1E1Du, ['b'] = 0x9A6A3Au, ['B'] = 0x845400u, ['l'] = 0xB08A4Au });

    /// 小猪（朝右，13×10）：大头、小身子、短短的腿
    private static readonly Sprite Piglet = new(
        new[]
        {
            "......PPPPPP.",
            "pPPPPPPPPPPP.",
            "pPPPPPPPkPPP.",
            "pPPPPPPPPPPnN",
            "pPPPPPPPPPPnN",
            "ppPPPPPPPPPP.",
            "ppppppPPPPPP.",
            ".hh..hh.hh...",
            ".hh..hh.hh...",
            ".dd..dd.dd...",
        }, new Dictionary<char, uint> { ['P'] = 0xFCB4A8u, ['p'] = 0xE08A80u, ['k'] = 0x1E1E1Du, ['n'] = 0xF4A09Au, ['N'] = 0xB86A60u, ['h'] = 0xF0A098u, ['d'] = 0xB86A60u });

    /// 小牛（朝右，16×14，对照 26.1 的新模型）：大方脑袋占了快一半，脸前面是灰色的大鼻头，两只小灰角、侧面一只耳朵；
    /// 黑褐色的身子带白斑，四条粗短的腿
    private static readonly Sprite Calf = new(
        new[]
        {
            "..........h..h..",
            "........DDDDDDD.",
            "........DDDDDDDD",
            "......eDDWWDkwDD",
            "......eDDWWDkkDD",
            "BBBBBBBDDDDDDsSS",
            "BWWBBBBDDDDDDsSS",
            "BWWWBBBDDDDDDsnS",
            "BBWBBBBBDDDDDsSS",
            "bBBBBWWBBDDDDDD.",
            "bbBBBWBBBBBBBB..",
            ".LL.LL....LL.LL.",
            ".LL.LL....LL.LL.",
            ".dd.dd....dd.dd.",
        }, new Dictionary<char, uint>
        {
            ['B'] = 0x302418u, ['b'] = 0x1E1610u, ['D'] = 0x3C3024u, ['W'] = 0xD8D8D8u, ['e'] = 0x241818u, ['h'] = 0x9C9C9Cu,
            ['k'] = 0x0C0C0Cu, ['w'] = 0xF0F0F0u, ['s'] = 0x9C9C9Cu, ['S'] = 0x787878u, ['n'] = 0x3A3A3Au, ['L'] = 0x302418u, ['d'] = 0x180C0Cu,
        });

    /// 小狼（朝右，13×9）：灰色的大头和竖着的耳朵，小身子，尾巴翘着
    private static readonly Sprite Pup = new(
        new[]
        {
            ".......k..k..",
            ".......GGGGG.",
            "t......GGkGGs",
            ".tGGGGGGGGGsS",
            "..GGGGGGGGG..",
            "..gGGGGgGG...",
            "..gg.gg.gg...",
            "..gg.gg.gg...",
            "..dd.dd.dd...",
        }, new Dictionary<char, uint> { ['G'] = 0xC8C8C8u, ['g'] = 0x9C9C9Cu, ['k'] = 0x3A3A3Au, ['t'] = 0xA8A8A8u, ['s'] = 0xB49C84u, ['S'] = 0x3A3A3Au, ['d'] = 0x7A7A7Au });

    /// 金蒲公英（拿在手里，不转）
    private static readonly HeldItem GoldenDandelion = new(
        new Sprite(new[]
        {
            ".WyW.", "yYOYy", ".yOy.", "..g..", ".gg..", "..g..", "..g..",
        }, new Dictionary<char, uint> { ['W'] = 0xFCFCE4u, ['y'] = 0xE4C03Cu, ['Y'] = 0xE4E454u, ['O'] = 0xA8600Cu, ['g'] = 0x5A8A2Eu }),
        swings: false);

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u, 0xC8DCFAu });
        // 远处的山和树
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.028 + 2) * 0.5 + 0.5) * 20 + DotMath.Noise(x / 6, 0, 70) * 6);
            for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Pick(new uint[] { 0x5F8C4Eu, 0x547F45u, 0x6A9757u }, x, y, 71));
        }
        var trees = new[] { 0.03, 0.97 };
        for (var k = 0; k < trees.Length; k++)
        {
            var cx = trees[k];
            var x = (int)(b.Cols * cx);
            b.Fill(x - 2, g - 36, 4, 36, 0x6B5133u);
            for (var dy = -20; dy <= 6; dy++)
                for (var dx = -14; dx <= 14; dx++)
                    if ((double)(dx * dx) / 196 + (double)(dy * dy) / 180 < 1 && DotMath.Noise(x + dx, dy, 72 + k) > 0.1)
                    {
                        b.Set(x + dx, g - 36 + dy, DotMath.Pick(new uint[] { 0x3E7A2Cu, 0x4A8A34u, 0x5DA03Fu }, x + dx, dy, 73));
                    }
        }
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u, 0x76B850u }, 4,
            new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30, new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, 74);
        // 草地上的蒲公英和虞美人
        for (var x = 0; x < b.Cols; x++)
            if (DotMath.Noise(x, 0, 75) > 0.8)
            {
                b.Set(x, g - 1, 0x48783Cu); b.Set(x, g - 2, DotMath.Noise(x, 1, 75) > 0.5 ? 0xFCE040u : 0xD8341Fu);
            }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, 4);
        var px = (int)(b.Cols * 0.76);
        // 小动物们：各自蹦着走，走到头掉头
        void Wander(Sprite s, double x0, double span, double speed, double hopRate, double phase)
        {
            var q = Paint.Mod(t * speed + phase, 2);
            var forward = q < 1;
            var x = x0 + span * (forward ? q : 2 - q);
            var hop = (int)Math.Max(0, Math.Sin(t * hopRate + phase * 5) * 3);
            b.Blit(s, (int)x, g - 1 - hop, flip: !forward);
        }
        Wander(Calf, b.Cols * 0.08, b.Cols * 0.14, 0.05, 3, 0.3);
        Wander(Piglet, b.Cols * 0.26, b.Cols * 0.16, 0.07, 5, 1.1);
        // 小狼追着自己的尾巴转圈
        var spin = (int)(t * 2.5) % 2 == 0;
        b.Blit(Pup, (int)(b.Cols * 0.46), g - 1 - (Math.Sin(t * 5) > 0.6 ? 1 : 0), flip: spin);

        // 小鸡：蹦到玩家跟前，被喂金蒲公英，冒出金色的星星，然后蹦回去
        var p = Paint.Mod(t, 10);
        var home = b.Cols * 0.56; var near = (double)(px - 16);
        var cx = home;
        if (p < 2) cx = home;
        else if (p < 3.5) cx = home + (near - home) * DotMath.Ease((p - 2) / 1.5);
        else if (p < 7) cx = near;
        else if (p < 8.5) cx = near - (near - home) * DotMath.Ease((p - 7) / 1.5);
        else cx = home;
        var moving = (p is >= 2 and <= 3.5) || (p is >= 7 and <= 8.5);
        var hop2 = moving ? (int)(Math.Abs(Math.Sin(t * 10)) * 3) : (Math.Sin(t * 4) > 0.8 ? 1 : 0);
        b.Blit(Chick, (int)cx, g - 1 - hop2, flip: p is >= 7 and <= 8.5);
        if (p is >= 4.2 and <= 7)
        {
            for (var i = 0; i < 9; i++)
            {
                var a = (double)i / 9 * 2 * Math.PI + t * 2;
                var r = 6 + (p - 4.2) * 3;
                var x = (int)cx + 5 + (int)(Math.Cos(a) * r); var y = g - 8 - (int)(Math.Sin(a) * r * 0.6) - (int)((p - 4.2) * 2);
                if (Math.Sin(t * 9 + i) > -0.2) { b.Set(x, y, i % 2 == 0 ? 0xFCE040u : 0xFFF6B0u); }
            }
        }
        // 玩家：小鸡跑过来时把金蒲公英递下去
        var props = new PlayerProps { Held = GoldenDandelion };
        var pose = Player.Idle(t, props, 7);
        if (p is >= 3.4 and <= 5)
        {
            var k = DotMath.Ramp(p, 3.4, 3.8) * (1 - DotMath.Ramp(p, 4.6, 5));
            pose.ArmAngle = 0.9 * k;
            pose.HeadDip = k > 0.5 ? 1 : 0;
            pose.ItemUp = 0;
        }
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

/// 26.4 冰洞与冻尸
///
/// 26.4（快照）加入了冰洞：蓝冰的洞顶垂着冰锥，洞里长着发光的冰晶；还有冻尸（在细雪里待久了的僵尸）、冰冻效果。
/// 冰洞里：冻尸在左边，往玩家这边扔冰球；玩家举盾挡住，冰球碎成冰碴；洞顶的冰锥时不时掉下来摔碎
public sealed class IceCaves : IDotScene
{
    public string Id => "26.4";
    public uint Background => 0x0A1430u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.68);
    private int Ceiling(DotBuffer b, int x) =>
        (int)(b.Rows * 0.14 + Math.Abs(Math.Sin(x * 0.05)) * 8 + DotMath.Noise(x / 6, 0, 80) * 6);

    /// 盾牌（对照 1.9 CombatUpdate 的 shield，这里举着挡冰球）
    private static readonly Sprite Shield = new(
        new[]
        {
            "oooooooo", "oWWwWWWo", "oWnWWnWo", "oWWWwWWo", "owWWWWwo", "oWWnWWWo", "oWWWWnWo", "oWwWWWWo",
            "oWWWwWWo", "oWnWWWWo", "oWWWWwWo", "oooooooo",
        }, new Dictionary<char, uint> { ['o'] = 0x8A8E94u, ['W'] = 0x7A5A34u, ['w'] = 0x6A4A2Au, ['n'] = 0x5A5E66u });

    /// 冻尸：青灰色的冻僵的皮肤，头顶、肩膀和脚上结着白霜，深蓝的上衣，破破烂烂的棕裤子
    private static readonly PlayerLook Frostbite = new()
    {
        HeadFront = new Sprite(
            new[] { "WWwWWwWW", "WSSWSSWS", "SSSSSSSS", "SSSSSSSS", "SkrSSrkS", "SSSSSSSS", "SSsddsSS", "sSSSSSSs" },
            new Dictionary<char, uint> { ['W'] = 0xD8ECECu, ['w'] = 0x9CB4B4u, ['S'] = 0x609090u, ['s'] = 0x4A7878u, ['k'] = 0x302418u, ['r'] = 0x6A3A2Au, ['d'] = 0x3C5C5Cu }),
        HeadSide = new Sprite(
            new[] { "WWWw", "SSWs", "SSSs", "SSSs", "SSSs", "SSSs", "SSss", "ssss" },
            new Dictionary<char, uint> { ['W'] = 0xC8DCDCu, ['w'] = 0x9CB4B4u, ['S'] = 0x548484u, ['s'] = 0x426E6Eu }),
        BodyFront = new Sprite(
            new[] { "WWBBBBWW" }.Concat(Enumerable.Repeat("BBBbBBBB", 6)).Concat(new[] { "BBSBBbBB", "BbSSBBBB", "BBBBBbBB", "bBBBBBBb", "bbbbbbbb" }).ToArray(),
            new Dictionary<char, uint> { ['W'] = 0xD8ECECu, ['B'] = 0x2E5A7Au, ['b'] = 0x244A66u, ['S'] = 0x609090u }),
        BodySide = new Sprite(
            Enumerable.Repeat("Bb", 12).ToArray(),
            new Dictionary<char, uint> { ['B'] = 0x244A66u, ['b'] = 0x1C3A52u }),
        NearArm = new Sprite(
            new[] { "WWWWw", "WWSSs" }.Concat(Enumerable.Repeat("SSSSs", 9)).Concat(new[] { "sssss" }).ToArray(),
            new Dictionary<char, uint> { ['W'] = 0xD8ECECu, ['w'] = 0x9CB4B4u, ['S'] = 0x609090u, ['s'] = 0x4A7878u }),
        FarArm = new Sprite(
            new[] { "WWw" }.Concat(Enumerable.Repeat("SSs", 11)).ToArray(),
            new Dictionary<char, uint> { ['W'] = 0xC8DCDCu, ['w'] = 0x9CB4B4u, ['S'] = 0x548484u, ['s'] = 0x426E6Eu }),
        Legs = new Sprite(
            Enumerable.Repeat("PPPpPPPPpp", 7).Concat(new[] { "PP.pPPP.pp", "P..pP...p.", "WWWWWWWWww", "WWWWWWWWww", "wwwwwwwwww" }).ToArray(),
            new Dictionary<char, uint> { ['P'] = 0x3C3024u, ['p'] = 0x302418u, ['W'] = 0xD8ECECu, ['w'] = 0x9CB4B4u }),
    };

    /// 冰锥（宽 w、长 len）：上宽下尖，浅蓝带白色高光
    private static void Icicle(DotBuffer b, int x, int top, int len, int w = 4)
    {
        for (var dy = 0; dy < len; dy++)
        {
            var half = w / 2.0 * (1 - (double)dy / len);
            for (var dx = 0; dx < w; dx++)
                if (Math.Abs(dx + 0.5 - w / 2.0) <= half + 0.3)
                {
                    b.Set(x + dx, top + dy, dx == 0 ? 0xD8E4FCu : (dx == w - 1 ? 0x6078C0u : DotMath.Pick(new uint[] { 0x90B4FCu, 0x84A8F0u, 0x789CF0u }, x + dx, dy, 81)));
                }
        }
    }

    /// 一簇冰晶（底边在 bottom）：几根往上的尖刺
    private static void Crystal(DotBuffer b, int x, int bottom, int size)
    {
        var spikes = new (int Off, int H)[] { (0, size), (-3, size * 2 / 3), (3, size * 3 / 4), (-5, size / 3), (5, size / 2) };
        for (var k = 0; k < spikes.Length; k++)
        {
            var (off, h) = spikes[k];
            for (var dy = 0; dy < h; dy++)
            {
                var w = Math.Max(0, 2 - dy * 3 / Math.Max(1, h));
                for (var dx = -w; dx <= w; dx++) { b.Set(x + off + dx, bottom - dy, dx == -w ? 0xFCFCFCu : (k % 2 == 0 ? 0xB4CCFCu : 0xD8E4FCu)); }
            }
        }
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 洞里的深处：深蓝，往下渐亮一点
        Paint.Sky(b, g, new uint[] { 0x0A1430u, 0x0E1C40u, 0x142450u, 0x1E3468u });
        // 远处的冰墙：一块块蓝冰
        for (var y = 0; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
                if (DotMath.Noise(x / 9, y / 9, 82) > 0.55 && DotMath.Dither(x, y, 0.5))
                {
                    b.Set(x, y, 0x1E3468u);
                }
        // 洞顶（蓝冰）和挂着的冰锥
        for (var x = 0; x < b.Cols; x++)
        {
            var c = Ceiling(b, x);
            for (var y = 0; y < c; y++) { b.Set(x, y, y >= c - 2 ? 0x6C90E0u : DotMath.Pick(new uint[] { 0x30549Cu, 0x304890u, 0x3A5CA8u, 0x4A6CC0u }, x / 2, y / 2, 83)); }
        }
        for (var i = 0; i < 22; i++)
        {
            var x = (int)(DotMath.Noise(i, 0, 84) * b.Cols);
            Icicle(b, x, Ceiling(b, x) - 1, 6 + (int)(DotMath.Noise(i, 1, 84) * 24), 3 + (int)(DotMath.Noise(i, 2, 84) * 3));
        }
        // 地面：雪 → 浮冰 → 蓝冰
        Paint.Strata(b, g, new uint[] { 0xE8F0FCu, 0xD8E8FCu, 0xF4F8FFu, 0xC8DCF8u }, 4,
            new uint[] { 0x90B4FCu, 0x84A8F0u, 0x789CF0u, 0xA0C0FCu }, 18, new uint[] { 0x30549Cu, 0x304890u }, 85);
        // 地上的冰晶
        var spots = new[] { 0.06, 0.33, 0.52, 0.95 };
        for (var k = 0; k < spots.Length; k++) { Crystal(b, (int)(b.Cols * spots[k]), g - 1, 9 + k % 2 * 5); }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 冰晶发出的微光，一明一暗
        var spots = new[] { 0.06, 0.33, 0.52, 0.95 };
        for (var k = 0; k < spots.Length; k++)
        {
            var cx = spots[k];
            var x0 = (int)(b.Cols * cx); var r = 14.0 + Math.Sin(t * 1.4 + k) * 3;
            for (var y = g - 26; y < g; y++)
                for (var x = x0 - 18; x <= x0 + 18; x++)
                {
                    var d = Math.Sqrt(Math.Pow(x - x0, 2) + Math.Pow(y - g + 6, 2) * 1.6) / r;
                    if (d < 1 && DotMath.Dither(x, y, (1 - d) * 0.35) && b[x, y] == 0) b.Set(x, y, 0x4A78D0u);
                }
        }
        // 飘着的小冰晶粒
        Paint.Falling(b, t, 26, new uint[] { 0xD8E4FCu, 0xFFFFFFu }, 20, g, 0.03, 86);
        // 冰锥掉下来摔碎（6 秒一根）
        var n = (int)(t / 6); var fp = Paint.Mod(t, 6);
        var ix = (int)(b.Cols * (0.1 + DotMath.Noise(n, 0, 87) * 0.4));
        var top = Ceiling(b, ix);
        if (fp < 1.2)
        {
            var y = top + (int)(fp * fp * 90);
            if (y + 14 < g) { Icicle(b, ix, y, 14, 4); }
            else
            {
                for (var i = 0; i < 10; i++)
                {
                    var f = (fp - Math.Sqrt((g - 14 - top) / 90.0)) * 3;
                    b.Set(ix + 2 + (int)((i - 5) * f * 3), g - 1 - (int)(Math.Sin(i) * 3 * f + 2 * f), i % 2 == 0 ? 0xD8E4FCu : 0x90B4FCu);
                }
            }
        }

        // 冻尸扔冰球（4 秒一次），玩家举盾挡住
        var px = (int)(b.Cols * 0.76); var fx = (int)(b.Cols * 0.18);
        var p = Paint.Mod(t, 4);
        var fpose = new PlayerPose();
        fpose.ArmAngle = 1.35 + Math.Sin(t * 2) * 0.08;
        fpose.Dx = (int)Math.Round(Math.Sin(t * 0.8) * 3);
        if (p is >= 0.4 and <= 1.0) { fpose.ArmAngle = p < 0.7 ? 1.35 + 1.4 * (p - 0.4) / 0.3 : 2.75 - 1.4 * (p - 0.7) / 0.3; }
        Player.Draw(b, Frostbite, null, fpose, fx, g - 1, t + 2, true);
        // 冰球：抛物线飞过去，撞到盾上碎掉
        var sx = (double)(fx + 18); var ex = (double)(px - 6);
        if (p is >= 0.9 and <= 2.1)
        {
            var f = (p - 0.9) / 1.2;
            var x = sx + (ex - sx) * f; var y = (double)(g - 24) - Math.Sin(f * Math.PI) * 22;
            b.Blit(new Sprite(new[] { ".bb.", "bWbb", "bbbB", ".BB." }, new Dictionary<char, uint> { ['b'] = 0x90B4FCu, ['W'] = 0xFFFFFFu, ['B'] = 0x6078C0u }), (int)x, (int)y);
        }
        else if (p is >= 2.1 and <= 2.6)
        {
            var f = (p - 2.1) / 0.5;
            for (var i = 0; i < 8; i++)
            {
                var a = (double)i / 8 * Math.PI + Math.PI / 2;
                b.Set((int)ex - (int)(Math.Cos(a) * f * 8) - 2, g - 24 + (int)(Math.Sin(a + Math.PI / 2) * f * 6) + (int)(f * f * 10), i % 2 == 0 ? 0xFFFFFFu : 0x90B4FCu);
            }
        }
        var pose = Player.Idle(t, new PlayerProps(), 1);
        pose.Blocking = p is >= 1.6 and <= 2.8;
        Player.Draw(b, cast.Player, new PlayerProps { Offhand = Shield }, pose, px, g - 1, t);
    }
}
