namespace Kiln.Core.DotScene;

// 各版本的横版场景（对照原版 DotSceneVersions.swift）
//
// 共同的模板：这个版本新增的地形做背景，新生物在左边，登录的玩家在右边（斜 45°，用他自己的皮肤），
// 拿着或穿着这个版本新增的东西。每个场景在站位、道具、动作上各有变化，但读起来是同一套。
// 除了登录的玩家，场景里其他的人一律用作者 NelsonPence 的皮肤（cast.Author）。

/// 1.8 Bountiful Update：斗兽场里的 PVP。1.8 以 PVP 出名：玩家（右）和作者（左）穿着钻石甲、
/// 拿着钻石剑在斗兽场里对打，跳劈、格挡连击、击退、暴击粒子（对照原版 PvpArena）
public sealed class PvpArena : IDotScene
{
    public string Id => "1.8";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u });
        // 斗兽场的看台：两层拱廊，石砖墙
        var wallTop = (int)(b.Rows * 0.16);
        uint[] brick = { 0x7D7D7Du, 0x888888u, 0x727272u, 0x8F8F8Fu };
        for (var y = wallTop; y < g; y++)
        {
            for (var x = 0; x < b.Cols; x++)
            {
                var row = (y - wallTop) / 4;
                var mortar = (y - wallTop) % 4 == 3 || (x + (row % 2) * 4) % 8 == 7;
                b.Set(x, y, mortar ? 0x5E5E5Eu : DotMath.Pick(brick, x / 8, row, 3));
            }
        }
        // 墙顶的垛口
        for (var x = 0; x < b.Cols; x += 12) b.Fill(x, wallTop - 4, 7, 4, 0x8A8A8Au);
        // 拱门洞口：上层小、下层大，里面是暗处（观众在动态层）
        void Arches(int top, int bottom, int width, int gap)
        {
            var x = gap / 2;
            while (x + width < b.Cols)
            {
                var r = width / 2.0;
                var cx = x + r;
                for (var y = top; y < bottom; y++)
                {
                    for (var dx = 0; dx < width; dx++)
                    {
                        var px = x + dx + 0.5;
                        var inside = y >= top + r || Math.Pow(px - cx, 2) + Math.Pow(top + r - y, 2) <= r * r;
                        if (inside)
                            b.Set(x + dx, y, DotMath.DitherRamp(new uint[] { 0x1C1A20u, 0x2A2730u, 0x36323Cu }, (double)(y - top) / (bottom - top), x + dx, y));
                    }
                }
                x += width + gap;
            }
        }
        var mid = wallTop + (g - wallTop) * 45 / 100;
        Arches(wallTop + 4, mid - 3, 12, 10);
        for (var x = 0; x < b.Cols; x++) { b.Set(x, mid - 2, 0x5A5A5Au); b.Set(x, mid - 1, 0x9A9A9Au); }
        Arches(mid + 3, g - 7, 16, 12);
        // 场地边上的矮墙
        for (var y = g - 6; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
                b.Set(x, y, y == g - 6 ? 0xA8A8A8u : DotMath.Pick(brick, x / 6, y, 5));
        // 旗帜（1.8 新增）：挂在墙顶，红蓝相间，带图案
        for (int x = 8, i = 0; x < b.Cols - 8; x += 46, i++)
        {
            var flagBase = i % 2 == 0 ? 0xB02E26u : 0x3C44AAu;
            b.Fill(x - 1, wallTop - 1, 9, 1, 0x6B5133u);
            for (var y = 0; y < 16; y++)
            {
                for (var dx = 0; dx < 7; dx++)
                {
                    var c = flagBase;
                    if (i % 2 == 0 && (dx == 3 || y == 6)) c = 0xF0F0F0u;
                    if (i % 2 == 1 && Math.Abs(dx - 3) == Math.Abs(y - 7) % 4) c = 0xF9D84Au;
                    if (y == 15 && dx % 2 == 1) continue;
                    b.Set(x + dx, wallTop + y, c);
                }
            }
        }
        // 沙地
        Paint.Strata(b, g, new uint[] { 0xDBCFA3u, 0xE3D7AEu, 0xCFC294u, 0xD6C99Bu }, 16,
            new uint[] { 0xD8C78Eu, 0xC9B67Au, 0xD1BF85u }, 16, new uint[] { 0xB5A26Au, 0xA69360u }, seed: 7);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        var wallTop = (int)(b.Rows * 0.16);
        var mid = wallTop + (g - wallTop) * 45 / 100;
        Paint.Clouds(b, t, wallTop - 4, count: 3);
        // 看台上的观众：拱门里一排脑袋，此起彼伏
        uint[] skins = { 0xB78A6Bu, 0xE1B48Eu, 0x8E6249u, 0xF2CBA4u };
        uint[] hair = { 0x3B2717u, 0xE3792Bu, 0x1E1E1Du, 0xD9B648u };
        foreach (var (row, y0) in new[] { (0, mid - 7), (1, g - 12) })
        {
            for (var i = 0; i < b.Cols; i += 5)
            {
                if (DotMath.Noise(i, row, 77) <= 0.35) continue;
                var hop = Math.Sin(t * (3 + DotMath.Noise(i, row, 78) * 4) + i) > 0.6 ? 1 : 0;
                var c = DotMath.Pick(skins, i, row, 79);
                var h = DotMath.Pick(hair, i, row, 80);
                b.Fill(i, y0 - hop, 3, 2, c);
                b.Fill(i, y0 - 1 - hop, 3, 1, h);
            }
        }
        // 墙上的火把
        for (var x = 30; x < b.Cols; x += 46)
        {
            b.Fill(x, mid + 8, 1, 5, 0x6B5133u);
            Paint.Flame(b, x - 1, mid + 7, 3, 4, t + x);
        }

        // 战斗时间线（5.2 秒一轮）
        var cycle = 5.2;
        var p = Paint.Mod(t, cycle);
        double Swing(double q) => q < 0.35 ? 0.3 + 1.6 * (q / 0.35) : 1.9 - 1.6 * ((q - 0.35) / 0.65);
        double BlockHit(double phase) => 0.45 + Math.Abs(Math.Sin(t * 9 + phase)) * 0.9;

        var me = new PlayerPose();
        var foe = new PlayerPose();
        me.Dx = (int)Math.Round(Math.Sin(t * 1.9) * 2);
        foe.Dx = (int)Math.Round(Math.Sin(t * 1.9 + Math.PI) * 2);
        me.ArmAngle = 0.5; foe.ArmAngle = 0.5;
        // 玩家跳劈
        if (p is >= 0.4 and <= 1.15)
        {
            var k = (p - 0.4) / 0.75;
            me.Lift = (int)Math.Round(4 * k * (1 - k) * 10);
            if (p is >= 0.75 and <= 1.05) { me.ArmAngle = Swing((p - 0.75) / 0.3); me.ItemTilt = 0; }
        }
        // 作者被打中：泛红、被击退
        if (p is >= 0.95 and <= 2.3)
        {
            foe.Hurt = Math.Max(0.0, 1 - (p - 0.95) / 0.3);
            foe.Dx -= (int)Math.Round(6 * DotMath.Ramp(p, 0.95, 1.3) * (1 - DotMath.Ramp(p, 1.6, 2.3)));
            if (p < 1.3) foe.Lift = (int)Math.Round(Math.Sin((p - 0.95) / 0.35 * Math.PI) * 3);
        }
        // 作者反击
        if (p is >= 2.8 and <= 3.5)
        {
            var k = (p - 2.8) / 0.7;
            foe.Lift = (int)Math.Round(4 * k * (1 - k) * 8);
            if (p is >= 3.05 and <= 3.35) { foe.ArmAngle = Swing((p - 3.05) / 0.3); foe.ItemTilt = 0; }
        }
        if (p is >= 3.25 and <= 4.5)
        {
            me.Hurt = Math.Max(0.0, 1 - (p - 3.25) / 0.3);
            me.Dx += (int)Math.Round(6 * DotMath.Ramp(p, 3.25, 3.6) * (1 - DotMath.Ramp(p, 3.9, 4.5)));
            if (p < 3.6) me.Lift = (int)Math.Round(Math.Sin((p - 3.25) / 0.35 * Math.PI) * 3);
        }
        // 两边都在格挡连击（1.8 的标志）
        if ((p is >= 1.6 and <= 2.6) || (p is >= 3.9 and <= 4.9)) { me.ArmAngle = BlockHit(0); foe.ArmAngle = BlockHit(1.3); }
        // 不在挥砍时，剑尖水平指向对方（略微上扬）
        var meSwinging = p is >= 0.75 and <= 1.05;
        var foeSwinging = p is >= 3.05 and <= 3.35;
        if (!meSwinging) me.ItemTilt = -0.45 - me.ArmAngle + Math.PI / 2 - 1.35;
        if (!foeSwinging) foe.ItemTilt = -0.45 - foe.ArmAngle + Math.PI / 2 - 1.35;

        var props = new PlayerProps { Held = HeldItem.DiamondSword, Armor = Armor.Full(ArmorMaterial.Diamond) };
        var meX = (int)(b.Cols * 0.585);
        var foeX = (int)(b.Cols * 0.4);
        Player.Draw(b, cast.Author, props, foe, foeX, g - 1, t + 0.7, true);
        Player.Draw(b, cast.Player, props, me, meX, g - 1, t);

        // 暴击粒子：被打中时从身上炸开的暗蓝色小星
        void Crits(int x, int y, double q)
        {
            if (!(q >= 0 && q < 0.6)) return;
            for (var i = 0; i < 9; i++)
            {
                var a = (double)i / 9 * 2 * Math.PI + 0.4;
                var r = 3 + q * 26;
                var px = x + (int)(Math.Cos(a) * r);
                var py = y + (int)(Math.Sin(a) * r * 0.7) - (int)(q * 6);
                var c = i % 2 == 0 ? 0x7C8FD0u : 0x4E5A8Au;
                b.Set(px, py, c); b.Set(px + 1, py, c); b.Set(px - 1, py, c); b.Set(px, py + 1, c); b.Set(px, py - 1, c);
            }
        }
        Crits(foeX + 8 + foe.Dx, g - 22, p - 0.95);
        Crits(meX + 2 + me.Dx, g - 22, p - 3.25);
    }
}

