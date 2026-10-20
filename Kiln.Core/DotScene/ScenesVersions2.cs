namespace Kiln.Core.DotScene;

// 移植自 DotSceneVersions.swift 的 6 个版本场景（WildUpdate / CombatUpdate / CavesAndCliffsI /
// CavesAndCliffsII / VillageAndPillage / BuzzyBees），像素数据与动画节奏与原版逐行一致。

/// 1.19 The Wild Update：远古城市里的监守者。远古城市的深板岩砖墙，地上和墙上蔓延着幽匿，魂灯发着青光，
/// 背景是巨大的强化深板岩门框；监守者（比玩家高一大截）就在玩家旁边，触须抖动、胸口的灵魂一跳一跳，时不时探过头来嗅；
/// 玩家蹲着不出声，拿着追溯指针，监守者嗅的时候一动不敢动；黑暗效果让整个画面一阵一阵地变黑
public sealed class WildUpdate : IDotScene
{
    public string Id => "1.19";
    public uint Background => 0x05080Au;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// Swift 的 .rounded() 是四舍五入远离零，C# 的 Math.Round 默认是银行家舍入
    private static int RoundAway(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// 监守者（朝右，48 点高，约 3 格，比玩家高一半）：宽厚的身子、相对小的头；深青黑的身体，骨色的肋骨和粗壮的上臂，
    /// 胸口青色的灵魂，头顶两侧的触须。pulse 为胸口灵魂的亮度（0…1），lean 为嗅的时候头往前探的格数
    public static Sprite Warden(bool roar, double pulse, int tendril, int lean = 0)
    {
        uint[] dark = { 0x0B1418u, 0x0E1C22u, 0x0A1216u, 0x12262Eu };
        uint[] teal = { 0x0E3B45u, 0x12505Cu, 0x145361u };
        uint[] bone = { 0xB8B48Eu, 0x9E9A78u, 0xC9C5A0u };
        var soul = pulse > 0.6 ? 0x6FF4FFu : (pulse > 0.3 ? 0x2AD6E8u : 0x1A8E9Cu);
        return new Sprite(38, 48, (x0, y) =>
        {
            var x = y <= 17 ? x0 - lean : x0;
            // 触须：头顶两侧往外翘
            if (y >= 0 && y < 7)
            {
                var lx = 10 - y / 2 - (y % 3 == 0 ? tendril : 0);
                var rx = 26 + y / 2 + (y % 3 == 0 ? tendril : 0);
                if (x == lx || x == lx + 1 || x == rx || x == rx - 1) return y % 2 == 0 ? 0x3FC6D0u : 0x1E8E9Au;
            }
            // 头：16 宽 14 高（x 10…25，y 4…17）
            if (x is >= 10 and <= 25 && y is >= 4 and <= 17)
            {
                if (y <= 6) return DotMath.Noise(x, y, 3) < 0.12 ? 0x2AD6E8u : DotMath.Pick(teal, x, y, 4);
                if (x is >= 13 and <= 22 && (roar ? y is >= 10 and <= 15 : y is >= 11 and <= 13)) return 0x040709u;
                if (x <= 11) return DotMath.Shade(DotMath.Pick(dark, x, y, 5), 0.8);
                return DotMath.Noise(x, y, 6) < 0.15 ? DotMath.Pick(teal, x, y, 7) : DotMath.Pick(dark, x, y, 8);
            }
            // 粗壮的胳膊：8 宽，从肩膀一直垂到膝盖（x 1…8 和 29…36，y 18…43），上臂骨色，往下渐暗
            if (y is >= 18 and <= 43 && (x is >= 1 and <= 8 || x is >= 29 and <= 36))
            {
                var k = (double)(y - 18) / 25;
                if ((x == 1 || x == 36) && y < 30) return DotMath.Pick(teal, x, y, 18);
                return DotMath.Dither(x, y, k * 1.25) ? DotMath.Pick(dark, x, y, 9)
                    : (DotMath.Noise(x, y, 10) < 0.22 ? 0x6E7680u : DotMath.Pick(bone, x, y, 11));
            }
            // 宽厚的身体：18 宽（x 10…27，y 18…35），胸口两侧是肋骨，中间是灵魂
            if (x is >= 9 and <= 28 && y is >= 18 and <= 35)
            {
                var ribRow = (y - 20) % 3 == 0 && y < 33;
                if (ribRow && (x is >= 11 and <= 15 || x is >= 22 and <= 26)) return DotMath.Pick(bone, x, y, 12);
                if (x is >= 16 and <= 21 && y is >= 21 and <= 31 && DotMath.Noise(x, y, 13) > 0.25) return soul;
                return DotMath.Pick(dark, x, y, 14);
            }
            // 腿：两条 6 宽（x 11…16、21…26，y 36…47），脚上有骨色条纹
            if (y is >= 36 and <= 47 && (x is >= 11 and <= 16 || x is >= 21 and <= 26))
            {
                if (y >= 44 && (x == 12 || x == 15 || x == 22 || x == 25)) return DotMath.Pick(bone, x, y, 16);
                return DotMath.Pick(dark, x, y, 17);
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 深板岩砖墙，越往上越黑
        for (var y = 0; y < g; y++)
        {
            for (var x = 0; x < b.Cols; x++)
            {
                var row = y / 6;
                var mortar = y % 6 == 5 || (x + (row % 2) * 8) % 16 == 15;
                var c = mortar ? 0x1E1E22u : DotMath.Pick(new uint[] { 0x3A3A3Eu, 0x45454Bu, 0x34343Au, 0x4A4A50u }, x / 4, y / 3, 3);
                c = DotMath.Dither(x, y, 0.75 - (double)y / g * 0.55) ? DotMath.Shade(c, 0.45) : c;
                b.Set(x, y, c);
            }
        }
        // 强化深板岩门框：骨色的四角，中间是空的黑
        var fx = (int)(b.Cols * 0.02);
        var fw = (int)(b.Cols * 0.3);
        var ft = (int)(g * 0.12);
        for (var y = ft; y < g; y++)
        {
            for (var x = fx; x < fx + fw; x++)
            {
                var border = x < fx + 6 || x >= fx + fw - 6 || y < ft + 6;
                if (border)
                {
                    var corner = (x < fx + 6 || x >= fx + fw - 6) && (y - ft) % 16 < 3;
                    b.Set(x, y, corner ? DotMath.Pick(new uint[] { 0xC9C5A0u, 0xB8B48Eu }, x, y, 4)
                                       : DotMath.Pick(new uint[] { 0x2E2E34u, 0x3A3A40u }, x, y, 5));
                }
                else
                {
                    b.Set(x, y, DotMath.DitherRamp(new uint[] { 0x020304u, 0x060B0Eu, 0x0B1A20u }, (double)(y - ft) / (g - ft) * 0.8, x, y));
                }
            }
        }
        // 幽匿：从地面往墙上蔓延，深青色带青点
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)(DotMath.Noise(x / 5, 0, 6) * 18 + DotMath.Noise(x / 2, 1, 6) * 6);
            for (var dy = 0; dy < h; dy++)
                b.Set(x, g - 1 - dy, DotMath.Noise(x, dy, 7) < 0.1 ? 0x2AD6E8u : DotMath.Pick(new uint[] { 0x0B2A33u, 0x0E3B45u, 0x08202Au }, x, dy, 8));
        }
        Paint.Strata(b, g, new uint[] { 0x0E3B45u, 0x0B2A33u, 0x12505Cu }, 3, new uint[] { 0x34343Au, 0x3A3A3Eu, 0x2E2E32u }, 16,
                     new uint[] { 0x26262Au, 0x1E1E22u }, seed: 19);
        for (var x = 0; x < b.Cols; x++)
            if (DotMath.Noise(x, 2, 20) > 0.88) b.Set(x, g, 0x2AD6E8u);
        // 幽匿感测体（玩家前面）
        var sx = (int)(b.Cols * 0.58);
        for (var dy = 0; dy < 8; dy++)
            for (var dx = 0; dx < 16; dx++)
                b.Set(sx + dx, g - 8 + dy, DotMath.Noise(dx, dy, 21) < 0.12 ? 0x2AD6E8u : DotMath.Pick(new uint[] { 0x0E3B45u, 0x12505Cu }, dx, dy, 22));
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 魂灯：挂在上面，一直发着青光
        foreach (var lx in new[] { (int)(b.Cols * 0.16), (int)(b.Cols * 0.86) })
        {
            var ly = (int)(g * 0.28);
            b.Fill(lx + 2, 0, 1, ly, 0x2E2E34u);
            b.Fill(lx, ly, 6, 8, 0x3A4A5Au);
            b.Fill(lx + 1, ly + 1, 4, 6, Math.Sin(t * 3 + lx) > 0 ? 0x6FF4FFu : 0x2AD6E8u);
            for (var i = 0; i < 10; i++)
            {
                var a = (double)i / 10 * 2 * Math.PI + t * 0.3;
                var r = 9 + Math.Sin(t * 2 + i) * 2;
                if (DotMath.Dither(lx, i, 0.5)) b.Set(lx + 3 + (int)(Math.Cos(a) * r), ly + 4 + (int)(Math.Sin(a) * r), 0x1A6E7Au);
            }
        }
        // 往上飘的灵魂粒子
        Paint.Rising(b, t, 18, new uint[] { 0x2AD6E8u, 0x6FF4FFu, 0x1A8E9Cu }, 4, g - 4, speed: 0.04, seed: 9);
        // 监守者：就在玩家旁边，心跳、触须抖动，每隔几秒探过头来嗅一嗅
        var p = Paint.Mod(t, 6.5);
        var sniffing = p is >= 2.2 and <= 4.4;
        var lean = sniffing ? RoundAway(2 * DotMath.Ramp(p, 2.2, 2.6) * (1 - DotMath.Ramp(p, 4.0, 4.4))) : 0;
        var beat = t * (sniffing ? 6.5 : 4.5);
        var pulse = Math.Max(0, Math.Sin(beat)) * (Math.Sin(beat) > 0.9 ? 1 : 0.7);
        var wx = (int)(b.Cols * 0.4);
        b.Blit(Warden(false, pulse, Math.Sin(t * (sniffing ? 16 : 6)) > 0 ? 1 : 0, lean), wx, g - 1);
        if (sniffing)
        {
            // 嗅的时候鼻子前面冒出的小气流
            for (var i = 0; i < 4; i++)
            {
                var q = Paint.Mod(t * 2.4 + i * 0.25, 1);
                b.Set(wx + 27 + lean + (int)(q * 7), g - 37 + i % 2 * 2 - (int)(q * 3), q < 0.5 ? 0x6FF4FFu : 0x1A8E9Cu);
            }
        }
        var roar = false;
        // 幽匿感测体的触须：音波或脚步传到时亮起来
        var sx = (int)(b.Cols * 0.58);
        var lit = roar || Math.Sin(t * 1.3) > 0.85;
        for (var k = 0; k < 4; k++)
        {
            var tx = sx + 3 + k * 3;
            for (var dy = 0; dy < 4 + k % 2 * 2; dy++) b.Set(tx, g - 9 - dy, lit ? 0x6FF4FFu : 0x0E3B45u);
        }
        // 玩家：蹲着不出声，看追溯指针；监守者嗅的时候一动不动，手也不敢抬
        var props = new PlayerProps { Held = HeldItem.RecoveryCompass };
        var pose = Player.Idle(t, props, 2);
        pose.Sneaking = true;
        pose.LookBack = false;
        if (sniffing) { pose.ArmAngle = 0.15; pose.ItemUp = 0; pose.HeadDip = 1; }
        var px = (int)(b.Cols * 0.68);
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
        // 黑暗效果：一阵一阵地变黑（有序抖动的黑点），只在玩家身边留一小圈能看见
        var dark = Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI / 3.6)), 2) * 0.92;
        if (dark > 0.02)
        {
            var cx = (double)(px + 5);
            var cy = (double)(g - 16);
            for (var y = 0; y < b.Rows; y++)
            {
                for (var x = 0; x < b.Cols; x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) / 900 + (y - cy) * (y - cy) / 500);
                    if (DotMath.Dither(x, y, dark * Math.Min(1, Math.Max(0, d - 0.35) * 1.6))) b.Set(x, y, 0x020304u);
                }
            }
        }
    }
}