/// 1.12 World of Color：彩色混凝土与鹦鹉。1.12 加入了 16 色混凝土、带釉陶瓦和鹦鹉。
/// 鹦鹉在左边飞，一只停在玩家肩膀上（1.12 的新特性）；玩家站在一级彩色台阶上（对照原版 WorldOfColor）
public sealed class WorldOfColor : IDotScene
{
    public string Id => "1.12";
    public uint Background => 0x86AAF2u;

    private static readonly uint[] Concrete =
        { 0xCF3A2Au, 0xE06A12u, 0xF1AF15u, 0x5E9A1Eu, 0x157788u, 0x2C2E8Fu, 0x64209Cu, 0xA9309Fu, 0xD5658Eu, 0x24A0C4u, 0xCFD5D6u };

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// 鹦鹉（侧面朝右，约 14 点高）：红白冠羽、黑色脸斑、深色喙、黄蓝翅膀、长尾；两帧翅膀
    public static Sprite Parrot(uint body, uint wing, uint wing2, bool flap)
    {
        var rows = flap
            ? new[]
            {
                "...wR.......", "....Rw......", "..RRRRR.....", ".RRRRwK.....", ".RRRRKKB....", "..RRRRKb.Y..",
                "..RRRRR.YYZ.", "...RRRYYZZ..", "...RRRR.....", "....RRR.....", "....RZZ.....", "....g.g.....",
                ".....RZ.....", "......Z.....",
            }
            : new[]
            {
                "...wR.......", "....Rw......", "..RRRRR.....", ".RRRRwK.....", ".RRRRKKB....", "..RRRRKb....",
                "..RRRYYR....", "...RYYZZ....", "...RYZZR....", "....RZZR....", "....RRR.....", "....g.g.....",
                ".....RZ.....", "......Z.....",
            };
        return new Sprite(rows.Select(r => new string(r.Reverse().ToArray())).ToArray(),
            new Dictionary<char, uint> { ['R'] = body, ['w'] = 0xF4F4F4u, ['K'] = 0x1E1E1Du, ['B'] = 0x3A3D42u, ['b'] = 0x5A5E66u, ['Y'] = wing, ['Z'] = wing2, ['g'] = 0x6E6A66u });
    }

    public static readonly Sprite ShoulderParrot = new(
        new[] { "..wR..", "..RRw.", "RRRRK.", "RRRKKB", "RYYRb.", ".RZZR.", "..RR..", "..g.g." }
            .Select(r => new string(r.Reverse().ToArray())).ToArray(),
        new Dictionary<char, uint> { ['R'] = 0xE53B2Cu, ['w'] = 0xF4F4F4u, ['K'] = 0x1E1E1Du, ['B'] = 0x3A3D42u, ['b'] = 0x5A5E66u, ['Y'] = 0xF1C232u, ['Z'] = 0x2E5BD9u, ['g'] = 0x6E6A66u });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u, 0xC2D6FAu });
        // 远处的彩色方块建筑：一级级的台阶和塔
        var x = 2;
        var i = 0;
        while (x < b.Cols)
        {
            var h = 1 + (int)(DotMath.Noise(i, 0, 3) * 4);
            var w = 1 + (int)(DotMath.Noise(i, 1, 3) * 2);
            for (var bx = 0; bx < w; bx++)
            {
                for (var by = 0; by < h; by++)
                {
                    var c = Concrete[(i * 3 + by + bx) % Concrete.Length];
                    Paint.Block(b, x + bx * 16, g - 16 * (by + 1), DotMath.Shade(c, 0.78 + 0.05 * by), seed: i);
                }
            }
            x += w * 16 + 6 + (int)(DotMath.Noise(i, 2, 3) * 18);
            i += 1;
        }
        // 带釉陶瓦：图案方块，点缀在建筑上
        for (var k = 0; k < 4; k++)
        {
            var bx = (int)(DotMath.Noise(k, 7, 3) * (b.Cols - 16));
            var by = g - 16 * (1 + k % 2);
            for (var dy = 0; dy < 16; dy++)
                for (var dx = 0; dx < 16; dx++)
                {
                    var d = Math.Abs(dx - 8) + Math.Abs(dy - 8);
                    b.Set(bx + dx, by + dy, d % 6 < 2 ? 0xF7F2E8u : (d % 6 < 4 ? Concrete[k * 2 % Concrete.Length] : 0x2C2E8Fu));
                }
        }
        // 草地
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, 3, new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30,
            new uint[] { 0x7F7F7Fu, 0x6E6E6Eu, 0x8C8C8Cu }, seed: 11);
        // 玩家脚下的一级彩色台阶
        var sx = (int)(b.Cols * 0.72);
        for (var k = 0; k < 2; k++) Paint.Block(b, sx + k * 16, g - 16, Concrete[new[] { 8, 9 }[k]], seed: 70 + k);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 2, count: 4);
        // 三只鹦鹉在左边绕圈飞
        var colors = new (uint Body, uint Wing, uint Wing2)[] { (0xE53B2Cu, 0xF1C232u, 0x2E5BD9u), (0x3DB24Au, 0xF1C232u, 0x2E5BD9u), (0x2E9BD9u, 0xF1C232u, 0x1E3E9Au) };
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            var a = t * (0.7 + i * 0.15) + i * 2.1;
            var cx = b.Cols * (0.16 + i * 0.09) + Math.Cos(a) * 14;
            var cy = g * (0.38 + i * 0.1) + Math.Sin(a * 1.3) * 7;
            var flap = Math.Sin(t * 14 + i) > 0;
            b.Blit(Parrot(c.Body, c.Wing, c.Wing2, flap), (int)cx, (int)cy, flip: Math.Cos(a) > 0);
        }
        var props = new PlayerProps { Shoulder = ShoulderParrot };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 3), (int)(b.Cols * 0.72) + 8, g - 17, t);
    }
}

/// 1.13 Update Aquatic：海滩、珊瑚礁、海龟与海豚。1.13 重做了海洋：珊瑚、海带、海龟、海豚、三叉戟。
/// 左边海水里有珊瑚礁和摇摆的海带，海龟在沙滩上慢慢爬，海豚偶尔跃出水面；
/// 玩家站在右边的沙滩上，拿着三叉戟，戴着海龟壳（时不时摘下来再戴回去）（对照原版 UpdateAquatic）
public sealed class UpdateAquatic : IDotScene
{
    public string Id => "1.13";
    public uint Background => 0x86AAF2u;

    private (int Ground, int Water, int Shore) Layout(DotBuffer b)
    {
        var g = (int)(b.Rows * 0.62);
        return (g, g - 3, (int)(b.Cols * 0.5));
    }

    /// 海底的高度：越往左越深
    private int Seabed(int x, DotBuffer b)
    {
        var (g, _, shore) = Layout(b);
        if (x >= shore) return g;
        var k = (double)(shore - x) / shore;
        return g + (int)(Math.Min(1, k * 1.6) * 34) + (int)(Math.Sin(x * 0.11) * 2);
    }

    /// 海龟（侧面朝右，约 22×8）：亮绿的壳上有深色的格纹，头和鳍是浅绿
    public static readonly Sprite Turtle = new(
        new[]
        {
            ".......dSSSSd.........",
            "....dSSsSlSSsSSd......",
            "..dSslSSSSsSSlSSd..hh.",
            ".dSSSSsSSlSSSSSsSd.hHH",
            "dddddddddddddddddd.hkH",
            "..ff..........ffhhhHHh",
            ".ff.............ff....",
            "ff................ff..",
        },
        new Dictionary<char, uint> { ['d'] = 0x2A7A33u, ['S'] = 0x3BAA45u, ['s'] = 0x2E8A3Au, ['l'] = 0x55C25Au, ['h'] = 0x4FAE52u, ['H'] = 0x6CC46Au, ['k'] = 0x1E1E1Du, ['f'] = 0x4FAE52u });

    public static readonly Sprite Dolphin = new(
        new[]
        {
            "......FF........", ".....FFG........", "..GGGGGGGGGGG...", ".GGGGGGGGGGGGG.T",
            "GGkGGGGGGGGGGGTT", "BBWWWWWWWWGGG..T", "..WWWWWW.F......", ".........F......",
        },
        new Dictionary<char, uint> { ['G'] = 0x9FB3C8u, ['F'] = 0x8299B0u, ['W'] = 0xD5DEE8u, ['B'] = 0x7A8FA6u, ['k'] = 0x1E2A3Au, ['T'] = 0x8299B0u });

    public void PaintStatic(DotBuffer b)
    {
        var (g, water, _) = Layout(b);
        Paint.Sky(b, water, new uint[] { 0x4F7FE0u, 0x6F98ECu, 0x8FB3FFu, 0xB6CDFBu });
        // 沙滩与海底
        for (var x = 0; x < b.Cols; x++)
        {
            var top = Seabed(x, b);
            for (var y = top; y < b.Rows; y++)
            {
                var d = y - top;
                var c = d < 10 ? DotMath.Pick(new uint[] { 0xE8DCAEu, 0xDBCFA3u, 0xF0E4B8u }, x, y, 2) : DotMath.Pick(new uint[] { 0xD8C78Eu, 0xC9B67Au }, x, y, 3);
                b.Set(x, y, DotMath.Dither(x, y, (double)(y - g) / (b.Rows - g) * 0.7) ? DotMath.Shade(c, 0.6) : c);
            }
        }
        // 珊瑚：五种颜色的小丛
        var corals = new uint[] { 0xD9579Bu, 0x3D5BD9u, 0xD8341Fu, 0xF1C232u, 0xA34ED9u };
        for (var i = 0; i < 14; i++)
        {
            var x = (int)(DotMath.Noise(i, 0, 31) * (Layout(b).Shore - 12)) + 4;
            var top = Seabed(x, b);
            if (top <= water + 6) continue;
            var c = corals[i % corals.Length];
            var h = 3 + (int)(DotMath.Noise(i, 1, 31) * 6);
            for (var dy = 0; dy < h; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (!(DotMath.Noise(x + dx, dy, 32 + i) > 0.35 || dx == 0)) continue;
                    b.Set(x + dx, top - 1 - dy, dy == h - 1 ? DotMath.Shade(c, 1.2) : c);
                }
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var (g, water, shore) = Layout(b);
        Paint.Clouds(b, t, water / 2, count: 4);
        // 海带：从海底长出来，左右摇摆
        for (var i = 0; i < 9; i++)
        {
            var x0 = (int)(DotMath.Noise(i, 0, 41) * (shore - 20)) + 6;
            var top = Seabed(x0, b);
            var h = Math.Min(top - water - 3, 10 + (int)(DotMath.Noise(i, 1, 41) * 18));
            if (h <= 4) continue;
            for (var dy = 0; dy < h; dy++)
            {
                var sway = (int)Math.Round(Math.Sin(t * 1.4 + i + dy * 0.18) * dy * 0.12);
                b.Set(x0 + sway, top - 1 - dy, dy % 4 == 0 ? 0x6FAE3Au : 0x4E8C2Au);
            }
        }
        // 海水：在静态的海底与珊瑚上叠一层蓝，水面一排浪花在动
        for (var x = 0; x < shore + 14; x++)
        {
            var bottom = Seabed(x, b);
            var surface = water + (Math.Sin(x * 0.4 + t * 2.2) > 0.6 ? -1 : 0);
            if (bottom <= surface) continue;
            for (var y = surface; y < bottom; y++)
            {
                var w = Math.Sin(x * 0.18 + y * 0.4 - t * 1.5);
                if (y == surface) b.Set(x, y, 0xD9F1FFu);
                else if (DotMath.Dither(x, y, 0.55 + w * 0.15)) b.Set(x, y, y - surface < 6 ? 0x3F86E0u : 0x2E64C8u);
            }
        }
        // 海豚：每隔几秒从水里跃出一道弧
        var dp = Paint.Mod(t, 7.5) / 1.6;
        if (dp < 1)
        {
            var x = (int)(b.Cols * (0.08 + dp * 0.22));
            var y = water - (int)(4 * dp * (1 - dp) * 22);
            b.Blit(Dolphin, x, y, flip: true);
        }
        // 气泡
        for (var i = 0; i < 8; i++)
        {
            var p = Paint.Mod(t * 0.25 + DotMath.Noise(i, 0, 51), 1);
            var x = (int)(DotMath.Noise(i, 1, 51) * (shore - 10)) + 4;
            var bed = Seabed(x, b);
            var y = bed - (int)(p * (bed - water));
            if (y > water) b.Set(x, y, 0xCFEFFFu);
        }
        // 海龟在沙滩上慢慢来回爬
        var tp = Paint.Mod(t / 18, 1);
        var dir = tp < 0.5;
        var walk = (double)shore + 4 + 30 * (dir ? tp * 2 : (1 - tp) * 2);
        b.Blit(Turtle, (int)walk, g - 1 + (Math.Sin(t * 6) > 0.5 ? 0 : 0), flip: !dir);
        // 玩家：三叉戟，戴着海龟壳
        var props = new PlayerProps { Held = HeldItem.Trident, RemovableHelmet = ArmorMaterial.Turtle };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 6), (int)(b.Cols * 0.8), g - 1, t);
    }
}