/// 1.9 Combat Update：末地城。末地的虚空里漂着末地石小岛，远处是紫珀块的末地城塔楼
/// 和挂着龙首的末地船，末影龙在天边盘旋；左边的潜影贝开合着壳射出潜影弹，玩家背着鞘翅绕着末地城滑翔，潜影弹在后面追
public sealed class CombatUpdate : IDotScene
{
    public string Id => "1.9";
    public uint Background => 0x0E0814u;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    private static readonly uint[] Purpur = { 0xA77BA7u, 0xB98CB9u, 0x9A6E9Au, 0xAC80ACu };
    private static readonly uint[] EndStone = { 0xDBDE9Eu, 0xE8EAB0u, 0xC6C98Au, 0xD4D796u };
    public static readonly Sprite Shield = new(
        new[]
        {
            "oooooooo", "oWWwWWWo", "oWnWWnWo", "oWWWwWWo", "owWWWWwo", "oWWnWWWo", "oWWWWnWo", "oWwWWWWo",
            "oWWWwWWo", "oWnWWWWo", "oWWWWwWo", "oooooooo",
        },
        new Dictionary<char, uint> { ['o'] = 0x8A8E94u, ['W'] = 0x7A5A34u, ['w'] = 0x6A4A2Au, ['n'] = 0x5A5E66u });