/// 1.16 Nether Update：绯红森林里的猪灵。1.16 重做了下界：绯红 / 诡异森林、猪灵、下界合金。
/// 背景是熔岩瀑布与暗红的下界岩，巨型绯红菌和垂泪藤，
/// 左边的猪灵拿着金剑，玩家穿下界合金甲、拿下界合金剑，站在中间偏右的菌岩小坡上（对照原版 NetherUpdate）
public sealed class NetherUpdate : IDotScene
{
    public string Id => "1.16";
    public uint Background => 0x2A0A0Au;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.63);
    private int[] LavaX(DotBuffer b) => new[] { (int)(b.Cols * 0.3), (int)(b.Cols * 0.86) };

    /// 猪灵（斜对着右边的玩家，和玩家一样 32 点高）：宽大的粉色头，浅色大鼻子和鼻孔，獠牙，下垂的耳朵；
    /// 棕色皮甲配金扣，粉色手臂，棕色裤子。前面那只手（右侧）单独画，可以拿东西
    public static Sprite PiglinBody(int headDip)
    {
        return new Sprite(20, 32, (x, y) =>
        {
            uint P = 0xD9967Cu, p = 0xC27E64u, side = 0xA86A52u;
            // 头：侧面 x3…6，正面 x7…16，耳朵挂在两边
            var hy = y - headDip;
            if (hy >= 0 && hy < 8)
            {
                if (x >= 3 && x <= 6) return hy < 2 ? p : side;
                if (x >= 7 && x <= 16)
                {
                    var fx = x - 7;
                    if (hy == 3 && (fx == 1 || fx == 8)) return 0x2A1C14u;
                    if (hy >= 4 && hy <= 6 && fx >= 3 && fx <= 6) return hy == 5 && (fx == 3 || fx == 6) ? 0x8A4A3Au : 0xF0B7A0u;
                    if (hy == 6 && (fx == 1 || fx == 8)) return 0xF2E6C8u;
                    return hy < 2 ? 0xE5A68Cu : (DotMath.Noise(x, hy, 5) < 0.15 ? p : P);
                }
                if (x == 2 && hy >= 2 && hy <= 5) return 0xB4705Au;
                if (x >= 17 && x <= 18 && hy >= 2 && hy <= 6 && !(x == 18 && hy == 2)) return 0xC27E64u;
            }
            // 身体：皮甲，腰带上的金扣
            if (y >= 8 && y < 20)
            {
                if (x >= 8 && x <= 15)
                {
                    if (y == 14) return (x >= 11 && x <= 12) ? 0xF5C84Au : 0x4A2C18u;
                    if (y == 13 && x >= 11 && x <= 12) return 0xF5C84Au;
                    return DotMath.Noise(x, y, 6) < 0.2 ? 0x5E3820u : 0x7A4A2Au;
                }
                if (x >= 6 && x <= 7) return 0x5E3820u;
                // 后面那只手（画面左侧）：粉色，袖口是皮甲
                if (x >= 2 && x <= 5) return y < 10 ? 0x6A4026u : (x == 2 ? side : p);
            }
            // 腿
            if (y >= 20 && y < 32)
            {
                if (x >= 8 && x <= 15) return y >= 29 ? 0x3E2716u : (x == 11 || x == 12 ? 0x57361Fu : 0x6E4428u);
                if (x >= 6 && x <= 7) return y >= 29 ? 0x2E1C10u : 0x4E3018u;
            }
            return null;
        });
    }

    public static readonly Sprite PiglinArm = new(
        new[] { "LLL", "LLL" }.Concat(Enumerable.Repeat("PPp", 9)).Concat(new[] { "ppp" }).ToArray(),
        new Dictionary<char, uint> { ['L'] = 0x6A4026u, ['P'] = 0xD9967Cu, ['p'] = 0xC27E64u });

    public static readonly Sprite GoldIngot = new(
        new[] { "..YYYY.", ".YyYYYY", "YYYYYYo", "ooooooo" },
        new Dictionary<char, uint> { ['Y'] = 0xF5D74Au, ['y'] = 0xFFF39Au, ['o'] = 0xB08A1Eu });

    public static readonly Sprite DirtItem = new(
        new[] { ".dDdD.", "dDdDdD", "DdDDdD", "dDdDDd", "DDdDdD", ".dDDd." },
        new Dictionary<char, uint> { ['d'] = 0x6F4627u, ['D'] = 0x8A5A33u });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 背景：下界岩的暗红，越往上越暗
        for (var y = 0; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
            {
                var c = DotMath.DitherRamp(new uint[] { 0x1E0606u, 0x2E0A0Au, 0x431212u, 0x5A1A18u }, (double)y / g, x, y);
                if (DotMath.Noise(x / 3, y / 3, 5) > 0.93) c = DotMath.Shade(c, 1.3);
                b.Set(x, y, c);
            }
        // 熔岩瀑布两侧的光
        foreach (var lx in LavaX(b))
            for (var y = 0; y < g; y++)
                for (var x = lx - 20; x < lx + 26; x++)
                {
                    var d = 1 - Math.Abs(x - lx - 3) / 23.0;
                    if (d > 0 && DotMath.Dither(x, y, d * 0.6)) b.Set(x, y, 0x7A2A14u);
                }
        // 巨型绯红菌：菌柄 + 菌盖（疣块）+ 菌光体
        double[] centers = { 0.1, 0.5, 0.95 };
        for (var k = 0; k < centers.Length; k++)
        {
            var cx = centers[k];
            var x = (int)(b.Cols * cx);
            var h = (int)(g * (0.45 + 0.1 * (k % 2)));
            b.Fill(x, g - h, 5, h, 0x7B2E45u);
            for (var y = g - h; y < g; y++) if (DotMath.Noise(x, y, 61) < 0.3) b.Set(x + 1, y, 0x5C2235u);
            var capTop = g - h - 14;
            for (var y = capTop; y < g - h + 3; y++)
                for (var dx = -14; dx <= 18; dx++)
                {
                    var w = 14 - (g - h + 3 - y) / 2;
                    if (Math.Abs(dx - 2) > w + 2) continue;
                    b.Set(x + dx, y, DotMath.Noise(x + dx, y, 62) < 0.08 ? 0xFFAE45u : DotMath.Pick(new uint[] { 0xA31D1Du, 0x8B1414u, 0xB82A22u }, x + dx, y, 63));
                }
            // 垂泪藤
            for (var v = 0; v < 5; v++)
            {
                var vx = x - 10 + v * 6;
                var len = 6 + (int)(DotMath.Noise(vx, 0, 64) * 14);
                for (var dy = 0; dy < len; dy++) b.Set(vx, g - h + 3 + dy, dy == len - 1 ? 0xD9483Au : 0xA3241Du);
            }
        }
        // 地面：绯红菌岩 + 下界岩；玩家脚下一个小坡
        var mound = (int)(b.Cols * 0.62);
        for (var x = 0; x < b.Cols; x++)
        {
            var bump = Math.Max(0, 5 - Math.Abs(x - mound - 8) / 3);
            for (var y = g - bump; y < b.Rows; y++)
            {
                var d = y - (g - bump);
                var c = d < 3 ? DotMath.Pick(new uint[] { 0x8F1F1Fu, 0xB02A2Au, 0x7A1818u }, x, y, 66) : DotMath.Pick(new uint[] { 0x6E2626u, 0x5A1E1Eu, 0x7A2C2Cu, 0x4E1A1Au }, x, y, 67);
                b.Set(x, y, DotMath.Dither(x, y, (double)(y - g) / (b.Rows - g) * 0.8) ? DotMath.Shade(c, 0.55) : c);
            }
            if (DotMath.Noise(x, 0, 68) > 0.6 && !(x >= mound - 2 && x <= mound + 18))
            {
                b.Set(x, g - bump - 1, 0xC23A2Eu);
                if (DotMath.Noise(x, 1, 68) > 0.5) b.Set(x, g - bump - 2, 0xD9483Au);
            }
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 熔岩瀑布：往下流动的条纹
        foreach (var lx in LavaX(b))
            for (var y = 0; y < g; y++)
                for (var x = lx; x < lx + 7; x++)
                {
                    var v = Math.Sin(y * 0.5 - t * 6 + x * 0.9);
                    b.Set(x, y, v > 0.6 ? 0xFFE08Au : (v > -0.2 ? 0xFF9A2Eu : 0xE0561Cu));
                }
        // 绯红孢子与火星往上飘
        Paint.Rising(b, t, 40, new uint[] { 0xD9483Au, 0xFF7A3Au, 0xFFB45Au }, 4, g - 2, speed: 0.05, seed: 3);
        // 以物换物（9 秒一轮）：玩家扔出金锭 → 猪灵接住，举到眼前看一看 → 扔出来一块泥土
        var cycle = 9.0;
        var p = Paint.Mod(t, cycle);
        var pgX = (int)(b.Cols * 0.22);
        var meX = (int)(b.Cols * 0.62) + 4;
        var meFeet = g - 6;
        var inspecting = p >= 1.2 && p <= 3.8;
        var headDip = inspecting && Math.Sin(t * 5) > 0 ? 1 : 0;
        var bob = Math.Sin(t * 2.2) > 0.5 ? 1 : 0;
        b.Blit(PiglinBody(headDip), pgX, g - 1 - bob);
        // 猪灵前面那只手：接金锭时伸出去，看的时候举到脸前，扔泥土时往前一甩
        var pigArm = 0.1 + Math.Sin(t * 1.4) * 0.06;
        if (p >= 0.7 && p <= 1.2) pigArm = 0.9;
        if (inspecting) pigArm = 1.5;
        if (p >= 3.8 && p <= 4.3) pigArm = 1.5 + (p - 3.8) / 0.5 * 0.6;
        var shoulderX = (double)(pgX + 17);
        var shoulderY = (double)(g - 1 - bob - 23);
        // 朝右：手往画面右边抬
        b.BlitRotated(PiglinArm, shoulderX, shoulderY, (1.5, 0), -pigArm);
        var pigHandX = shoulderX + Math.Sin(pigArm) * 10.5;
        var pigHandY = shoulderY + Math.Cos(pigArm) * 10.5;
        if (inspecting || (p >= 0.95 && p <= 1.2))
        {
            b.Blit(GoldIngot, (int)pigHandX - 3, (int)pigHandY + 2 - (inspecting ? 2 : 0));
        }
        // 金锭从玩家手里飞到猪灵手里
        if (p >= 0.35 && p <= 1.0)
        {
            var k = (p - 0.35) / 0.65;
            var x0 = (double)(meX - 4);
            var y0 = (double)(meFeet - 18);
            var x1 = pigHandX;
            var y1 = pigHandY;
            b.Blit(GoldIngot, (int)(x0 + (x1 - x0) * k) - 3, (int)(y0 + (y1 - y0) * k - 4 * k * (1 - k) * 18));
        }
        // 扔出来的泥土：一道弧线落在两人之间，弹一下，躺几秒后消失
        if (p >= 4.1 && p <= 8.4)
        {
            var k = Math.Min(1.0, (p - 4.1) / 0.8);
            var x0 = pigHandX;
            var x1 = b.Cols * 0.42;
            var y = (double)(g - 7) - 4 * k * (1 - k) * 16;
            if (k >= 1) { var q = p - 4.9; if (q < 0.4) y -= 4 * (q / 0.4) * (1 - q / 0.4) * 3; }
            var blinkOut = p > 7.6 && Math.Sin(t * 20) > 0;
            if (!blinkOut) b.Blit(DirtItem, (int)(x0 + (x1 - x0) * k), (int)y + 6);
        }
        // 玩家：下界合金甲与剑，头盔时不时摘下来；开头那一下是把金锭扔出去
        var props = new PlayerProps { Held = HeldItem.NetheriteSword, Armor = new Armor { Chest = ArmorMaterial.Netherite, Legs = ArmorMaterial.Netherite, Boots = ArmorMaterial.Netherite }, RemovableHelmet = ArmorMaterial.Netherite };
        var pose = Player.Idle(t, props, 1);
        if (p < 0.6) { pose.ArmAngle = 1.4 * Math.Sin(p / 0.6 * Math.PI); pose.ItemTilt = 0; }
        Player.Draw(b, cast.Player, props, pose, meX, meFeet, t);
    }
}

/// 1.20 Trails &amp; Tales：樱花林与嗅探兽。1.20 加入了樱花林、嗅探兽、考古（刷子与可疑的沙子）。
/// 左边的嗅探兽低头嗅地、刨土；玩家跪在可疑的沙子前用刷子刷，扬起沙尘；樱花瓣一直在飘（对照原版 TrailsAndTales）
public sealed class TrailsAndTales : IDotScene
{
    public string Id => "1.20";
    public uint Background => 0x9EC1F7u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// 嗅探兽（朝右，约 30×54）：方方正正的大身子，背上和两侧披着深绿的苔藓，下面是红褐色的毛，
    /// 前面是黄色的大鼻子。head 为鼻子往下伸的格数（嗅地、刨土）
    public static Sprite Sniffer(int head)
    {
        uint[] moss = { 0x1F6B4Eu, 0x2E8A60u, 0x23784Fu, 0x3AA374u };
        uint[] fur = { 0x8A3A2Au, 0xA44A32u, 0x7A3222u };
        int[] legCols = { 5, 6, 7, 8, 18, 19, 20, 21, 31, 32, 33, 34 };
        return new Sprite(56, 32, (x, y) =>
        {
            // 腿
            if (y >= 27 && legCols.Contains(x)) return y == 31 ? 0x2A1E18u : 0x3A2A22u;
            // 身体：苔藓盖在上面，下沿参差不齐地垂下来
            if (x >= 1 && x <= 43 && y >= 2 && y <= 26)
            {
                var fringe = 13 + (int)(DotMath.Noise(x, 0, 5) * 4);
                if (y < fringe) return y == 2 ? 0x3AA374u : DotMath.Pick(moss, x, y, 6);
                var bottomFringe = 24 + (int)(DotMath.Noise(x, 1, 5) * 3);
                if (y <= bottomFringe) return DotMath.Pick(fur, x, y, 7);
                return null;
            }
            if (x == 0 && y >= 4 && y <= 20) return 0x154F3Au;
            // 头：红褐色的上半部，黄色的鼻子。低头时整个头一起往下，脖子把头和身体连着，不会分开
            var hy = y - head;
            if (x >= 40 && x <= 45 && y >= 10 && y <= 18 + head) return DotMath.Pick(fur, x, y, 10);
            if (x >= 42 && x <= 49 && hy >= 10 && hy <= 18) return hy == 13 && x == 47 ? 0x1E1E1Du : DotMath.Pick(fur, x, hy, 8);
            if (x >= 44 && x <= 55 && hy >= 17 && hy <= 26)
            {
                if (hy == 20 && (x == 47 || x == 48 || x == 52 || x == 53)) return 0x5A3A12u;
                if (hy == 17 || x == 55) return 0xC49422u;
                return DotMath.Noise(x, hy, 9) < 0.2 ? 0xF5CF55u : 0xE8B93Au;
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x7FA6F2u, 0x9EC1F7u, 0xC2D6FAu, 0xE6C9E8u });
        // 远山
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.025 + 1) * 0.5 + 0.5) * 22 + DotMath.Noise(x / 7, 0, 2) * 6);
            for (var y = g - 10 - h; y < g; y++)
                b.Set(x, y, DotMath.DitherRamp(new uint[] { 0x8FA8C8u, 0x7E9A7Au, 0x6A8F5Eu }, (double)(y - (g - 10 - h)) / (h + 10), x, y));
        }
        // 樱花树
        double[] treeX = { 0.06, 0.44, 0.93 };
        for (var k = 0; k < treeX.Length; k++)
        {
            var cx = treeX[k];
            var x = (int)(b.Cols * cx);
            var h = 34 + k * 6;
            b.Fill(x, g - h, 4, h, 0x5E3A3Au);
            for (var y = g - h; y < g; y++) if (DotMath.Noise(x, y, 5) < 0.3) b.Set(x + 1, y, 0x4A2C2Eu);
            for (var dy = -18; dy <= 6; dy++)
                for (var dx = -24; dx <= 26; dx++)
                {
                    var r = dx * dx / 600.0 + dy * dy / 140.0;
                    if (!(r < 1 && DotMath.Noise(x + dx, dy, 6 + k) > 0.1)) continue;
                    b.Set(x + dx, g - h + dy, DotMath.DitherRamp(new uint[] { 0xE58FB8u, 0xF5B3D2u, 0xFAD0E3u }, 1 - r + DotMath.Noise(x + dx, dy, 7) * 0.2, x + dx, dy));
                }
        }
        // 草地上铺着粉色花瓣
        Paint.Strata(b, g, new uint[] { 0x6DB34Au, 0x5DA03Fu, 0x7AC255u }, 3, new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30,
            new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, seed: 9);
        for (var x = 0; x < b.Cols; x++) if (DotMath.Noise(x, 0, 13) > 0.62) b.Set(x, g - 1, DotMath.Pick(new uint[] { 0xF5B3D2u, 0xFAD0E3u, 0xE58FB8u }, x, 0, 14));
        // 可疑的沙子（玩家面前）
        var sx = (int)(b.Cols * 0.66);
        for (var dy = 0; dy < 14; dy++)
            for (var dx = 0; dx < 14; dx++)
                b.Set(sx + dx, g + 1 + dy - 14, DotMath.Noise(dx, dy, 15) < 0.12 ? 0xB59A5Au : DotMath.Pick(new uint[] { 0xE3D7AEu, 0xD6C99Bu }, dx, dy, 16));
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, count: 3);
        Paint.Falling(b, t, 46, new uint[] { 0xF5B3D2u, 0xFAD0E3u, 0xE58FB8u }, 0, g, speed: 0.05, seed: 21);
        // 嗅探兽：低头嗅、刨土，土粒往后溅
        var sp = Paint.Mod(t, 5);
        var dig = sp > 2.2 && sp < 4;
        var head = dig ? (Math.Sin(t * 10) > 0 ? 5 : 4) : (sp < 1 ? 0 : (Math.Sin(t * 4) > 0 ? 1 : 0));
        var sxn = (int)(b.Cols * 0.06);
        b.Blit(Sniffer(head), sxn, g - 1 + (dig ? 4 : 0) - (dig ? 4 : 0));
        if (dig)
            for (var i = 0; i < 7; i++)
            {
                var q = Paint.Mod(t * 3 + i * 0.15, 1);
                b.Set(sxn + 50 - (int)(q * 18), g - 2 - (int)(4 * q * (1 - q) * 10), i % 2 == 0 ? 0x6F4627u : 0x8A5A33u);
            }
        // 玩家：在可疑的沙子前刷沙，沙尘扬起
        var pose = new PlayerPose();
        var brushing = Paint.Mod(t, 8) < 5.5;
        pose.ArmAngle = brushing ? 1.05 + Math.Sin(t * 8) * 0.22 : Player.Idle(t, new PlayerProps { Held = HeldItem.Brush }).ArmAngle;
        pose.ItemTilt = brushing ? -1.2 : 0;
        pose.HeadDip = brushing ? 1 : 0;
        var px = (int)(b.Cols * 0.66) + 20;
        Player.Draw(b, cast.Player, new PlayerProps { Held = HeldItem.Brush }, pose, px, g - 1, t);
        if (brushing)
            for (var i = 0; i < 6; i++)
            {
                var q = Paint.Mod(t * 1.8 + i * 0.17, 1);
                b.Set(px - 10 + (int)(Math.Sin(i * 2.1) * 4), g - 15 - (int)(q * 10), 0xEAD9A6u);
            }
    }
}