    /// 潜影贝（16×16）：紫珀色的壳，open 为壳打开的高度，打开时露出淡黄绿色的头
    public static Sprite Shulker(int open)
    {
        return new Sprite(16, 16 + open, (x, y) =>
        {
            const int lidBottom = 7;
            // 上半个壳
            if (y <= lidBottom)
            {
                if (y == 0 || x == 0 || x == 15) return 0x8A5E8Au;
                if (y == lidBottom && (x < 3 || x > 12)) return 0x7A527Au;
                return DotMath.Pick(Purpur, x, y, 3);
            }
            // 壳打开的缝里：头
            if (y > lidBottom && y <= lidBottom + open)
            {
                if (x is >= 5 and <= 10)
                {
                    var hy = y - lidBottom;
                    if (hy == 2 && (x == 6 || x == 9)) return 0x1E1E1Du;
                    return hy == 1 ? 0xE3EAAAu : 0xD7E09Au;
                }
                return null;
            }
            // 下半个壳
            if (x == 0 || x == 15 || y == 15 + open) return 0x8A5E8Au;
            return DotMath.Pick(Purpur, x, y + 40, 4);
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 虚空：深紫黑
        Paint.Sky(b, g, new uint[] { 0x0A0610u, 0x120A1Cu, 0x1A0F28u, 0x22132Eu });
        // 末地城塔楼：一层层往上，每层带末地石砖的窗带，顶上是末地烛
        var tx = (int)(b.Cols * 0.44);
        var top = (int)(g * 0.12);
        const int floors = 4;
        var floorH = (g - top) / floors;
        for (var f = 0; f < floors; f++)
        {
            var w = 34 - f * 3;
            var y0 = top + f * floorH;
            var x0 = tx - w / 2 + (f % 2) * 2;
            for (var y = y0; y < y0 + floorH; y++)
            {
                for (var x = x0; x < x0 + w; x++)
                {
                    var line = (x - x0) % 8 == 0 || (y - y0) % 8 == 0;
                    var c = line ? 0x8A5E8Au : DotMath.Pick(Purpur, x, y, 5);
                    if ((y - y0) > 3 && (y - y0) < 9 && (x - x0) > 2 && (x - x0) < w - 3)
                        c = (x - x0) % 6 < 2 ? 0x1A1024u : DotMath.Pick(new uint[] { 0xE3E5A8u, 0xD8DA9Cu }, x, y, 6);
                    b.Set(x, y, c);
                }
            }
            // 每层的屋檐往外伸
            for (var x = x0 - 3; x < x0 + w + 3; x++) { b.Set(x, y0, 0xC79CC7u); b.Set(x, y0 + 1, 0x9A6E9Au); }
        }
        // 顶上的末地烛
        top = (int)(g * 0.12);
        foreach (var rx in new[] { tx - 15, tx + 15 })
            for (var dy = 0; dy < 7; dy++) b.Set(rx, top - 1 - dy, dy == 6 ? 0xFFFFFFu : 0xE8E0D8u);
        // 远处漂着的末地船：船身是紫珀，船头挂着黑色的龙首
        var sx = (int)(b.Cols * 0.72);
        var sy = (int)(g * 0.22);
        for (var dy = 0; dy < 6; dy++)
        {
            var w = 30 - dy * 2;
            for (var dx = 0; dx < w; dx++) b.Set(sx + dy + dx, sy + dy, DotMath.Shade(DotMath.Pick(Purpur, dx, dy, 7), 0.7));
        }
        b.Fill(sx + 13, sy - 14, 2, 14, 0x5E3A5Eu);
        b.Fill(sx + 8, sy - 12, 12, 7, 0x6A3E6Au);
        b.Fill(sx - 4, sy - 2, 5, 4, 0x1E1E22u);
        b.Set(sx - 4, sy - 1, 0xD92AD9u);
        // 末地石小岛
        for (var x = 0; x < b.Cols; x++)
        {
            var isleTop = g - (int)Math.Max(0, Math.Sin(x * 0.02) * 3);
            for (var y = isleTop; y < b.Rows; y++)
            {
                var d = y - isleTop;
                var c = d == 0 ? 0xF0F2C0u : DotMath.Pick(EndStone, x, y, 8);
                // 上面一截是实的末地石，再往下才渐渐融进虚空
                var fade = Math.Max(0, (double)d / (b.Rows - g) - 0.35) / 0.65;
                b.Set(x, y, DotMath.Dither(x, y, fade * 0.9) ? 0x1A0F28u : (d > 14 ? DotMath.Shade(c, 0.8) : c));
            }
        }
        // 紫颂植物：一节节方块往上长、往两边分叉，顶上是淡紫色的花方块
        void ChorusBlock(int x, int y, int size)
        {
            for (var dy = 0; dy < size; dy++)
                for (var dx = 0; dx < size; dx++)
                {
                    var edge = dx == 0 || dy == 0 || dx == size - 1 || dy == size - 1;
                    b.Set(x + dx, y + dy, edge ? 0x3E2450u : DotMath.Pick(new uint[] { 0x5E3A6Eu, 0x7A4A8Au, 0x6A4278u }, x + dx, y + dy, 9));
                }
        }
        void ChorusFlower(int x, int y, int size)
        {
            for (var dy = 0; dy < size; dy++)
                for (var dx = 0; dx < size; dx++)
                {
                    var edge = dx == 0 || dy == 0 || dx == size - 1 || dy == size - 1;
                    b.Set(x + dx, y + dy, edge ? 0x7A4A8Au : (DotMath.Noise(x + dx, y + dy, 10) < 0.35 ? 0xF2E6F5u : 0xC8A2D0u));
                }
        }
        var cxs = new[] { 0.03, 0.27, 0.9 };
        for (var k = 0; k < cxs.Length; k++)
        {
            const int n = 5;
            var x = (int)(b.Cols * cxs[k]);
            var y = g - n;
            for (var seg = 0; seg < 4 + k; seg++)
            {
                ChorusBlock(x, y, n);
                y -= n;
                if (seg % 2 == 1)
                {
                    var dir = (seg + k) % 2 == 0 ? 1 : -1;
                    ChorusBlock(x + dir * n, y + n, n);
                    ChorusFlower(x + dir * n * 2, y + n, n);
                }
            }
            ChorusFlower(x, y, n);
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 星星
        for (var i = 0; i < 36; i++)
        {
            if (Math.Sin(t * 1.5 + i * 2.1) > 0)
                b.Set((int)(DotMath.Noise(i, 0, 81) * b.Cols), (int)(DotMath.Noise(i, 1, 81) * g * 0.7), i % 4 == 0 ? 0xF2E6F5u : 0x9A86B4u);
        }
        // 末影龙在远处盘旋
        var dp = Paint.Mod(t / 26, 1);
        var dx = (int)(b.Cols * (1.1 - dp * 1.3));
        var dy = (int)(g * 0.1 + Math.Sin(t * 0.5) * 4);
        var wing = Math.Sin(t * 3) > 0 ? -3 : 2;
        b.Fill(dx, dy, 14, 3, 0x14141Au);
        b.Fill(dx - 4, dy + 1, 4, 2, 0x14141Au);
        b.Set(dx - 4, dy + 1, 0xD92AD9u);
        for (var k = 0; k < 7; k++)
        {
            b.Set(dx + 4 + k, dy + wing * (7 - k) / 7 - 1, 0x2A2A34u);
            b.Set(dx + 4 + k, dy + wing * (7 - k) / 7 - 2, 0x3A3A48u);
        }
        b.Fill(dx + 14, dy + 1, 8, 1, 0x14141Au);
        // 潜影贝：壳一开一合，打开时射出潜影弹
        var p = Paint.Mod(t, 6.0);
        var open = p < 0.5 ? (int)(p / 0.5 * 5) : (p < 2.4 ? 5 : (p < 2.9 ? (int)((2.9 - p) / 0.5 * 5) : 0));
        var towerTop = (int)(g * 0.12);
        var floorH = (g - towerTop) / 4;
        var shx = (int)(b.Cols * 0.44) - 17 - 3 - 4;
        var shBottom = towerTop + floorH * 2 - 1;
        b.Blit(Shulker(open), shx, shBottom);
        // 玩家：背着鞘翅绕着末地城滑翔，朝着飞行的方向；身后拖着几道气流
        var a = t * 0.32;
        var cx = b.Cols * 0.5 + Math.Cos(a) * b.Cols * 0.36;
        var cy = g * 0.42 + Math.Sin(a * 2) * g * 0.12;
        var facingRight = -Math.Sin(a) > 0;
        for (var k = 1; k <= 3; k++)
        {
            var tx = (int)cx + (facingRight ? -1 : 1) * (22 + k * 5);
            var ty = (int)cy - 4 + k * 3;
            for (var d = 0; d < 6 - k; d++) b.Set(tx + (facingRight ? -d : d), ty, 0x6A5A84u);
        }
        // 潜影弹：追着玩家飞
        var bp = (p - 0.8) / 3.6;
        if (bp > 0 && bp < 1)
        {
            var bx = (double)(shx + 8) + (cx - (shx + 8)) * bp;
            var by = (double)(shBottom - 8) + (cy - (shBottom - 8)) * bp + Math.Sin(bp * 14) * 3;
            foreach (var (ox, oy, c) in new (int, int, uint)[] { (0, 0, 0xFFFFFFu), (1, 0, 0xF5E6A8u), (0, 1, 0xF5E6A8u), (1, 1, 0xFFFFFFu) })
                b.Set((int)bx + ox, (int)by + oy, c);
            for (var k = 1; k <= 4; k++) b.Set((int)bx - k * 2, (int)by + (int)(Math.Sin(bp * 14 - k) * 1), 0xB9A6D8u);
        }
        var pose = new PlayerPose();
        pose.Flying = true;
        Player.Draw(b, cast.Player, new PlayerProps { Elytra = true }, pose, (int)cx, (int)cy, t, facingRight);
    }
}

/// 1.17 Caves & Cliffs I：繁茂洞穴。洞顶垂下挂着发光浆果的洞穴藤，地上是苔藓和杜鹃丛，墙上一簇紫水晶在闪；
/// 左边的水塘里美西螈在游、发光鱿鱼在发光；玩家站在苔藓台子上，时不时举起望远镜往远处看
public sealed class CavesAndCliffsI : IDotScene
{
    public string Id => "1.17";
    public uint Background => 0x101410u;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// Swift 的 .rounded() 是四舍五入远离零，C# 的 Math.Round 默认是银行家舍入
    private static int RoundAway(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static (int Lo, int Hi) Pool(DotBuffer b) => ((int)(b.Cols * 0.05), (int)(b.Cols * 0.42));

    /// 美西螈（朝右）：粉色身体，深粉色的鳃在头两侧往上翘，尾巴是一片扁鳍
    public static readonly Sprite Axolotl = new(
        new[]
        {
            "............g.g.g.", "............ggggg.", "TT....PPPPPPPPPPP.", "TTttPPPPPPPPPPPPPP",
            "TTttPPPPPPPPPPPkPP", "..ttpPPPPPPPPPPPPP", "....pppppppppppp..", ".....l...l....l...",
        },
        new Dictionary<char, uint> { ['P'] = 0xF4B4D0u, ['p'] = 0xE79AC0u, ['g'] = 0xD0558Au, ['T'] = 0xE58AB8u, ['t'] = 0xD877A8u, ['k'] = 0x3A1A3Au, ['l'] = 0xE79AC0u });

    /// 发光鱿鱼（对照原版，约 16×34）：方方的青色身子，带发光的亮斑和十字形的眼睛；下面一排方块触手，越往下越亮、尖端发白绿光。
    /// sway 让每根触手错开摆动，glow 为亮斑的亮度
    public static Sprite GlowSquid(double t, bool glow)
    {
        var spots = new (int, int)[] { (3, 3), (9, 4), (14, 2), (6, 11), (11, 12), (2, 13), (15, 10) };
        int[] tentacles = { 2, 6, 10, 14 };
        return new Sprite(18, 36, (x, y) =>
        {
            // 身子：x 1…16，y 0…15
            if (x is >= 1 and <= 16 && y is >= 0 and <= 15)
            {
                if (y == 0) return 0x3EB4B0u;
                if ((y == 7 && x is >= 3 and <= 5) || (x == 4 && y is >= 6 and <= 8)) return 0x0A2A2Au;   // 左眼（十字）
                if ((y == 7 && x is >= 12 and <= 14) || (x == 13 && y is >= 6 and <= 8)) return 0x0A2A2Au; // 右眼
                if (spots.Contains((x, y))) return glow ? 0x9FF8E0u : 0x4EC0A8u;
                return (x + y * 3) % 7 == 0 ? 0x24928Eu : 0x1E8484u;
            }
            // 触手：4 根看得见的方块触手，各自摆动
            for (var k = 0; k < tentacles.Length; k++)
            {
                var tx = tentacles[k];
                var off = RoundAway(Math.Sin(t * 1.8 + k * 1.3 + y * 0.15) * (y - 16) * 0.08);
                if (y is >= 16 and <= 35 && x >= tx + off && x <= tx + off + 1)
                {
                    var f = (double)(y - 16) / 19;
                    if (f > 0.85) return glow ? 0xC8FFE8u : 0x8FF2D2u;
                    return f > 0.6 ? 0x5ECCB0u : (f > 0.3 ? 0x2EA89Au : 0x1E8A8Au);
                }
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 洞壁：上面是石头，越往下越绿
        for (var y = 0; y < g; y++)
        {
            for (var x = 0; x < b.Cols; x++)
            {
                var k = (double)y / g;
                var c = DotMath.DitherRamp(new uint[] { 0x2A2E2Au, 0x3E443Au, 0x4E5A3Au, 0x56663Au }, 0.1 + k * 0.85, x, y);
                if (DotMath.Noise(x / 3, y / 3, 3) > 0.9) c = DotMath.Shade(c, 1.2);
                b.Set(x, y, c);
            }
        }
        // 紫水晶晶簇（墙上）
        var ax = (int)(b.Cols * 0.56);
        var ay = (int)(g * 0.42);
        var amethyst = new (int Dx, int Dy, uint C)[]
        {
            (0, -6, 0xD9C2FFu), (-1, -5, 0xB98AF0u), (0, -5, 0xE8D8FFu), (1, -5, 0xB98AF0u), (-1, -4, 0x9A6AD8u), (0, -4, 0xC8A8F8u),
            (1, -4, 0x9A6AD8u), (-4, -3, 0xB98AF0u), (-3, -2, 0x9A6AD8u), (3, -3, 0xB98AF0u), (4, -2, 0x9A6AD8u), (-2, -1, 0x8E5AC8u),
            (-1, -1, 0x9A6AD8u), (0, -1, 0x8E5AC8u), (1, -1, 0x9A6AD8u), (2, -1, 0x8E5AC8u),
        };
        foreach (var (dx, dy, c) in amethyst) b.Set(ax + dx, ay + dy, c);
        // 地面：苔藓块，下面是石头；左边是一块高出来的苔藓台地，中间挖出水塘（在地面以上，不会被标题挡住）
        var pl = Pool(b);
        var terrace = g - 38;
        for (var x = 0; x < b.Cols; x++)
        {
            var raised = x <= pl.Hi + 5;
            var top = raised ? terrace : g;
            for (var y = top; y < b.Rows; y++)
            {
                var d = y - top;
                var c = d < 4 ? DotMath.Pick(new uint[] { 0x6E8E34u, 0x5A7A2Au, 0x7A9A3Au, 0x4A6A22u }, x, y, 4)
                              : DotMath.Pick(new uint[] { 0x6E6E6Eu, 0x7A7A7Au, 0x5E5E5Eu }, x, y, 5);
                b.Set(x, y, DotMath.Dither(x, y, Math.Max(0, (double)(y - g)) / (b.Rows - g) * 0.8) ? DotMath.Shade(c, 0.55) : c);
            }
            if (x >= pl.Lo && x <= pl.Hi)
            {
                for (var y = terrace; y < g - 2; y++)
                    b.Set(x, y, DotMath.DitherRamp(new uint[] { 0x2E6A8Au, 0x24587Au, 0x1A4466u }, (double)(y - terrace) / 36, x, y));
            }
        }
        // 杜鹃丛：苔绿色的灌木，开花的带粉色小花
        var shrubs = new[] { 0.5, 0.88 };
        for (var k = 0; k < shrubs.Length; k++)
        {
            var x = (int)(b.Cols * shrubs[k]);
            for (var dy = 0; dy < 12; dy++)
                for (var dx = -9; dx <= 9; dx++)
                {
                    if (!((double)(dx * dx) / 90 + (double)((dy - 6) * (dy - 6)) / 40 < 1)) continue;
                    var c = DotMath.Pick(new uint[] { 0x6E8E34u, 0x5A7A2Au, 0x86A43Eu }, x + dx, dy, 6);
                    if (k == 0 && DotMath.Noise(x + dx, dy, 7) > 0.88) c = 0xD878C8u;
                    b.Set(x + dx, g - 1 - dy, c);
                }
            b.Fill(x - 1, g - 3, 2, 3, 0x5A4030u);
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 洞穴藤：从洞顶垂下，挂着发光的浆果，轻轻摆
        for (var i = 0; i < 14; i++)
        {
            var x0 = (int)(DotMath.Noise(i, 0, 61) * b.Cols);
            var len = 8 + (int)(DotMath.Noise(i, 1, 61) * g * 0.45);
            for (var dy = 0; dy < len; dy++)
            {
                var sway = RoundAway(Math.Sin(t * 0.9 + i) * dy * 0.06);
                b.Set(x0 + sway, dy, dy % 3 == 0 ? 0x5A8A2Au : 0x4A7A22u);
                if (dy % 6 == 4 && DotMath.Noise(i, dy, 62) > 0.4)
                {
                    var glow = Math.Sin(t * 2 + (i + dy)) > 0 ? 0xFFC04Au : 0xF59A2Au;
                    b.Set(x0 + sway + 1, dy, glow);
                    b.Set(x0 + sway + 1, dy + 1, glow);
                }
            }
        }
        // 孢子花落下的绿色孢子
        Paint.Falling(b, t, 22, new uint[] { 0x9AD06Au, 0x6EA43Eu }, 0, g, speed: 0.03, seed: 63);
        // 紫水晶一闪一闪
        var ax = (int)(b.Cols * 0.56);
        var ay = (int)(g * 0.42);
        if (Math.Sin(t * 3) > 0.7) { b.Set(ax, ay - 7, 0xFFFFFFu); b.Set(ax + 2, ay - 4, 0xF2E6FFu); }
        // 水塘：水面起伏，美西螈游来游去，发光鱿鱼的光斑一亮一暗
        var pl = Pool(b);
        var terrace = g - 38;
        for (var x = pl.Lo; x <= pl.Hi; x++)
            b.Set(x, terrace + (Math.Sin(x * 0.5 + t * 2) > 0.6 ? 0 : 1), 0x8FC4E8u);
        var ap = Paint.Mod(t / 9, 1);
        var right = ap < 0.5;
        var poolCount = pl.Hi - pl.Lo + 1;
        var axX = pl.Lo + 4 + (int)((double)(poolCount - 26) * (right ? ap * 2 : (1 - ap) * 2));
        b.Blit(Axolotl, axX, g - 26 + RoundAway(Math.Sin(t * 2.4) * 1), flip: !right);
        b.Blit(GlowSquid(t, Math.Sin(t * 1.4) > 0), pl.Lo + (int)(poolCount * 0.68), g - 3 - RoundAway(Math.Sin(t * 0.8) * 1.5));
        // 玩家：站在苔藓台子上，时不时举起望远镜
        var props = new PlayerProps { Held = HeldItem.Spyglass };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 7), (int)(b.Cols * 0.72), g - 1, t);
    }
}

/// 1.18 Caves & Cliffs II：高山与山羊。远处一层层带雪顶的灰色山峰，
/// 左边的山羊低头冲过来把玩家顶飞一下（玩家受击变红、往后退），然后退回去再来
public sealed class CavesAndCliffsII : IDotScene
{
    public string Id => "1.18";
    public uint Background => 0x8FB3FFu;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// Swift 的 .rounded() 是四舍五入远离零，C# 的 Math.Round 默认是银行家舍入
    private static int RoundAway(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// 山羊（朝右，约 30×21）：白色的毛，深灰色往后弯的角，下巴有胡子，蹄子是深色的
    public static Sprite Goat(bool headDown)
    {
        uint[] W = { 0xE8E6E0u, 0xD8D4CCu, 0xF2F0EAu };
        int[] legCols = { 4, 5, 9, 10, 17, 18, 21, 22 };
        return new Sprite(32, 22, (x, y) =>
        {
            // 身体
            if (x is >= 2 and <= 23 && y is >= 7 and <= 15) return y == 15 || x == 2 ? 0xC4C0B8u : DotMath.Pick(W, x, y, 3);
            // 腿与蹄子
            if (y is >= 16 and <= 21 && legCols.Contains(x)) return y >= 20 ? 0x3A3A3Au : DotMath.Pick(W, x, y, 4);
            // 头（低头冲撞时整个往下）
            var hy = headDown ? y - 4 : y;
            if (x is >= 22 and <= 30 && hy is >= 4 and <= 11)
            {
                if (hy == 7 && x == 28) return 0x1E1E1Du;
                if (x >= 29 && hy >= 9) return 0x4A4A4Au;
                return DotMath.Pick(W, x, hy, 5);
            }
            if (x is >= 24 and <= 26 && hy is >= 12 and <= 14) return 0xD8D4CCu;   // 胡子
            if ((x is >= 22 and <= 24 && hy is >= 0 and <= 3) || (x is >= 23 and <= 25 && hy == 4)) return hy < 2 ? 0x6E6E6Eu : 0x4A4A4Au;  // 角
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x7FA6F2u, 0xA5C1F6u, 0xD2E0FAu });
        // 三层山峰：越远越淡；尖峭，带雪顶
        var bases = new[] { 0.55, 0.72, 0.9 };
        for (var layer = 2; layer >= 0; layer--)
        {
            var haze = 0.55 + 0.15 * layer;
            for (var x = 0; x < b.Cols; x++)
            {
                var peaks = Math.Abs(Math.Sin(x * (0.045 + 0.01 * layer) + layer * 1.7));
                var h = (int)(g * (bases[layer] - 0.2) * peaks + DotMath.Noise(x / 3, layer, 7) * 6) + (int)(g * 0.18);
                var top = g - h;
                for (var y = Math.Max(0, top); y < g; y++)
                {
                    var d = y - top;
                    var c = d < 4 + (int)(DotMath.Noise(x, 0, 8 + layer) * 6)
                        ? DotMath.Pick(new uint[] { 0xF4F8FFu, 0xE6EEFAu }, x, y, 9)
                        : DotMath.Pick(new uint[] { 0x7A7A7Au, 0x8A8A8Au, 0x6E6E6Eu, 0x949494u }, x / 2, y / 2, 10 + layer);
                    if (layer < 2) c = DotMath.Dither(x, y, 1 - haze) ? 0xB4C8EAu : c;
                    b.Set(x, y, c);
                }
            }
        }
        // 近处的雪坡：左边高一点（山羊站的台子）
        for (var x = 0; x < b.Cols; x++)
        {
            var ledge = x < (int)(b.Cols * 0.3) ? 6 : 0;
            for (var y = g - ledge; y < b.Rows; y++)
            {
                var d = y - (g - ledge);
                var c = d < 5 ? DotMath.Pick(new uint[] { 0xF4F8FFu, 0xE6EEFAu, 0xFFFFFFu }, x, y, 11)
                              : DotMath.Pick(new uint[] { 0x8A8A8Au, 0x7A7A7Au, 0x949494u }, x, y, 12);
                b.Set(x, y, DotMath.Dither(x, y, (double)(y - g) / (b.Rows - g) * 0.7) ? DotMath.Shade(c, 0.6) : c);
            }
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, count: 3);
        Paint.Falling(b, t, 30, new uint[] { 0xFFFFFFu, 0xE6EEFAu }, 0, g, speed: 0.04, seed: 71);
        // 山羊冲撞（7 秒一轮）：站着 → 低头蓄力 → 冲过去 → 把玩家顶开 → 退回去
        var p = Paint.Mod(t, 7);
        var start = b.Cols * 0.1;
        var hit = b.Cols * 0.62;
        var gx = start;
        var headDown = false;
        if (p is >= 1.5 and <= 2.3) headDown = true;
        if (p is >= 2.3 and <= 2.9) { headDown = true; gx = start + (hit - start) * DotMath.Ramp(p, 2.3, 2.9); }
        if (p is >= 2.9 and <= 3.4) gx = hit;
        if (p is >= 3.4 and <= 5.5) gx = hit - (hit - start) * DotMath.Ramp(p, 3.4, 5.5);
        var onLedge = gx < b.Cols * 0.3 - 20;
        var hop = p is >= 2.3 and <= 2.9 ? (int)(Math.Abs(Math.Sin(p * 20)) * 2) : 0;
        b.Blit(Goat(headDown), (int)gx, g - 1 - (onLedge ? 6 : 0) - hop, flip: p is >= 3.4 and <= 5.5);
        // 玩家：被撞到时变红、往后退一截
        var pose = Player.Idle(t, new PlayerProps(), 8);
        if (p is >= 2.85 and <= 4.2)
        {
            pose.Hurt = Math.Max(0, 1 - (p - 2.85) / 0.3);
            pose.Dx = RoundAway(10 * DotMath.Ramp(p, 2.85, 3.2) * (1 - DotMath.Ramp(p, 3.6, 4.2)));
            if (p < 3.2) pose.Lift = RoundAway(Math.Sin((p - 2.85) / 0.35 * Math.PI) * 5);
        }
        Player.Draw(b, cast.Player, null, pose, (int)(b.Cols * 0.74), g - 1, t);
    }
}

/// 1.14 Village &amp; Pillage：掠夺者袭击村庄。村子里有房子、钟和一个村民；
/// 左边的劫掠兽驮着掠夺者冲进来，掠夺者举弩射击；玩家拿着弩还击，箭矢在中间飞
public sealed class VillageAndPillage : IDotScene
{
    public string Id => "1.14";
    public uint Background => 0x86AAF2u;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// 劫掠兽（朝右，约 46×34，对照原版）：暖深灰的大身子，毛的纹理是规则的竖纹而不是噪点；
    /// 大方头上一道更暗的眉带，绿眼睛，黑色的长鼻子垂到下巴下面，两侧伸出灰绿色的角；
    /// 背上是橙色的鞍，肩膀、屁股和四条腿上有浅青灰色的毛簇
    public static Sprite Ravager(int step)
    {
        const uint Base = 0x4A4644u, Light = 0x58534Fu, Dark = 0x3C3836u;
        uint[] tuft = { 0x9FB8B8u, 0xC4D4D4u, 0x7A9090u };
        uint Fur(int x, int y) => (x * 3 + y) % 9 == 0 ? Light : ((x + y * 2) % 11 == 0 ? Dark : Base);
        uint TuftC(int x, int y, int top) => y == top ? tuft[1] : ((x + y) % 4 == 0 ? tuft[2] : tuft[0]);
        var legs = new (int Lx, int Ph)[] { (3, 0), (11, 1), (21, 0), (28, 1) };
        return new Sprite(46, 34, (x, y) =>
        {
            // 鞍：橙色坐垫，深色边
            if (x is >= 12 and <= 26 && y is >= 3 and <= 6) return (x == 12 || x == 26 || y == 6) ? 0x3A2A22u : (y == 3 ? 0xF09A5Au : 0xE07A3Au);
            // 肩膀和屁股上的毛簇（浅青灰色的小方块）
            if ((x is >= 3 and <= 8 && y is >= 4 and <= 8) || (x is >= 28 and <= 33 && y is >= 4 and <= 8)) return TuftC(x, y, 4);
            // 身体
            if (x is >= 2 and <= 33 && y is >= 7 and <= 23) return (y == 23 || x == 2) ? Dark : (y == 7 ? Light : Fur(x, y));
            // 四条粗腿：上下两圈毛簇，走路时前后错开
            foreach (var (lx, ph) in legs)
            {
                var off = ph == step ? -1 : 0;
                if (x >= lx && x <= lx + 5 && y >= 24 && y <= 33 + off)
                {
                    if ((y is >= 24 and <= 25) || (y >= 30 + off && y <= 31 + off)) return TuftC(x, y, y < 28 ? 24 : 30 + off);
                    return y >= 32 + off ? 0x2E2A28u : Fur(x, y);
                }
            }
            // 头：在前面偏下；眉带、绿眼睛、黑色长鼻子
            if (x is >= 31 and <= 44 && y is >= 9 and <= 23)
            {
                // 头有一圈更暗的轮廓，和身体分开
                if (x == 31 || x == 44 || y == 9 || y == 23) return 0x2A2624u;
                if (y == 10) return Light;
                if (y is >= 13 and <= 14) return (x == 37 || x == 41) && y == 14 ? 0x3AA04Au : 0x2E2B2Au;
                if (x is >= 38 and <= 41 && y is >= 15 and <= 27) return x == 38 ? 0x161614u : 0x222220u;
                return x == 31 ? Dark : Fur(x, y);
            }
            if (x is >= 38 and <= 41 && y is >= 24 and <= 27) return x == 38 ? 0x161614u : 0x222220u;
            // 角：从头顶两侧往外、往后伸
            if ((x is >= 33 and <= 36 && y is >= 6 and <= 8) || (x is >= 30 and <= 33 && y is >= 4 and <= 6)) return y <= 4 ? 0x8A9692u : 0x6E7A76u;
            if (x is >= 42 and <= 45 && y is >= 8 and <= 10) return y == 8 ? 0x8A9692u : 0x6E7A76u;
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u });
        // 远处的山丘
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.03 + 2) * 0.5 + 0.5) * 16 + DotMath.Noise(x / 6, 0, 2) * 5);
            for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Pick(new uint[] { 0x5F8C4Eu, 0x547F45u, 0x6A9757u }, x, y, 3));
        }
        // 平原村庄的房子：圆石地基、橡木板墙、原木柱、玻璃窗、斜屋顶
        var hxs = new[] { 0.4, 0.7 };
        for (var k = 0; k < hxs.Length; k++)
        {
            var x0 = (int)(b.Cols * hxs[k]);
            var w = 30 + k * 6;
            const int h = 22;
            for (var y = g - h; y < g; y++)
            {
                for (var x = x0; x < x0 + w; x++)
                {
                    var isBase = y >= g - 4;
                    var post = x == x0 || x == x0 + w - 1;
                    var c = isBase ? DotMath.Pick(new uint[] { 0x7A7A7Au, 0x8A8A8Au, 0x6A6A6Au }, x, y, 4)
                        : (post ? 0x6B5133u : ((y - g) % 4 == 0 ? 0x9A7A4Au : DotMath.Pick(new uint[] { 0xB8945Au, 0xA8844Eu }, x, y, 5)));
                    if (!isBase && y - (g - h) > 5 && y - (g - h) < 13 && (x - x0) % 12 > 3 && (x - x0) % 12 < 9)
                        c = (x - x0) % 12 == 6 ? 0x6B5133u : 0xB4D8F0u;
                    b.Set(x, y, c);
                }
            }
            for (var r = 0; r < 10; r++)
                for (var x = x0 - 3 + r; x < x0 + w + 3 - r; x++)
                    b.Set(x, g - h - 1 - r, r % 2 == 0 ? 0x6B4A2Cu : 0x8A6238u);
        }
        // 钟：两根石柱，横梁上挂着金色的钟
        var bx = (int)(b.Cols * 0.6);
        b.Fill(bx, g - 22, 2, 22, 0x6E6E6Eu);
        b.Fill(bx + 12, g - 22, 2, 22, 0x6E6E6Eu);
        b.Fill(bx, g - 23, 14, 2, 0x4A3A2Au);
        for (var dy = 0; dy < 8; dy++)
        {
            var w = 4 + dy / 2;
            b.Fill(bx + 7 - w / 2, g - 20 + dy, w, 1, dy == 7 ? 0xC49A2Au : (dy % 3 == 0 ? 0xFFE27Au : 0xF5C840u));
        }
        // 草地与土路
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, 3, new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30,
                     new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, seed: 21);
        for (var x = (int)(b.Cols * 0.36); x < b.Cols; x++)
        {
            b.Set(x, g, DotMath.Pick(new uint[] { 0xA8844Eu, 0x9A7A4Au }, x, 0, 22));
            b.Set(x, g + 1, 0x8A6A3Au);
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, count: 3);
        // 营火的烟（从房子后面冒出来）
        for (var i = 0; i < 10; i++)
        {
            var q = Paint.Mod(t * 0.15 + (double)i / 10, 1);
            var x = (int)(b.Cols * 0.52) + (int)(Math.Sin(t + i) * (1 + q * 4));
            var y = g - 26 - (int)(q * (g - 30));
            if (DotMath.Dither(x, y, 1 - q)) b.Set(x, y, 0xB4B4B8u);
        }
        // 劫掠兽驮着掠夺者，原地踏步、一冲一冲的
        var p = Paint.Mod(t, 6);
        var charge = p is >= 1.0 and <= 1.6 ? DotMath.Ramp(p, 1.0, 1.3) * (1 - DotMath.Ramp(p, 1.3, 1.6)) : 0;
        var rx = (int)(b.Cols * 0.04 + charge * 16);
        var step = (int)(t * 4) % 2;
        b.Blit(Ravager(step), rx, g - 1 - (step == 0 ? 1 : 0));
        var rider = new PlayerPose();
        rider.Sitting = true;
        rider.ArmAngle = 1.5;
        // 弩端平，指向前方：物品角度 = 手臂角度 − π/2 + 0.45 + tilt = −π/2
        rider.ItemTilt = -(rider.ArmAngle + 0.45);
        // 掠夺者骑在鞍上：坐姿的大腿压在鞍上，小腿垂在身体侧面
        Player.Draw(b, PlayerLook.Pillager, new PlayerProps { Held = HeldItem.Crossbow }, rider, rx + 20, g - 34 + 3 + 10 - (step == 0 ? 1 : 0), t + 2, facingRight: true);
        // 村民在钟旁边抱着手，被吓得来回张望
        var vpose = new PlayerPose();
        vpose.ArmAngle = 0;
        Player.Draw(b, PlayerLook.Villager, null, vpose, (int)(b.Cols * 0.6) + 20, g - 1, t + 4, Math.Sin(t * 1.1) > 0.3);
        // 双方的弩箭
        void Bolt(int x0, int x1, int y, double q)
        {
            if (!(q >= 0 && q < 1)) return;
            var x = x0 + (int)((double)(x1 - x0) * q);
            for (var k = 0; k < 6; k++) b.Set(x + (x1 > x0 ? -k : k), y, k == 0 ? 0xC4C8CEu : 0x6B4A2Cu);
        }
        var px = (int)(b.Cols * 0.82);
        Bolt(rx + 30, px - 6, g - 26, (p - 2.0) / 0.8);
        Bolt(px - 8, rx + 34, g - 24, (p - 4.0) / 0.8);
        // 玩家：举着弩瞄准，射出去时手往后一顿
        var props = new PlayerProps { Held = HeldItem.Crossbow };
        var pose = Player.Idle(t, props, 9);
        pose.ArmAngle = 1.5;
        if (p is >= 4.0 and <= 4.2) { pose.ArmAngle = 1.38; pose.Dx = 1; }
        pose.ItemTilt = -(pose.ArmAngle + 0.45);
        if (p is >= 2.75 and <= 3.1) pose.Hurt = 1 - (p - 2.75) / 0.35;
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

/// 1.15 Buzzy Bees：蜜蜂与蜂巢。花田里一棵橡树上挂着蜂巢，蜂蜜往下滴，
/// 蜜蜂带着花粉飞来飞去；树下点着营火让蜜蜂安静，玩家拿着蜂蜜瓶，时不时举起来看看收的蜂蜜
public sealed class BuzzyBees : IDotScene
{
    public string Id => "1.15";
    public uint Background => 0x86AAF2u;

    private static int Ground(DotBuffer b) => (int)(b.Rows * 0.64);

    /// 蜜蜂（朝右，约 9×7）：黄黑条纹、两片白色的翅膀、深色的眼睛
    public static Sprite Bee(bool flap, bool pollen)
    {
        return new Sprite(
            flap
                ? new[]
                {
                    "...ww.ww.", "...ww.ww.", ".YYBYYBYY", "YYYBYYBkY", "YYYBYYBYY", ".YYBYYBYY", pollen ? "..p..p..." : ".........",
                }
                : new[]
                {
                    ".........", "..www.ww.", ".YYBYYBYY", "YYYBYYBkY", "YYYBYYBYY", ".YYBYYBYY", pollen ? "..p..p..." : ".........",
                },
            new Dictionary<char, uint> { ['Y'] = 0xF2C640u, ['B'] = 0x2A2A2Au, ['w'] = 0xE8F4F8u, ['k'] = 0x1E1E1Du, ['p'] = 0xFFE27Au });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u });
        // 橡树和挂在树枝上的蜂巢（黄色带棕色环纹的方块，中间一个洞）
        var tx = (int)(b.Cols * 0.3);
        b.Fill(tx, g - 44, 5, 44, 0x6B5133u);
        for (var y = g - 74; y < g - 38; y++)
            for (var x = tx - 26; x < tx + 30; x++)
            {
                if (!((double)((x - tx - 2) * (x - tx - 2)) / 760 + (double)((y - g + 56) * (y - g + 56)) / 300 < 1 && DotMath.Noise(x, y, 3) > 0.1)) continue;
                b.Set(x, y, DotMath.Pick(new uint[] { 0x3E7A2Cu, 0x4A8A34u, 0x356A26u }, x, y, 4));
            }
        var nx = tx + 8;
        var ny = g - 38;
        for (var dy = 0; dy < 14; dy++)
            for (var dx = 0; dx < 14; dx++)
            {
                var ring = Math.Max(Math.Abs(dx - 7), Math.Abs(dy - 7));
                var c = ring % 3 == 0 ? 0x8A5A2Au : (ring % 3 == 1 ? 0xF2C640u : 0xE0AE30u);
                if (dx is >= 5 and <= 8 && dy is >= 8 and <= 11) c = 0x3A2412u;
                b.Set(nx + dx, ny + dy, c);
            }
        // 花田：各色小花
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, 3, new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30,
                     new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, seed: 23);
        uint[] flowers = { 0xF2E64Au, 0xE53B2Cu, 0xF4F4F4u, 0x8E6FD6u, 0xF29A3Au, 0x4A78EEu };
        for (var x = 2; x < b.Cols; x += 5)
        {
            if (DotMath.Noise(x, 0, 24) <= 0.3) continue;
            var h = 2 + (int)(DotMath.Noise(x, 1, 24) * 4);
            for (var dy = 0; dy < h; dy++) b.Set(x, g - 1 - dy, 0x3E8A2Au);
            var c = flowers[(int)(DotMath.Noise(x, 2, 24) * flowers.Length) % flowers.Length];
            b.Set(x, g - 1 - h, c);
            b.Set(x - 1, g - h, c);
            b.Set(x + 1, g - h, c);
        }
        // 营火
        var cx = (int)(b.Cols * 0.32);
        b.Fill(cx - 7, g - 3, 15, 3, 0x6B4A2Cu);
        b.Fill(cx - 5, g - 5, 11, 2, 0x8A5A33u);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, count: 3);
        var tx = (int)(b.Cols * 0.3);
        var nx = tx + 8;
        var ny = g - 38;
        // 蜂蜜从蜂巢底部往下滴
        var dp = Paint.Mod(t, 2.4) / 2.4;
        b.Set(nx + 7, ny + 14 + (int)(dp * dp * (g - ny - 18)), 0xF5A42Eu);
        // 营火与烟
        var cx = (int)(b.Cols * 0.32);
        Paint.Flame(b, cx - 5, g - 6, 11, 9, t);
        for (var i = 0; i < 10; i++)
        {
            var q = Paint.Mod(t * 0.2 + (double)i / 10, 1);
            var x = cx + (int)(Math.Sin(t + i) * (1 + q * 5));
            var y = g - 14 - (int)(q * 30);
            if (DotMath.Dither(x, y, 1 - q)) b.Set(x, y, 0xC8C8CCu);
        }
        // 蜜蜂：在蜂巢和花丛之间绕着飞，有的带着花粉
        for (var i = 0; i < 5; i++)
        {
            var a = t * (0.9 + i * 0.13) + i * 1.3;
            var x = (double)nx + Math.Cos(a) * (18 + i * 9) + i * 6;
            var y = (double)ny + 6 + Math.Sin(a * 1.7) * (8 + i * 2) + i * 3;
            b.Blit(Bee(Math.Sin(t * 22 + i) > 0, i % 2 == 0), (int)x, (int)y, flip: -Math.Sin(a) < 0);
        }
        // 玩家：拿着蜂蜜瓶
        var props = new PlayerProps { Held = HeldItem.HoneyBottle };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 10), (int)(b.Cols * 0.7), g - 1, t);
    }
}