/// 1.21 Tricky Trials：试炼密室里的旋风人。1.21 加入了试炼密室（凝灰岩砖、铜灯）、试炼刷怪笼、旋风人和重锤。
/// 左边的旋风人浮在空中打转，时不时射出风弹；玩家拿着重锤，偶尔跳起来砸地，落地时扬起一圈尘土（对照原版 TrickyTrials）
public sealed class TrickyTrials : IDotScene
{
    public string Id => "1.21";
    public uint Background => 0x3A3D3Eu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 凝灰岩砖墙：一格一格的砖，中间夹着铜块与铜格栅
        for (var y = 0; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
            {
                var row = y / 8;
                var mortar = y % 8 == 7 || (x + (row % 2) * 8) % 16 == 15;
                var c = mortar ? 0x3E4142u : DotMath.Pick(new uint[] { 0x5F6162u, 0x6C6F70u, 0x56595Au, 0x646768u }, x / 4, y / 4, 3);
                c = DotMath.Dither(x, y, 0.45 - (double)y / g * 0.45) ? DotMath.Shade(c, 0.7) : c;
                b.Set(x, y, c);
            }
        // 铜的横梁与铜格栅（部分氧化成青绿色）
        var beam = (int)(g * 0.3);
        for (var y = beam; y < beam + 5; y++)
            for (var x = 0; x < b.Cols; x++)
            {
                var ox = DotMath.Noise(x / 6, 0, 9) > 0.6;
                b.Set(x, y, y == beam ? (ox ? 0x8FD6B8u : 0xF0A070u) : (ox ? DotMath.Pick(new uint[] { 0x4FA88Au, 0x5DB89Au }, x, y, 10) : DotMath.Pick(new uint[] { 0xC46A3Au, 0xB05A2Eu, 0xD07A48u }, x, y, 11)));
            }
        for (var gx = 10; gx < b.Cols; gx += 60)
            for (var y = beam + 9; y < beam + 25; y++)
                for (var x = gx; x < gx + 16; x++)
                    b.Set(x, y, (x - gx) % 4 == 0 || (y - beam - 9) % 4 == 0 ? 0xB05A2Eu : 0x2A2C2Du);
        // 地面：磨制凝灰岩
        Paint.Strata(b, g, new uint[] { 0x7A7D7Eu, 0x6C6F70u, 0x84878Au }, 2, new uint[] { 0x5F6162u, 0x56595Au, 0x646768u }, 16,
            new uint[] { 0x4F5152u, 0x46484Au }, seed: 12);
        for (var x = 0; x < b.Cols; x += 16)
            for (var y = g; y < g + 18; y++) b.Set(x, y, 0x46484Au);
        // 试炼刷怪笼：深蓝色的笼子，四角是橙色的铜；火焰在动态层
        var sx = (int)(b.Cols * 0.4);
        for (var dy = 0; dy < 16; dy++)
            for (var dx = 0; dx < 16; dx++)
            {
                var edge = dx == 0 || dx == 15 || dy == 0 || dy == 15;
                var corner = (dx < 2 || dx > 13) && (dy < 2 || dy > 13);
                var bar = (dx % 4 == 1 || dy % 4 == 1) && DotMath.Noise(dx, dy, 13) > 0.25;
                if (corner) b.Set(sx + dx, g - 16 + dy, 0xE07A3Au);
                else if (edge) b.Set(sx + dx, g - 16 + dy, 0x1E2E3Eu);
                else if (bar) b.Set(sx + dx, g - 16 + dy, DotMath.Pick(new uint[] { 0x2C4458u, 0x3E5A72u }, dx, dy, 14));
                else b.Set(sx + dx, g - 16 + dy, 0x12181Eu);
            }
    }

    /// 旋风人（约 28 点高）：淡紫蓝色的方头，白色的眼睛；下面是条纹的 V 形核心，
    /// 外面一圈圈白色半透明的风环，越往下越窄、一直在转
    public static void Breeze(DotBuffer b, int cx, int top, double t)
    {
        // 风环：从上往下 4 圈，宽度随转动变化，点是隔一个画一个（半透明）
        int[] widths = { 20, 16, 12, 7 };
        var rings = new[] { (0, true), (3, true) };
        for (var i = 0; i < widths.Length; i++)
        {
            var w0 = widths[i];
            var a = t * 4.2 + i * 0.9;
            var w = (int)(w0 * (0.78 + 0.22 * Math.Abs(Math.Cos(a))));
            var y = top + 6 + i * 5 + (int)(Math.Sin(a) * 1);
            var x0 = cx - w / 2 + (int)(Math.Sin(a * 0.5) * 2);
            for (var dx = 0; dx < w; dx++)
            {
                foreach (var (dy, on) in rings)
                {
                    if (!(on && (dx + dy + (int)(t * 20)) % 2 == 0)) continue;
                    b.Set(x0 + dx, y + dy, dy == 0 ? 0xFFFFFFu : 0xD6ECF5u);
                }
            }
            for (var dy = 0; dy <= 3; dy++) { b.Set(x0, y + dy, 0xEAF6FBu); b.Set(x0 + w - 1, y + dy, 0xEAF6FBu); }
        }
        // 条纹的 V 形核心
        int[] core = { 6, 6, 5, 4, 4, 3, 2, 2, 1 };
        var coreTones = new[] { 0xFFFFFFu, 0xB9CDF0u, 0x8F86D8u };
        for (var i = 0; i < core.Length; i++)
        {
            var w = core[i];
            var c = coreTones[i % 3];
            for (var dx = 0; dx < w; dx++) b.Set(cx - w / 2 + dx, top + 8 + i, c);
        }
        // 方头
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var c = y < 2 ? (DotMath.Noise(x, y, 3) < 0.4 ? 0xB4A8E8u : 0xA397DDu) : (DotMath.Noise(x, y, 4) < 0.25 ? 0x4E519Eu : 0x6A6CC4u);
                if (y == 4 && (x == 1 || x == 2 || x == 5 || x == 6)) c = 0xEAF6FFu;
                if (y == 5 && (x == 1 || x == 2 || x == 5 || x == 6)) c = 0x2A2E5Au;
                b.Set(cx - 4 + x, top + y, c);
            }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        var beam = (int)(g * 0.3);
        // 铜灯：一亮一暗
        for (int lx = 40, i = 0; lx < b.Cols; lx += 70, i++)
        {
            var on = Math.Sin(t * 0.9 + i * 1.7) > -0.2;
            b.Fill(lx, beam + 5, 6, 6, on ? 0xF9C66Au : 0x8A5A2Eu);
            if (on) b.Fill(lx + 1, beam + 6, 4, 4, 0xFFF0B0u);
        }
        // 试炼刷怪笼里的火（不祥时变成蓝色）
        var sx = (int)(b.Cols * 0.4);
        var ominous = Paint.Mod(t, 12) > 8;
        Paint.Flame(b, sx + 3, g - 3, 10, 10, t,
            ominous ? new uint[] { 0x1E5AD9u, 0x3E8AF0u, 0x8FD0FFu, 0xE6F8FFu } : new uint[] { 0xFF5A1Fu, 0xFF9A2Eu, 0xFFD54Au, 0xFFF6B0u });
        // 旋风人：浮在空中上下晃，风环一直在转
        var bx = b.Cols * 0.18;
        var by = (double)g - 34 + Math.Sin(t * 2) * 3;
        Breeze(b, (int)bx, (int)by, t);
        // 风弹：每隔几秒朝玩家飞过去
        var wp = Paint.Mod(t, 4.5) / 1.4;
        if (wp < 1)
        {
            var wx = (int)(bx + 10 + wp * b.Cols * 0.5);
            var wy = (int)(by + 12 + Math.Sin(wp * 3) * 2);
            for (var k = 0; k < 6; k++)
            {
                var a = t * 12 + k;
                b.Set(wx + (int)(Math.Cos(a) * 2), wy + (int)(Math.Sin(a) * 2), k % 2 == 0 ? 0xD9F4FAu : 0x9FD8E8u);
            }
        }
        // 玩家：拿着重锤，偶尔跳起来砸地
        var props = new PlayerProps { Held = HeldItem.Mace };
        var pose = Player.Idle(t, props, 4);
        var sp = Paint.Mod(t, 9) - 6.2;
        if (sp >= 0 && sp <= 1.1)
        {
            var k = sp / 0.9;
            if (k < 1)
            {
                pose.Lift = (int)Math.Round(4 * k * (1 - k) * 18);
                pose.ArmAngle = k < 0.5 ? 2.4 : 2.4 - (k - 0.5) * 4;
                pose.ItemTilt = 0;
            }
        }
        var px = (int)(b.Cols * 0.74);
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
        if (sp >= 0.9 && sp <= 1.5)
        {
            // 落地的冲击：一圈尘土往两边散开
            var q = (sp - 0.9) / 0.6;
            for (var i = 0; i < 10; i++)
            {
                var d = (int)(q * 26) + i;
                b.Set(px - 8 - d, g - 1 - i % 3, 0xA8ABADu);
                b.Set(px + 14 + d, g - 1 - i % 3, 0xA8ABADu);
            }
        }
    }
}

/// 26.3 Wilderness Bound：斑驳森林里的营地。26.3 加入了斑驳森林（红、橙、黄三色杨树和落叶）、
/// 废弃营地、草垫床、坐垫和探险家地图。傍晚的营地：篝火冒烟，狐狸睡在草垫床上；玩家坐在坐垫上看探险家地图（对照原版 WildernessBound）
public sealed class WildernessBound : IDotScene
{
    public string Id => "26.3";
    public uint Background => 0x2A2340u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.64);
    private int FireX(DotBuffer b) => (int)(b.Cols * 0.46);

    /// 睡着的狐狸（蜷成一团，朝左）：橙色身子、深一点的背、白色的下巴和尾巴尖、黑色耳尖和爪子
    public static readonly Sprite Fox = new(
        new[]
        {
            "..k..k............",
            "..OO.OO...........",
            ".OOOOOOO..oooooo..",
            "WOOcOOOOOooOOOOOo.",
            "WWOOOOOOOOOOOOOOOo",
            ".WWOOOOOOOOOOOOTTW",
            "..kWWOOOOOOOOTTTWW",
            "...kk.....kk..TWW.",
        },
        new Dictionary<char, uint> { ['O'] = 0xE37C2Bu, ['o'] = 0xA85A20u, ['W'] = 0xF4F0E8u, ['k'] = 0x1E1E1Du, ['c'] = 0x4A2A12u, ['T'] = 0xE37C2Bu });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 傍晚的天：上面是深紫，下面泛橙
        Paint.Sky(b, g, new uint[] { 0x1E1A3Au, 0x2A2350u, 0x4A3468u, 0x8A4A5Eu, 0xD9784Au });
        // 杨树：细高的浅色树干，三种颜色的树冠，树干上长着层架菇
        uint[][] leaves =
        {
            new uint[] { 0xB8322Au, 0xD9483Au, 0xE86A4Au },
            new uint[] { 0xD9702Au, 0xEA8A3Au, 0xF5A64Au },
            new uint[] { 0xD9B02Eu, 0xEACB4Au, 0xF5DE6Au },
        };
        double[] treeX = { 0.04, 0.17, 0.3, 0.6, 0.72, 0.86, 0.97 };
        for (var k = 0; k < treeX.Length; k++)
        {
            var cx = treeX[k];
            var x = (int)(b.Cols * cx);
            var h = (int)(g * (0.55 + 0.25 * DotMath.Noise(k, 0, 3)));
            var far = k % 2 == 1;
            b.Fill(x, g - h, 3, h, far ? 0x6E6458u : 0x9A8A73u);
            for (var y = g - h; y < g; y += 3) if (DotMath.Noise(x, y, 4) < 0.4) b.Set(x + 1, y, 0x5E5448u);
            if (DotMath.Noise(k, 1, 3) > 0.4) { b.Fill(x + 3, g - h / 3, 4, 1, 0xC98B4Au); b.Fill(x + 3, g - h / 3 + 1, 3, 1, 0xA86E36u); }
            var pal = leaves[k % 3];
            for (var dy = -26; dy <= 6; dy++)
                for (var dx = -9; dx <= 11; dx++)
                {
                    var r = dx * dx / 90.0 + dy * dy / 420.0;
                    if (!(r < 1 && DotMath.Noise(x + dx, dy, 5 + k) > 0.15)) continue;
                    var c = DotMath.DitherRamp(pal, 1 - r, x + dx, dy);
                    b.Set(x + dx, g - h + dy, far ? DotMath.Shade(c, 0.7) : c);
                }
        }
        // 地面：落叶层 + 泥土
        Paint.Strata(b, g, new uint[] { 0x8A5A2Au, 0xB06A2Eu, 0x6E4A2Au, 0xC4802Eu }, 3, new uint[] { 0x6F4627u, 0x8A5A33u, 0x5E3C22u }, 30,
            new uint[] { 0x4E3A2Au, 0x42301Fu }, seed: 15);
        for (var x = 0; x < b.Cols; x++) if (DotMath.Noise(x, 0, 16) > 0.55) b.Set(x, g - 1, DotMath.Pick(new uint[] { 0xD9483Au, 0xEA8A3Au, 0xEACB4Au }, x, 0, 17));
        // 帐篷（废弃营地）
        var tx = (int)(b.Cols * 0.22);
        for (var dy = 0; dy < 22; dy++)
        {
            var w = dy * 13 / 10;
            for (var dx = -w; dx <= w; dx++)
                b.Set(tx + dx, g - 22 + dy, dx == 0 ? 0x5A4A36u : (Math.Abs(dx) == w ? 0xA8946Au : (dx < 0 ? 0xD9C8A0u : 0xC2B086u)));
        }
        for (var dy = 8; dy < 22; dy++)
        {
            var w = (dy - 8) * 5 / 10;
            for (var dx = -w; dx <= w; dx++) b.Set(tx + dx, g - 22 + dy, 0x2E2418u);
        }
        // 草垫床（狐狸睡在上面）
        b.Fill((int)(b.Cols * 0.04), g - 4, 22, 4, 0xD8B54Au);
        for (var x = 0; x < 22; x++) if (x % 3 == 0) b.Set((int)(b.Cols * 0.04) + x, g - 4, 0xF0D06Au);
        // 篝火的木头和火光照亮的地面
        var fx = FireX(b);
        b.Fill(fx - 7, g - 3, 15, 3, 0x6B4A2Cu);
        b.Fill(fx - 5, g - 5, 11, 2, 0x8A5A33u);
        for (var x = fx - 40; x < fx + 40; x++)
            for (var y = g - 30; y < g; y++)
            {
                var d = 1 - Math.Sqrt(Math.Pow((x - fx) / 40.0, 2) + Math.Pow((g - y) / 30.0, 2));
                if (d > 0 && b[x, y] != 0 && DotMath.Dither(x, y, d * 0.5)) b.Set(x, y, DotMath.Shade(b[x, y] & 0xFFFFFFu, 1.35));
            }
        // 玩家坐的坐垫
        b.Fill((int)(b.Cols * 0.62) - 6, g - 4, 20, 4, 0xB5413Au);
        b.Fill((int)(b.Cols * 0.62) - 6, g - 4, 20, 1, 0xD9605Au);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 星星：只在天空上部，一闪一闪
        for (var i = 0; i < 30; i++)
        {
            if (!(Math.Sin(t * 1.7 + i * 2.3) > 0.1)) continue;
            b.Set((int)(DotMath.Noise(i, 0, 91) * b.Cols), (int)(DotMath.Noise(i, 1, 91) * g * 0.35), i % 5 == 0 ? 0xFFF6C8u : 0xD9D4F0u);
        }
        // 三色杨树的落叶
        Paint.Falling(b, t, 40, new uint[] { 0xD9483Au, 0xEA8A3Au, 0xEACB4Au }, 0, g, speed: 0.04, seed: 31);
        // 篝火与往上飘的烟
        var fx = FireX(b);
        Paint.Flame(b, fx - 5, g - 6, 11, 12, t);
        for (var i = 0; i < 14; i++)
        {
            var q = Paint.Mod(t * 0.18 + (double)i / 14, 1);
            var x = fx + (int)(Math.Sin(t * 0.8 + i) * (2 + q * 6));
            var y = g - 18 - (int)(q * (g - 26));
            if (DotMath.Dither(x, y, 1 - q)) b.Set(x, y, q < 0.5 ? 0x9A93A6u : 0x6E6880u);
        }
        // 狐狸蜷在草垫床上睡觉，呼吸时身子起伏，耳朵偶尔动一下
        var breath = Math.Sin(t * 1.6) > 0.3 ? 1 : 0;
        b.Blit(Fox, (int)(b.Cols * 0.04) + 3, g - 5, w: Fox.Width, h: Fox.Height + breath);
        if (Paint.Mod(t, 5) < 0.2) b.Set((int)(b.Cols * 0.04) + 5, g - 13 - breath, 0xE37C2Bu);
        // 玩家坐在坐垫上看探险家地图
        var props = new PlayerProps { Held = HeldItem.ExplorerMap };
        var pose = Player.Idle(t, props, 0);
        pose.Sitting = true;
        Player.Draw(b, cast.Player, props, pose, (int)(b.Cols * 0.62), g - 4, t);
    }
}
