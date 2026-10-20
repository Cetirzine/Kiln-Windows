namespace Kiln.Core.DotScene;

// 1.0 到 1.11 的老版本（对照原版 DotSceneClassic.swift）
//
// 画法与 Scenes.cs 相同：先查 minecraft.wiki 的渲染图和贴图，按真实尺寸（1 格 = 16 点）画。
// 只有末影龙太大（16 格长）放不进画面，画成远处盘旋的样子。

// 1.0 Adventure Update：末地与末影龙

/// 1.0 正式版加入了末地、末影龙、附魔台和酿造台。末地的主岛：黑曜石柱顶上的末影水晶转着圈，
/// 末影龙在远处的天上盘旋，飞近水晶时被一道光束治疗；末影人在柱子间晃悠；玩家拿着附魔过的钻石剑（剑身流过一道紫光）
public sealed class TheEnd : IDotScene
{
    public string Id => "1.0";
    public uint Background => 0x0C0814u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.68);

    private (int X, int H)[] Pillars(DotBuffer b) => new[]
    {
        ((int)(b.Cols * 0.08), 62), ((int)(b.Cols * 0.3), 48), ((int)(b.Cols * 0.5), 70), ((int)(b.Cols * 0.92), 54),
    };

    /// 末影龙（侧面，朝右）：黑色的长身子、长脖子、长尾巴，背上一排灰色的骨刺，紫色的眼睛；
    /// 翅膀是深灰的膜，flap 为扇动的相位（-1 向下，1 向上）。远处的龙按 scale 缩小
    public static void Dragon(DotBuffer b, int x, int y, double flap, bool facingRight)
    {
        uint[] body = { 0x1A1A1Eu, 0x141418u, 0x222226u, 0x0E0E10u };
        void Put(int dx, int dy, uint c) => b.Set(x + (facingRight ? dx : -dx), y + dy, c);

        /// 翅膀：肩膀、翼尖、后缘三点围成的一片膜，骨架是从肩膀到翼尖的灰线，膜上几道细骨
        void Wing((double X, double Y) tip, (double X, double Y) back, double tone)
        {
            var a = (X: 2.0, Y: -4.0);
            var minX = (int)Math.Min(Math.Min(a.X, tip.X), back.X);
            var maxX = (int)Math.Max(Math.Max(a.X, tip.X), back.X);
            var minY = (int)Math.Min(Math.Min(a.Y, tip.Y), back.Y);
            var maxY = (int)Math.Max(Math.Max(a.Y, tip.Y), back.Y);
            double Side((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) =>
                (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            for (var py = minY; py <= maxY; py++)
                for (var px = minX; px <= maxX; px++)
                {
                    var pt = ((double)px, (double)py);
                    var d1 = Side(a, tip, pt);
                    var d2 = Side(tip, back, pt);
                    var d3 = Side(back, a, pt);
                    var inside = (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
                    if (inside) Put(px, py, DotMath.Shade(DotMath.Noise(px, py, 2) < 0.15 ? 0x3A3A46u : 0x2E2E38u, tone));
                }
            for (var k = 0; k <= 30; k++)
            {
                var f = (double)k / 30;
                Put((int)(a.X + (tip.X - a.X) * f), (int)(a.Y + (tip.Y - a.Y) * f), DotMath.Shade(0x6A6A72u, tone));
                foreach (var rib in new[] { 0.45, 0.75 })
                {
                    if (k % 2 != 0) continue;
                    var s0x = a.X + (tip.X - a.X) * rib;
                    var s0y = a.Y + (tip.Y - a.Y) * rib;
                    var ex = s0x + (back.X - s0x) * f * 0.9;
                    var ey = s0y + (back.Y - s0y) * f * 0.9;
                    Put((int)ex, (int)ey, DotMath.Shade(0x4A4A54u, tone));
                }
            }
        }

        // 远处那片翅膀（暗一点）
        var up = 0.3 + 0.7 * flap;
        Wing((0, -6 - 26 * up), (-26, -4), 0.65);
        // 尾巴：往后渐细，末端微微摆动，背上一排骨刺
        for (var k = 0; k < 40; k++)
        {
            var tx = -16 - k;
            var ty = (int)Math.Round(Math.Sin(k * 0.16 + flap) * 2, MidpointRounding.AwayFromZero);
            var w = k < 10 ? 3 : (k < 26 ? 2 : 1);
            for (var dy = -w; dy < w; dy++) Put(tx, ty + dy, DotMath.Pick(body, k, dy, 1));
            if (k % 4 == 0) Put(tx, ty - w - 1, 0x6A6A70u);
        }
        // 身子
        for (var dx = -16; dx <= 14; dx++)
            for (var dy = -6; dy <= 5; dy++)
                if ((double)(dx * dx) / 240 + (double)(dy * dy) / 34 < 1)
                    Put(dx, dy, DotMath.Pick(body, dx, dy, 2));
        for (var dx = -14; dx <= 12; dx += 4)
        {
            Put(dx, -7, 0x6A6A70u);
            Put(dx, -8, 0x5A5A60u);
        }
        // 脖子往前上方，头是长方的，下巴一道缝，紫色的眼睛
        for (var k = 0; k < 14; k++)
            for (var dy = -2; dy <= 2; dy++)
                Put(14 + k, -3 - k / 3 + dy, DotMath.Pick(body, k, dy, 3));
        for (var dx = 0; dx < 14; dx++)
            for (var dy = -4; dy <= 2; dy++)
                Put(28 + dx, -9 + dy, dx > 6 && dy == 1 ? 0x0A0A0Cu : DotMath.Pick(body, dx, dy, 4));
        for (var k = 15; k < 28; k += 4) Put(k, -6 - (k - 14) / 3, 0x6A6A70u);
        Put(31, -14, 0x6A6A70u);
        Put(34, -14, 0x6A6A70u);
        Put(32, -11, 0xD06AF0u);
        Put(33, -11, 0xE8A8FFu);
        // 腿缩在身下
        foreach (var (lx, ly) in new[] { (-8, 6), (-8, 7), (-7, 8), (8, 6), (8, 7), (9, 8) }) Put(lx, ly, 0x141418u);
        // 近处的翅膀
        Wing((-6, -6 - 34 * up), (-34, -1), 1);
    }

    /// 末影水晶（8×8，转着的玻璃框里一块粉色的核心）
    public static void Crystal(DotBuffer b, int x, int y, double t)
    {
        var k = (int)(t * 6) % 4;
        for (var dy = 0; dy < 8; dy++)
            for (var dx = 0; dx < 8; dx++)
            {
                var edge = dx == 0 || dy == 0 || dx == 7 || dy == 7;
                var core = dx is >= 2 and <= 5 && dy is >= 2 and <= 5;
                if (edge && (dx + dy + k) % 3 == 0) b.Set(x + dx, y + dy, 0xE8F0FFu);
                else if (core) b.Set(x + dx, y + dy, (dx + dy + k) % 2 == 0 ? 0xE85AD8u : 0xB83AB0u);
                else if (edge) b.Set(x + dx, y + dy, 0xA8B8E0u);
            }
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 末地的天：深紫黑，带一点点杂色
        for (var y = 0; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
            {
                var n = DotMath.Noise(x / 2, y / 2, 90);
                b.Set(x, y, n < 0.08 ? 0x2A1E3Au : (n < 0.3 ? 0x16101Eu : 0x0E0A16u));
            }
        // 远处的小浮岛
        foreach (var (cx, cy, w) in new[] { (0.66, 0.3, 26), (0.2, 0.18, 16) })
        {
            var x0 = (int)(b.Cols * cx);
            var y0 = (int)(g * cy);
            for (var dy = 0; dy < Math.Min(6, w / 4); dy++)
                for (var dx = dy * 2; dx < w - dy * 2; dx++)
                    b.Set(x0 + dx, y0 + dy, dy == 0 ? 0xC8C88Eu : 0x8A8A60u);
        }
        // 黑曜石柱
        foreach (var (x, h) in Pillars(b))
        {
            for (var y = g - h; y < g; y++)
                for (var dx = 0; dx < 14; dx++)
                {
                    var c = DotMath.Pick(new uint[] { 0x14101Cu, 0x241A30u, 0x0E0A14u, 0x2E2040u }, x + dx, y, 91);
                    b.Set(x + dx, y, dx == 0 ? 0x3A2A4Eu : (dx == 13 ? 0x0A0810u : c));
                }
            // 柱顶的基岩
            b.Fill(x + 3, g - h - 2, 8, 2, 0x4A4A4Au);
        }
        // 主岛：末地石
        Paint.Strata(b, g, new uint[] { 0xDCDEA0u, 0xE8E8B0u, 0xC8C88Eu, 0xD4D49Au }, 20,
            new uint[] { 0xC8C88Eu, 0xB8B880u, 0xD0D096u }, 16, new uint[] { 0xA8A876u, 0x98986Au }, 92);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 水晶
        foreach (var (x, h) in Pillars(b))
            Crystal(b, x + 3, g - h - 11 + (int)Math.Round(Math.Sin(t * 2 + x) * 1.5, MidpointRounding.AwayFromZero), t);
        // 末影龙：在远处的天上来回盘旋（14 秒一圈），飞到第三根柱子上方时水晶射出治疗光束
        var p = Paint.Mod(t, 14) / 14;
        var right = p < 0.5;
        var q = right ? p * 2 : (p - 0.5) * 2;
        var dx = right ? -40 + q * (b.Cols + 80) : (b.Cols + 40) - q * (b.Cols + 80);
        var dy = g * 0.34 + Math.Sin(q * Math.PI * 2) * 10;
        var flap = Math.Sin(t * 3.2);
        Dragon(b, (int)dx, (int)dy, flap, right);
        var (cx, ch) = Pillars(b)[2];
        if (Math.Abs(dx - cx) < 40)
        {
            var sx = (double)(cx + 7);
            var sy = (double)(g - ch - 7);
            for (var k = 0; k < 60; k++)
            {
                var f = (double)k / 60;
                var x = (int)(sx + (dx - sx) * f);
                var y = (int)(sy + (dy - sy) * f);
                if ((k + (int)(t * 20)) % 3 != 0) b.Set(x, y, k % 2 == 0 ? 0xF0C8FFu : 0xD06AF0u);
            }
        }
        // 末影人：在柱子之间慢慢走，偶尔瞬移（紫色粒子）
        var ep = Paint.Mod(t, 10);
        var ex = (int)(b.Cols * (ep < 5 ? 0.18 : 0.4)) + (int)(Math.Sin(t * 0.5) * 6);
        var enderman = new Sprite(8, 46, (x, y) =>
        {
            if (y < 8) return (y == 4 && (x == 1 || x == 2 || x == 5 || x == 6)) ? (x == 2 || x == 5 ? 0xE8A8FFu : 0xC86AE0u) : 0x141414u;
            if (y < 20) return x is >= 2 and <= 5 ? 0x0E0E0Eu : ((x == 0 || x == 7) ? 0x161616u : (uint?)null);
            if (y < 30 && (x == 0 || x == 7)) return 0x161616u;
            if ((x == 2 || x == 5) && y >= 20) return 0x121212u;
            return null;
        });
        b.Blit(enderman, ex, g - 1);
        if ((ep >= 4.6 && ep <= 5.4) || ep > 9.6 || ep < 0.4)
        {
            for (var i = 0; i < 14; i++)
                b.Set(ex - 4 + (int)(DotMath.Noise(i, (int)(t * 8), 93) * 16), g - 4 - (int)(DotMath.Noise(i, (int)(t * 8), 94) * 46), 0xC86AE0u);
        }
        // 附魔钻石剑：剑身上流过一道紫光
        var band = (int)(t * 8) % 18;
        var sword = HeldItem.DiamondSword.Sprite;
        var glint = new Sprite(sword.Width, sword.Height, (x, y) =>
        {
            var v = sword.Px[y * sword.Width + x];
            if (v == 0) return null;
            return Math.Abs((x + y) - band) < 2 ? 0xC88AF8u : v & 0xFFFFFFu;
        });
        var props = new PlayerProps { Held = new HeldItem(glint) };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 2), (int)(b.Cols * 0.76), g - 1, t);
    }
}

// 1.2：丛林与豹猫

/// 1.2 加入了丛林（高大的丛林木、藤蔓、可可豆）和豹猫，用生鱼可以把豹猫驯服成猫。
/// 丛林里：豹猫从灌木后面探出头，压低身子一点点挪到拿着生鳕鱼、一动不动的玩家跟前，冒出爱心，变成一只虎斑猫
public sealed class JungleUpdate : IDotScene
{
    public string Id => "1.2";
    public uint Background => 0x1E3A1Au;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 豹猫（朝右，22×11，约 0.7 格高）：黄色带棕色斑点，绿眼睛，细长的尾巴；crouch 时压低身子；cat 时换成虎斑猫的颜色
    public static Sprite Ocelot(bool crouch, int step, bool cat)
    {
        uint fur = cat ? 0xA8A8A8u : 0xF0C860u;
        uint spot = cat ? 0x5A5A5Au : 0xB07A3Au;
        uint belly = cat ? 0xD8D8D8u : 0xF8E0A0u;
        var low = crouch ? 2 : 0;
        return new Sprite(22, 12, (x, y) =>
        {
            var yy = y - low;
            // 尾巴：从屁股往后上翘
            if (x <= 4 && yy >= 1 && yy <= 4 && (4 - x) == yy - 1) return x % 2 == 0 ? fur : spot;
            // 身子 12×4
            if (x is >= 4 and <= 15 && yy is >= 4 and <= 7)
            {
                if (yy == 7) return belly;
                return DotMath.Noise(x, yy, cat ? 96 : 95) < 0.3 ? spot : fur;
            }
            // 头 5×5，耳朵竖着
            if (x is >= 15 and <= 19 && yy is >= 2 and <= 6)
            {
                if (yy == 4 && x == 18) return 0x3A9A3Au;
                if (yy == 6 && x == 19) return 0x6A4A3Au;
                return fur;
            }
            if ((x == 16 || x == 18) && yy == 1) return spot;
            if (x == 20 && yy is >= 5 and <= 6) return belly;
            // 腿
            var legXs = new[] { 5, 7, 13, 15 };
            for (var i = 0; i < legXs.Length; i++)
            {
                var lx = legXs[i];
                if (x != lx) continue;
                var up = i % 2 == step ? 1 : 0;
                if ((yy >= 8 && yy <= 11 - up) || ((y >= 8 && y <= 11 - up) && y >= 8 + low))
                    return y == 11 - up ? belly : fur;
            }
            return null;
        });
    }

    public static readonly HeldItem RawCod = new(
        new Sprite(
            new[]
            {
                "..ff....", ".fFFFF..", "fFkFFFFt", ".FFFFFtt", "..ff..t.",
            },
            new Dictionary<char, uint> { ['f'] = 0xC8A880u, ['F'] = 0xB09070u, ['k'] = 0x1E1E1Du, ['t'] = 0x8A7058u }),
        swings: false);

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x3E7AA8u, 0x5A9AC0u, 0x86BCD8u, 0xB4D8E4u });
        // 远处密密的树冠
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)(g * 0.55 + Math.Sin(x * 0.06) * 8 + DotMath.Noise(x / 5, 0, 97) * 10);
            for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Dither(x, y, 0.5) ? 0x2E5A22u : 0x26501Cu);
        }
        // 高大的丛林木：2×2 的树干，藤蔓垂下来，树干上挂着可可豆
        var trunkCxs = new[] { 0.08, 0.42, 0.97 };
        for (var k = 0; k < trunkCxs.Length; k++)
        {
            var cx = trunkCxs[k];
            var x0 = (int)(b.Cols * cx) - 8;
            for (var y = 0; y < g; y++)
                for (var dx = 0; dx < 16; dx++)
                {
                    var c = DotMath.Pick(new uint[] { 0x56441Eu, 0x6E5A2Au, 0x4A3A18u, 0x625020u }, dx / 2, y / 2, 98 + k);
                    b.Set(x0 + dx, y, dx == 0 ? 0x3A2E12u : c);
                }
            foreach (var dx in new[] { -2, 17 })
                for (var y = 0; y < g - 10 - k * 6; y++)
                    if (DotMath.Noise(x0 + dx, y / 3, 99) > 0.25)
                        b.Set(x0 + dx, y, DotMath.Pick(new uint[] { 0x3E8E26u, 0x2E7A1Eu }, dx, y, 100));
            var podCys = new[] { g - 40, g - 58 };
            for (var i = 0; i < podCys.Length; i++)
            {
                if (!(k != 1 || i == 0)) continue;
                var cy = podCys[i];
                b.Fill(x0 + 16, cy, 4, 6, 0xB0602Eu);
                b.Fill(x0 + 16, cy, 4, 1, 0x8A6A2Au);
                b.Set(x0 + 17, cy + 2, 0xD08040u);
            }
        }
        // 顶上的树叶
        for (var x = 0; x < b.Cols; x++)
        {
            var h = 16 + (int)(Math.Sin(x * 0.11) * 4 + DotMath.Noise(x / 3, 1, 101) * 6);
            for (var y = 0; y < h; y++)
                b.Set(x, y, DotMath.Pick(new uint[] { 0x2E7A1Eu, 0x3E8E26u, 0x50A030u, 0x267018u }, x, y, 102));
            if (DotMath.Noise(x, 2, 103) > 0.86)
                for (var y = h; y < h + 8 + (int)(DotMath.Noise(x, 3, 103) * 26); y++)
                    b.Set(x, y, DotMath.Pick(new uint[] { 0x3E8E26u, 0x2E7A1Eu }, x, y, 104));
        }
        // 地面：草和灌木
        Paint.Strata(b, g, new uint[] { 0x4F8F35u, 0x5DA03Fu, 0x3E7A2Cu }, 4,
            new uint[] { 0x6F4627u, 0x8A5A33u, 0x5E3C22u }, 30, new uint[] { 0x6E6E6Eu, 0x7F7F7Fu }, 105);
        foreach (var (cx, w) in new[] { (0.18, 26), (0.58, 18) })
        {
            var x0 = (int)(b.Cols * cx);
            for (var dy = 0; dy < 12; dy++)
                for (var dx = 0; dx < w; dx++)
                    if ((double)((dx - w / 2) * (dx - w / 2)) / (w * w / 4) + (double)(dy * dy) / 144 < 1)
                        b.Set(x0 + dx, g - 1 - dy, DotMath.Pick(new uint[] { 0x3E8E26u, 0x50A030u, 0x2E7A1Eu }, dx, dy, 106));
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Falling(b, t, 10, new uint[] { 0x50A030u, 0x3E8E26u }, 20, g, 0.03, 107);
        // 鹦鹉一样的小飞虫……这里只有几只蝴蝶似的光点
        var px = (int)(b.Cols * 0.76);
        // 时间线（16 秒一轮）：探头 → 压低身子挪过去 → 爱心 → 变成猫，坐在玩家旁边 → 跑回灌木
        var p = Paint.Mod(t, 16);
        var hide = b.Cols * 0.2;
        var near = (double)(px - 24);
        var ox = hide;
        var crouch = true;
        var cat = false;
        var flip = false;
        var step = 2;
        if (p < 2) ox = hide;
        else if (p < 7)
        {
            ox = hide + (near - hide) * (p - 2) / 5;
            step = (int)(t * 3) % 2;
        }
        else if (p < 9) ox = near;
        else if (p < 13)
        {
            ox = near;
            cat = true;
            crouch = false;
        }
        else
        {
            ox = near - (near - hide) * DotMath.Ease((p - 13) / 3);
            cat = true;
            crouch = false;
            flip = true;
            step = (int)(t * 6) % 2;
        }
        b.Blit(Ocelot(crouch, step, cat), (int)ox, g - 1, flip: flip);
        // 灌木挡住一半（探头的时候）
        if (p < 2)
        {
            var x0 = (int)(b.Cols * 0.18);
            for (var dy = 0; dy < 10; dy++)
                for (var dx = 0; dx < 16; dx++)
                    if ((double)((dx - 13) * (dx - 13)) / 169 + (double)(dy * dy) / 100 < 1)
                        b.Set(x0 + dx, g - 1 - dy, DotMath.Pick(new uint[] { 0x3E8E26u, 0x50A030u }, dx, dy, 106));
        }
        // 爱心
        if (p >= 7.4 && p <= 10)
        {
            for (var i = 0; i < 3; i++)
            {
                var f = Paint.Mod((p - 7.4) * 0.8 + i * 0.33, 1);
                var hx = (int)ox + 14 + i * 3;
                var hy = g - 14 - (int)(f * 14);
                b.Blit(new Sprite(new[] { ".r.r.", "rRrRr", "rRRRr", ".rRr.", "..r.." },
                    new Dictionary<char, uint> { ['r'] = 0xC81E2Eu, ['R'] = 0xF0384Au }), hx, hy);
            }
        }
        var props = new PlayerProps { Held = RawCod };
        var pose = Player.Idle(t, props, 4);
        if (p >= 1.5 && p <= 9)
        {
            pose.ArmAngle = 0.7;
            pose.LookBack = false;
            pose.HeadDip = 1;
        }
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

// 1.3：沙漠神殿与绿宝石交易

/// 1.3 加入了沙漠神殿、丛林神殿、绿宝石和村民交易、末影箱、绊线。
/// 沙漠神殿前：玩家递出一颗绿宝石，村民接过去，换回一本附魔书，头上冒出绿色的开心粒子
public sealed class DesertTrade : IDotScene
{
    public string Id => "1.3";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 引擎预置的村民没设 HeadFrontBlink（原版 PlayerLook 构造时默认取 headFront），补上以免眨眼时取到空值
    private static readonly PlayerLook VillagerLook = WithBlink(PlayerLook.Villager);

    private static PlayerLook WithBlink(PlayerLook l) =>
        l.HeadFrontBlink != null ? l : new PlayerLook
        {
            HeadFront = l.HeadFront,
            HeadFrontBlink = l.HeadFront,
            HeadSide = l.HeadSide,
            BodyFront = l.BodyFront,
            BodySide = l.BodySide,
            NearArm = l.NearArm,
            FarArm = l.FarArm,
            Legs = l.Legs,
            Nose = l.Nose,
            CrossedArms = l.CrossedArms,
        };

    public static readonly Sprite Emerald = new(
        new[]
        {
            "..ggg..", ".gLGGg.", "gLGGGGg", "gGGGGDg", ".gGGDg.", "..ggg..",
        },
        new Dictionary<char, uint> { ['g'] = 0x0A7A2Eu, ['L'] = 0xA8F8C8u, ['G'] = 0x18D058u, ['D'] = 0x0E9A3Au });

    public static readonly Sprite Book = new(
        new[]
        {
            "bbbbbbb", "bPPPPPb", "bPpPpPb", "bPPPPPb", "bbbbbbb",
        },
        new Dictionary<char, uint> { ['b'] = 0x8A3A2Au, ['P'] = 0xB85AC8u, ['p'] = 0xE8A8F0u });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u, 0xD8E0E8u });
        // 沙漠神殿：砂岩的台阶状主体，两座塔，塔门上是橙色陶瓦拼的图案
        uint[] sand = { 0xE0D2A0u, 0xD8C890u, 0xE8DCAEu, 0xCFC088u };
        var x0 = (int)(b.Cols * 0.04);
        var w = 120;
        for (var step = 0; step < 4; step++)
        {
            var inset = step * 10;
            var top = g - 16 - step * 8;
            for (var y = top; y < top + 8; y++)
                for (var x = x0 + 26 + inset; x < x0 + w - 26 - inset; x++)
                    b.Set(x, y, y == top ? 0xF0E6C0u : DotMath.Pick(sand, x / 4, y / 4, 110));
        }
        b.Fill(x0 + 26, g - 16, w - 52, 16, 0xD8C890u);
        foreach (var tower in new[] { x0, x0 + w - 28 })
        {
            for (var y = g - 54; y < g; y++)
                for (var x = tower; x < tower + 28; x++)
                {
                    var c = DotMath.Pick(sand, x / 4, y / 4, 111);
                    if ((y - (g - 54)) % 16 == 0) c = 0xC8B880u;
                    b.Set(x, y, c);
                }
            b.Fill(tower - 2, g - 56, 32, 3, 0xF0E6C0u);
            // 塔上的陶瓦图案
            var pat = new Sprite(new[] { "..oo..", ".oOOo.", "oOwwOo", ".oOOo.", "..oo..", "..oo..", ".oooo." },
                new Dictionary<char, uint> { ['o'] = 0xB0602Eu, ['O'] = 0xD9844Au, ['w'] = 0x2E6EA8u });
            b.Blit(pat, tower + 11, g - 34);
            b.Fill(tower + 10, g - 18, 8, 18, 0x4A3A22u);
        }
        // 沙地
        Paint.Strata(b, g, new uint[] { 0xDBCFA3u, 0xE3D7AEu, 0xCFC294u }, 16,
            new uint[] { 0xD8C78Eu, 0xC9B67Au }, 16, new uint[] { 0xB5A26Au, 0xA69360u }, 112);
        foreach (var x in new[] { (int)(b.Cols * 0.62), (int)(b.Cols * 0.98) })
        {
            b.Fill(x, g - 16, 4, 16, 0x3E7A2Cu);
            for (var y = g - 15; y < g; y += 3) b.Set(x + 1, y, 0x2E5A22u);
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, 3);
        var px = (int)(b.Cols * 0.76);
        var vx = (int)(b.Cols * 0.56);
        // 时间线（8 秒一轮）
        var p = Paint.Mod(t, 8);
        var vpose = new PlayerPose();
        vpose.HeadDip = p >= 2.6 && p <= 3.6 ? 1 : 0;
        Player.Draw(b, VillagerLook, null, vpose, vx, g - 1, t + 1.7, true);
        // 绿宝石从玩家手里飞到村民手里，书飞回来
        var handPx = (double)(px - 4);
        var handPy = (double)(g - 18);
        var handVx = (double)(vx + 12);
        var handVy = (double)(g - 18);
        if (p >= 1.4 && p <= 2.6)
        {
            var f = DotMath.Ease((p - 1.4) / 1.2);
            b.Blit(Emerald, (int)(handPx + (handVx - handPx) * f), (int)(handPy - Math.Sin(f * Math.PI) * 8));
        }
        if (p >= 3.6 && p <= 4.8)
        {
            var f = DotMath.Ease((p - 3.6) / 1.2);
            b.Blit(Book, (int)(handVx + (handPx - handVx) * f), (int)(handVy - Math.Sin(f * Math.PI) * 8));
        }
        if (p >= 2.6 && p <= 4.4)
        {
            for (var i = 0; i < 6; i++)
            {
                var f = Paint.Mod(p - 2.6 + i * 0.3, 1);
                b.Set(vx + (int)(DotMath.Noise(i, 0, 113) * 12) - 1, g - 34 - (int)(f * 10), i % 2 == 0 ? 0x3AE05Au : 0x9AF8A8u);
            }
        }
        HeldItem? held = p < 1.4 ? new HeldItem(Emerald, swings: false) : (p > 4.8 && p < 7.4 ? new HeldItem(Book, swings: false) : null);
        var props = new PlayerProps { Held = held };
        var pose = Player.Idle(t, props, 0);
        if (p < 1.6 || (p >= 4.6 && p <= 6.2))
        {
            pose.ArmAngle = 0.9;
            pose.ItemUp = 0.4;
        }
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
    }
}

// 1.4 Pretty Scary Update：凋灵、蝙蝠和烟花

/// 1.4 “相当恐怖的更新”加入了凋灵、女巫、蝙蝠、南瓜派、信标和烟花火箭（1.4.6）。
/// 万圣节的夜里：凋灵飘在左边，三个脑袋轮流朝地面吐凋灵之首；蝙蝠扑腾着乱飞，地上摆着南瓜灯；
/// 玩家放烟花，火箭拖着火星升空，炸开一圈彩色的火花
public sealed class PrettyScary : IDotScene
{
    public string Id => "1.4";
    public uint Background => 0x0E1028u;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 凋灵（正面，30×36）：中间一个大头、两边两个小头，下面是脊椎和肋骨，尾巴细长；黑灰色，眼睛是浅灰的
    public static Sprite Wither(double t)
    {
        uint[] dark = { 0x1E1E1Eu, 0x2A2A2Au, 0x141414u, 0x343434u };
        uint? Head(int x, int y, int x0, int y0, int s)
        {
            if (!(x >= x0 && x < x0 + s && y >= y0 && y < y0 + s)) return null;
            var lx = x - x0;
            var ly = y - y0;
            if (ly == s / 2 - 1 && (lx == 1 || lx == s - 2)) return 0xC8C8C8u;
            if (ly == s - 2 && lx >= 1 && lx < s - 1 && lx % 2 == 0) return 0x8A8A8Au;
            return DotMath.Pick(dark, x, y, 114);
        }
        var sway = (int)Math.Round(Math.Sin(t * 2) * 1, MidpointRounding.AwayFromZero);
        return new Sprite(30, 38, (x, y) =>
        {
            if (Head(x, y, 11, 0, 8) is { } c1) return c1;
            if (Head(x, y, 2, 3 + sway, 6) is { } c2) return c2;
            if (Head(x, y, 22, 3 - sway, 6) is { } c3) return c3;
            if (x >= 2 && x <= 27 && y >= 9 && y <= 11) return DotMath.Pick(dark, x, y, 115);   // 肩
            if (x >= 13 && x <= 16 && y >= 12 && y <= 26) return DotMath.Pick(dark, x, y, 116);  // 脊椎
            if (y >= 14 && y <= 25 && y % 3 == 0 && x >= 8 && x <= 21) return 0x2A2A2Au;        // 肋骨
            var tx = 14 + (int)(Math.Sin(y * 0.3 + t * 2) * 2);
            if (y >= 27 && y <= 37 && x >= tx && x <= tx + 1) return 0x1E1E1Eu;                 // 尾巴
            return null;
        });
    }

    public static Sprite Bat(bool flap) =>
        flap
            ? new Sprite(new[] { "w.....w", "ww.b.ww", ".wbbbw.", "..bkb..", "...b..." },
                new Dictionary<char, uint> { ['w'] = 0x2A2018u, ['b'] = 0x4A3A2Eu, ['k'] = 0x1E1E1Du })
            : new Sprite(new[] { "..b.b..", ".wbbbw.", "wwbkbww", "w..b..w" },
                new Dictionary<char, uint> { ['w'] = 0x2A2018u, ['b'] = 0x4A3A2Eu, ['k'] = 0x1E1E1Du });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x0A0A20u, 0x10123Au, 0x1A1C4Eu, 0x2A2A5Eu });
        // 方形的月亮
        var mx = (int)(b.Cols * 0.62);
        var my = 14;
        b.Fill(mx, my, 16, 16, 0xE8ECF4u);
        b.Fill(mx + 3, my + 4, 4, 3, 0xC8CEDAu);
        b.Fill(mx + 9, my + 9, 3, 3, 0xC8CEDAu);
        for (var i = 0; i < 40; i++)
            b.Set((int)(DotMath.Noise(i, 0, 117) * b.Cols), (int)(DotMath.Noise(i, 1, 117) * g * 0.5), 0xC8CCE8u);
        // 远处光秃秃的树
        foreach (var (cx, h) in new[] { (0.04, 50), (0.5, 38), (0.97, 46) })
        {
            var x = (int)(b.Cols * cx);
            b.Fill(x, g - h, 3, h, 0x2A2030u);
            foreach (var (dx, dy, len) in new[] { (-1, 14, 8), (3, 20, 9), (-1, 28, 6), (3, 8, 5) })
            {
                if (dy >= h) continue;
                for (var k = 0; k < len; k++) b.Set(x + (dx < 0 ? -k : dx + k), g - h + dy - k / 2, 0x2A2030u);
            }
        }
        // 灵魂沙堆（凋灵就是从这里召唤出来的）
        var sx = (int)(b.Cols * 0.14);
        b.Fill(sx, g - 4, 16, 4, 0x4A3A2Eu);
        for (var x = sx; x < sx + 16; x += 3) b.Set(x + 1, g - 3, 0x3A2A22u);
        // 地面
        Paint.Strata(b, g, new uint[] { 0x3E6A2Cu, 0x325A24u, 0x4A7A34u }, 4,
            new uint[] { 0x4A3222u, 0x3E2A1Cu, 0x54382Au }, 30, new uint[] { 0x3E3E3Eu, 0x4A4A4Au }, 118);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        // 南瓜灯：一明一暗
        var pumpkinCxs = new[] { 0.34, 0.46, 0.98 };
        for (var k = 0; k < pumpkinCxs.Length; k++)
        {
            var cx = pumpkinCxs[k];
            var x = (int)(b.Cols * cx) - 4;
            b.Fill(x, g - 9, 9, 8, 0xD8781Eu);
            b.Fill(x + 4, g - 10, 1, 1, 0x4A6A2Au);
            uint lit = Math.Sin(t * 5 + k * 2) > -0.3 ? 0xFFE04Au : 0xF0A02Au;
            b.Set(x + 2, g - 7, lit);
            b.Set(x + 6, g - 7, lit);
            b.Fill(x + 2, g - 4, 5, 1, lit);
            b.Set(x + 4, g - 5, lit);
        }
        // 凋灵：慢慢上下飘，周围有黑色的粒子；每 3 秒吐一颗凋灵之首砸向地面
        // 原版的凋灵模型渲染时放大一倍，约 3.5 格高；这里按 1.6 倍画
        var wx = (int)(b.Cols * 0.08);
        var wy = g - 14 + (int)Math.Round(Math.Sin(t * 0.9) * 4, MidpointRounding.AwayFromZero);
        b.Blit(Wither(t), wx, wy, 48, 61);
        for (var i = 0; i < 14; i++)
            b.Set(wx + (int)(DotMath.Noise(i, (int)(t * 3), 119) * 54) - 2, wy - (int)(DotMath.Noise(i, (int)(t * 3), 120) * 64), 0x1A1A1Au);
        var sp = Paint.Mod(t, 3);
        if (sp < 1.2)
        {
            var f = sp / 1.2;
            var sx = wx + 24 + (int)(f * 40);
            var sy = wy - 50 + (int)(f * f * (g - wy + 48));
            b.Fill(sx, sy, 4, 4, 0x1E1E1Eu);
            b.Set(sx + 1, sy + 1, 0xC8C8C8u);
        }
        else if (sp < 1.6)
        {
            var f = (sp - 1.2) / 0.4;
            for (var i = 0; i < 8; i++)
                b.Set(wx + 64 + (int)(Math.Cos(i) * f * 10), g - 2 - (int)(Math.Abs(Math.Sin(i)) * f * 6), i % 2 == 0 ? 0x3A3A3Au : 0x8A8A8Au);
        }
        // 蝙蝠乱飞
        for (var i = 0; i < 5; i++)
        {
            var x = (int)(b.Cols * (0.2 + DotMath.Noise(i, 0, 121) * 0.6) + Math.Sin(t * (1.1 + i * 0.3) + i) * 24);
            var y = (int)(g * (0.2 + DotMath.Noise(i, 1, 121) * 0.4) + Math.Cos(t * (1.7 + i * 0.2)) * 8);
            b.Blit(Bat((int)(t * 10 + i) % 2 == 0), x, y);
        }
        // 烟花：4 秒一发，火箭拖着火星升空，在天上炸开
        var px = (int)(b.Cols * 0.76);
        var fp = Paint.Mod(t, 4);
        var n = (int)(t / 4);
        uint[][] palettes =
        {
            new uint[] { 0xFF4A6Au, 0xFFB0C0u }, new uint[] { 0x4AD8FFu, 0xC8F4FFu },
            new uint[] { 0xFFD84Au, 0xFFF4B0u }, new uint[] { 0x8AFF5Au, 0xD8FFC0u },
        };
        var pal = palettes[n % palettes.Length];
        var rx = px - 14 + (int)(DotMath.Noise(n, 0, 122) * 10);
        var burstY = (int)(g * (0.22 + DotMath.Noise(n, 1, 122) * 0.15));
        if (fp < 1.2)
        {
            var y = g - 20 - (int)((g - 20 - burstY) * DotMath.Ease(fp / 1.2));
            b.Fill(rx, y, 2, 4, 0xC8343Au);
            b.Set(rx, y + 1, 0xF0F0F0u);
            for (var k = 1; k < 8; k++)
                if (DotMath.Dither(rx, y + 4 + k, 1 - (double)k / 8))
                    b.Set(rx + (k % 2), y + 4 + k, 0xFFD06Au);
        }
        else if (fp < 3)
        {
            var f = (fp - 1.2) / 1.8;
            for (var i = 0; i < 28; i++)
            {
                var a = (double)i / 28 * 2 * Math.PI;
                var r = 4 + f * 22;
                var x = rx + (int)(Math.Cos(a) * r);
                var y = burstY + (int)(Math.Sin(a) * r + f * f * 8);
                if (Math.Sin(t * 30 + i) > (f - 0.6) * 2) b.Set(x, y, i % 3 == 0 ? pal[1] : pal[0]);
            }
        }
        var pose = Player.Idle(t, new PlayerProps(), 1);
        if (fp < 0.6) pose.ArmAngle = 0.9 * (1 - fp / 0.6);
        Player.Draw(b, cast.Player, null, pose, px, g - 1, t);
    }
}

// 1.5 Redstone Update：漏斗、比较器和红石灯

/// 1.5 红石更新加入了漏斗、红石比较器、阳光探测器、投掷器、陷阱箱和下界石英。
/// 地下的工作间：物品一个个掉进一串漏斗，漏斗把它们送进箱子；比较器读出箱子里的东西，
/// 红石粉一格格亮起来，点亮一排红石灯；玩家拿着下界石英看
public sealed class RedstoneUpdate : IDotScene
{
    public string Id => "1.5";
    public uint Background => 0x1A1A1Eu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    public static readonly HeldItem Quartz = new(
        new Sprite(
            new[]
            {
                "..w..", ".wQq.", "wQQQq", ".qQq.", "..q..",
            },
            new Dictionary<char, uint> { ['w'] = 0xFFFFFFu, ['Q'] = 0xE8E0D8u, ['q'] = 0xB8ACA0u }),
        swings: false);

    /// 漏斗（16×16，侧面）：上面宽口，下面一截细嘴
    public static readonly Sprite Hopper = new(16, 16, (x, y) =>
    {
        if (y < 10)
        {
            if (y == 0 || x == 0 || x == 15) return 0x3A3A3Eu;
            if (y < 3) return x is >= 2 and <= 13 ? 0x1E1E22u : 0x4A4A4Eu;
            var inset = (y - 3) / 2;
            return x >= inset && x <= 15 - inset ? (x == inset || x == 15 - inset ? 0x2E2E32u : 0x4A4A4Eu) : null;
        }
        return x is >= 6 and <= 9 ? (x == 6 ? 0x3A3A3Eu : 0x4A4A4Eu) : null;
    });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        // 石砖墙
        for (var y = 0; y < g; y++)
            for (var x = 0; x < b.Cols; x++)
            {
                var row = y / 8;
                var mortar = y % 8 == 7 || (x + (row % 2) * 8) % 16 == 15;
                var c = mortar ? 0x3A3A3Eu : DotMath.Pick(new uint[] { 0x6E6E72u, 0x7A7A7Eu, 0x626266u, 0x84848Au }, x / 16 + row * 7, row, 123);
                b.Set(x, y, DotMath.Dither(x, y, 0.25) ? DotMath.Shade(c, 0.8) : c);
            }
        // 下界石英块的柱子
        foreach (var cx in new[] { 0.03, 0.98 })
        {
            var x0 = (int)(b.Cols * cx) - 6;
            for (var y = 0; y < g; y++)
                for (var dx = 0; dx < 12; dx++)
                    b.Set(x0 + dx, y, dx == 0 || dx == 11 ? 0xC8C0B4u : (dx % 4 == 1 ? 0xE0D8CCu : 0xEAE4DAu));
        }
        // 一串漏斗（三个）通向箱子
        var hx = (int)(b.Cols * 0.1);
        for (var i = 0; i < 3; i++) b.Blit(Hopper, hx + i * 16, g - 1 - 32 + i * 16);
        // 箱子
        var chestX = hx + 46;
        b.Fill(chestX, g - 14, 16, 14, 0x9A6A2Eu);
        b.Fill(chestX, g - 14, 16, 1, 0x6A4A22u);
        b.Fill(chestX, g - 9, 16, 1, 0x6A4A22u);
        b.Fill(chestX + 7, g - 11, 2, 4, 0xB8B8B8u);
        // 地面：石头
        Paint.Strata(b, g, new uint[] { 0x8A8A8Eu, 0x7A7A7Eu, 0x949498u }, 3,
            new uint[] { 0x6E6E72u, 0x7A7A7Eu, 0x626266u }, 20, new uint[] { 0x4A4A4Eu, 0x3E3E42u }, 124);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        var hx = (int)(b.Cols * 0.1);
        var cx = hx + 46;
        // 物品掉进漏斗，一格格往下传（每秒一个）
        uint[] colors = { 0xE8E0D8u, 0xD8341Fu, 0x4AEDD9u, 0xF5D74Au, 0x5DA03Fu };
        for (var k = 0; k < 4; k++)
        {
            var q = Paint.Mod(t * 0.5 + (double)k / 4, 1);
            var c = colors[((int)(t * 0.5 + (double)k / 4) + k) % colors.Length];
            // 路径：从上面掉进第一个漏斗，经过三个漏斗的嘴，进箱子
            (double X, double Y)[] pts = { (hx + 6, g - 60), (hx + 6, g - 40), (hx + 22, g - 24), (hx + 38, g - 8), (cx + 6, g - 10) };
            var s = q * (pts.Length - 1);
            var i = Math.Min(pts.Length - 2, (int)s);
            var f = s - i;
            var x = pts[i].X + (pts[i + 1].X - pts[i].X) * f;
            var y = pts[i].Y + (pts[i + 1].Y - pts[i].Y) * f;
            b.Fill((int)x, (int)y, 4, 4, c);
            b.Set((int)x, (int)y, DotMath.Shade(c, 1.2));
        }
        // 比较器：箱子满一点就亮；信号沿着红石粉往右传，点亮红石灯
        var signal = Math.Sin(t * 0.8) * 0.5 + 0.5;
        var compX = cx + 18;
        b.Fill(compX, g - 3, 14, 3, 0xB8B8B8u);
        foreach (var (dx, on) in new[] { (2, signal > 0.2), (10, signal > 0.2), (6, signal > 0.6) })
        {
            b.Fill(compX + dx, g - 7, 2, 4, 0x8A6A3Au);
            b.Fill(compX + dx, g - 8, 2, 2, on ? 0xFF3A2Au : 0x6A1A1Au);
        }
        var dustStart = compX + 15;
        double[] lamps = { 0.38, 0.5, 0.62 };
        var dustEnd = (int)(b.Cols * 0.66);
        var reach = (int)((dustEnd - dustStart) * signal);
        for (var x = dustStart; x < dustEnd; x++)
        {
            var lit = x - dustStart < reach;
            b.Set(x, g - 1, lit ? (x % 3 == 0 ? 0xFF4A2Au : 0xD8241Au) : 0x5A1010u);
        }
        for (var i = 0; i < lamps.Length; i++)
        {
            var lx = lamps[i];
            var x = (int)(b.Cols * lx);
            var on = x - dustStart < reach;
            for (var dy = 0; dy < 14; dy++)
                for (var dx = 0; dx < 14; dx++)
                {
                    var edge = dx == 0 || dy == 0 || dx == 13 || dy == 13;
                    uint c = on
                        ? (edge ? 0x9A5A2Au : ((dx + dy + i) % 5 == 0 ? 0xFFF0B0u : 0xF8C860u))
                        : (edge ? 0x5A3418u : ((dx + dy) % 5 == 0 ? 0x8A5A30u : 0x6A4224u));
                    b.Set(x + dx, g - 15 - dy, c);
                }
            if (on)
                for (var k = 0; k < 6; k++)
                    if (DotMath.Dither(x + k * 3, g - 20, 0.5))
                        b.Set(x - 2 + k * 3, g - 17 - (int)(DotMath.Noise(k, (int)(t * 4), 125) * 4), 0xFFE08Au);
        }
        var props = new PlayerProps { Held = Quartz };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props, 6), (int)(b.Cols * 0.8), g - 1, t);
    }
}

// 1.6 Horse Update：马与驴

/// 1.6 马匹更新加入了马、驴、骡、拴绳、干草块、地毯和马铠。
/// 草原上：一头驴被拴绳拴在栅栏上，另一匹栗色的马低头吃草；玩家骑着一匹披着钻石马铠的白马，马时不时扬一下头
public sealed class HorseUpdate : IDotScene
{
    public string Id => "1.6";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 马（朝右，按原版模型 1 像素 = 1 点）：身子 22 长 10 高，四条腿 4 宽 11 高（远侧的腿暗一点、错开一点），
    /// 粗脖子往前斜着长出来，鬃毛沿着脖子后沿；头是一个往前下方斜的长方块，嘴在最前面；尾巴从屁股垂下来。
    /// graze 时脖子往前下方伸、嘴碰到地；ears 为驴的长耳朵，muzzle 为驴和骡子发白的嘴；armor 为马铠的两种颜色；
    /// saddle 时背上一副鞍；step 为迈步
    public static Sprite Horse(uint[] coat, uint mane, bool graze, int step, bool ears = false, uint? muzzle = null,
        uint[]? armor = null, bool saddle = false, double toss = 0)
    {
        const uint hoof = 0x3A2E26u;

        /// 一个斜放的长方块：从 (ox, oy) 沿 angle 方向（0 = 竖直向上，正值往前倾）伸出 len，宽 w；返回 (沿长度的位置, 横向位置)
        (double U, double V)? Box(int x, int y, double ox, double oy, double angle, double len, double w)
        {
            var dx = x + 0.5 - ox;
            var dy = y + 0.5 - oy;
            var u = dx * Math.Sin(angle) - dy * Math.Cos(angle);
            var v = dx * Math.Cos(angle) + dy * Math.Sin(angle);
            return u >= 0 && u <= len && Math.Abs(v) <= w / 2 ? (u, v) : null;
        }
        var neckAngle = graze ? 2.05 : 0.55 - toss;
        var headAngle = graze ? 2.9 : 2.0 - toss * 0.5;
        var neckBase = (X: 23.0, Y: 16.0);
        var neckLen = graze ? 12.0 : 13.0;
        var neckTop = (X: neckBase.X + Math.Sin(neckAngle) * neckLen, Y: neckBase.Y - Math.Cos(neckAngle) * neckLen);
        return new Sprite(40, 34, (x, y) =>
        {
            uint CoatAt(int cx, int cy, double k = 1) => DotMath.Shade(DotMath.Pick(coat, cx / 2, cy / 2, 126), k);
            // 耳朵：在头顶靠脖子的地方，驴的耳朵又长又往后斜
            if (Box(x, y, neckTop.X - 1, neckTop.Y + 1, neckAngle - 0.35, ears ? 7 : 3, 2) != null) return CoatAt(x, y, 0.85);
            // 头：往前下方斜的长方块，最前面是嘴，靠上一点是眼睛
            if (Box(x, y, neckTop.X - 2, neckTop.Y + 2, headAngle, 11, 6) is { } head)
            {
                var (u, v) = head;
                if (armor is { } headArmor && u < 6) return v < -1.5 ? headArmor[1] : headArmor[0];
                if (u > 8) return muzzle ?? CoatAt(x, y, 0.7);
                if ((int)u >= 2 && (int)u <= 3 && v < -0.5 && v > -2) return 0x1E1E1Du;
                return CoatAt(x, y, v > 1.8 ? 0.85 : 1);
            }
            // 脖子：粗，鬃毛沿着后沿
            if (Box(x, y, neckBase.X, neckBase.Y, neckAngle, neckLen + 1.5, 7) is { } neck)
            {
                var v = neck.V;
                if (v < -1.8) return mane;
                if (armor is { } neckArmor) return v > 2 ? neckArmor[1] : neckArmor[0];
                return CoatAt(x, y);
            }
            // 身子
            if (x >= 3 && x <= 24 && y >= 12 && y <= 21)
            {
                if (saddle && x >= 9 && x <= 16 && y >= 11 && y <= 13) return y == 13 ? 0x5A3A22u : 0x7A4E2Eu;
                if (armor is { } bodyArmor && y <= 15 && x >= 14) return y == 12 ? bodyArmor[1] : bodyArmor[0];
                return y == 21 ? CoatAt(x, y, 0.82) : CoatAt(x, y);
            }
            if (saddle && x >= 9 && x <= 16 && y == 11) return 0x8A5A34u;
            if (saddle && x == 12 && y >= 14 && y <= 19) return 0x4A3A2Au;   // 脚蹬的带子
            // 尾巴
            if (x >= 0 && x <= 2 && y >= 13 && y <= 27 && !(x == 0 && y < 16)) return x == 2 && y > 24 ? null : mane;
            // 腿：近侧两条亮，远侧两条暗、往里错开两点
            foreach (var (lx, far, i) in new[] { (6, true, 0), (20, true, 1), (4, false, 1), (18, false, 0) })
            {
                if (x < lx || x > lx + 3) continue;
                var up = i == step ? 1 : 0;
                if (y >= 22 && y <= 33 - up)
                {
                    if (y >= 32 - up) return hoof;
                    return CoatAt(x, y, far ? 0.72 : (x == lx + 3 ? 0.88 : 1));
                }
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u });
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.02 + 1) * 0.5 + 0.5) * 16 + DotMath.Noise(x / 6, 0, 127) * 5);
            for (var y = g - h; y < g; y++)
                b.Set(x, y, DotMath.Pick(new uint[] { 0x6A9757u, 0x5F8C4Eu, 0x76A060u }, x, y, 128));
        }
        // 栅栏
        var fx = (int)(b.Cols * 0.03);
        for (var k = 0; k < 4; k++)
        {
            b.Fill(fx + k * 16 + 6, g - 16, 4, 16, 0x8A6238u);
        }
        b.Fill(fx + 6, g - 13, 52, 2, 0x8A6238u);
        b.Fill(fx + 6, g - 7, 52, 2, 0x8A6238u);
        // 干草块：两块叠起来
        foreach (var (dx, dy) in new[] { (0, 0), (16, 0), (8, 16) })
        {
            var x0 = (int)(b.Cols * 0.42) + dx;
            var y0 = g - 16 - dy;
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var c = DotMath.Pick(new uint[] { 0xC8A82Au, 0xB89820u, 0xD8B83Au }, x, y / 3, 129);
                    if (y == 3 || y == 12) c = 0x9A3A22u;
                    b.Set(x0 + x, y0 + y, c);
                }
        }
        Paint.Strata(b, g, new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, 4,
            new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu }, 30, new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, 130);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, 4);
        // 驴：拴在栅栏上，拴绳是一条下垂的线
        var dx0 = (int)(b.Cols * 0.06);
        uint[] donkey = { 0x7A6A5Eu, 0x6A5C50u, 0x8A7A6Cu };
        b.Blit(Horse(donkey, 0x3A2E26u, false, 2, ears: true, muzzle: 0xC8C0B0u,
            toss: Math.Sin(t * 0.5) > 0.8 ? 0.15 : 0), dx0, g - 1);
        var post = (X: (double)((int)(b.Cols * 0.03) + 54), Y: (double)(g - 14));
        var neck = (X: (double)(dx0 + 30), Y: (double)(g - 22));
        for (var k = 0; k <= 20; k++)
        {
            var f = (double)k / 20;
            b.Set((int)(post.X + (neck.X - post.X) * f),
                (int)(post.Y + (neck.Y - post.Y) * f + Math.Sin(f * Math.PI) * 4), 0xB8A070u);
        }
        // 吃草的栗色马
        var graze = Math.Sin(t * 0.4) > -0.2;
        b.Blit(Horse(new uint[] { 0x8A5A2Eu, 0x7A4E26u, 0x9A6A3Au }, 0x3A2414u, graze, 2), (int)(b.Cols * 0.25), g - 1);
        // 玩家骑着的白马：披着钻石马铠，朝左，时不时扬头
        var px = (int)(b.Cols * 0.7);
        var toss = Math.Max(0, Math.Sin(t * 0.7) - 0.8) * 2;
        var bob = Math.Sin(t * 2.4) > 0.6 ? 1 : 0;
        b.Blit(Horse(new uint[] { 0xE8E8E8u, 0xD8D8D8u, 0xF4F4F4u, 0xC8C8C8u }, 0x9A9A9Au, false, 2,
            armor: new uint[] { 0x4AEDD9u, 0x1A9B8Au }, saddle: true, toss: toss), px - 8, g - 1 - bob, flip: true);
        var pose = Player.Idle(t, new PlayerProps(), 8);
        pose.Sitting = true;
        Player.Draw(b, cast.Player, null, pose, px + 15, g - 12 - bob, t);
    }
}

// 1.7 The Update that Changed the World：恶地钓鱼

/// 1.7 “改变世界的更新”重做了地形生成，加入了恶地、黑森林、巨型针叶林等十几种群系，还有染色玻璃、新的花，
/// 并重做了钓鱼（能钓到河豚、热带鱼和宝藏）。恶地的水塘边：玩家甩竿钓鱼，浮漂一沉，拉上来一条河豚
public sealed class ChangedTheWorld : IDotScene
{
    public string Id => "1.7";
    public uint Background => 0x6F98ECu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 钓鱼竿：竖着画（竿梢朝上），跟着手转；比剑长得多
    public static readonly HeldItem FishingRod = new(
        new Sprite(RodRows(), new Dictionary<char, uint> { ['w'] = 0x6B4A2Cu, ['W'] = 0x8A6238u, ['s'] = 0x4A4A4Au }));

    private static string[] RodRows()
    {
        var rows = new List<string> { ".s.", ".s." };
        for (var i = 0; i < 22; i++) rows.Add(i % 3 == 0 ? ".w." : ".W.");
        rows.AddRange(new[] { "wWw", ".w." });
        return rows.ToArray();
    }

    /// 竿梢在场景里的位置：按 Player.Draw 里手的位置和物品的转法算（物品转角 = armAngle - π/2 + 0.45 + itemTilt）
    public static (double X, double Y) RodTip(int x, int feet, PlayerPose pose)
    {
        var h = (double)FishingRod.Sprite.Height;
        var ax = (double)Player.CanvasX;
        var handX = ax - 1.5 - Math.Sin(pose.ArmAngle) * 10.5;
        var handY = 37 + Math.Cos(pose.ArmAngle) * 10.5;
        var angle = pose.ArmAngle - Math.PI / 2 + 0.45 + pose.ItemTilt;
        return (x + pose.Dx - ax + Math.Floor(handX) + (h - 2) * Math.Sin(angle),
                feet - 60 + Math.Floor(handY) - (h - 2) * Math.Cos(angle));
    }

    public static readonly Sprite Pufferfish = new(
        new[]
        {
            "..y.y.y..", ".yYYYYYy.", "yYkYYYYYb", "yYYYYYYYb", ".wWWWWWw.", "..y.y.y..",
        },
        new Dictionary<char, uint>
        {
            ['y'] = 0xE8A81Eu, ['Y'] = 0xF5C42Eu, ['k'] = 0x1E1E1Du, ['b'] = 0x8AC8E8u, ['w'] = 0xF0E0B0u, ['W'] = 0xF8ECC8u,
        });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x5B87E0u, 0x6F98ECu, 0x86AAF2u, 0xA5C1F6u, 0xE8D8C0u });
        // 恶地的台地：一层层陶瓦（橙、白、黄、红、棕、浅灰）
        uint[] bands = { 0xB8622Eu, 0xD07A3Au, 0xE8D8C8u, 0xA8502Au, 0xD8A84Au, 0x9A4A2Eu, 0xC08A6Au, 0xB8622Eu, 0x8A5A42u, 0xD07A3Au };
        for (var x = 0; x < b.Cols; x++)
        {
            var mesa = x < (int)(b.Cols * 0.36) || x > (int)(b.Cols * 0.82);
            var h = mesa ? (int)(g * 0.62 + DotMath.Noise(x / 12, 0, 131) * 6) : (int)(g * 0.22 + Math.Sin(x * 0.05) * 4);
            for (var y = g - h; y < g; y++)
            {
                var c = bands[((y + 3) / 4) % bands.Length];
                b.Set(x, y, DotMath.Dither(x, y, 0.15) ? DotMath.Shade(c, 0.9) : c);
            }
            // 台地顶上的一层草和泥
            b.Set(x, g - h, 0x8A9A3Au);
        }
        // 金合欢树（1.7）
        var ax = (int)(b.Cols * 0.6);
        for (var k = 0; k < 22; k++) b.Fill(ax + k / 4, g - 22 - k, 3, 1, 0x6A5A4Au);
        for (var dy = 0; dy < 4; dy++) b.Fill(ax - 8 + dy, g - 46 + dy, 26 - dy * 2, 1, dy == 0 ? 0x7A9A2Eu : 0x5E7E22u);
        // 地面：红沙
        Paint.Strata(b, g, new uint[] { 0xC86A2Eu, 0xD07A3Au, 0xB85E26u }, 6,
            new uint[] { 0xB8622Eu, 0xA8502Au, 0xD07A3Au }, 24, new uint[] { 0x9A4A2Eu, 0x8A5A42u }, 132);
        // 水塘
        var w0 = (int)(b.Cols * 0.18);
        var w1 = (int)(b.Cols * 0.62);
        for (var x = w0; x < w1; x++)
            for (var y = g; y < g + 8; y++)
            {
                var edge = Math.Min(x - w0, w1 - x) / 8.0;
                if (y - g < 8 * Math.Min(1, edge))
                    b.Set(x, y, y == g ? 0x5A8AE0u : DotMath.Pick(new uint[] { 0x3A64C8u, 0x3260BAu, 0x4470D0u }, x, y, 133));
            }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Clouds(b, t, g / 3, 3);
        // 水面的波光
        for (var i = 0; i < 10; i++)
        {
            var x = (int)(b.Cols * 0.2) + (int)(DotMath.Noise(i, 0, 134) * b.Cols * 0.4) + (int)(Math.Sin(t + i) * 3);
            if (Math.Sin(t * 2 + i) > 0.3) b.Fill(x, g + 1 + i % 3, 3, 1, 0x9CC8F8u);
        }
        // 时间线（9 秒一轮）：甩竿 → 浮漂在水上漂 → 一沉，冒水泡 → 拉竿，河豚飞上来
        var p = Paint.Mod(t, 9);
        var px = (int)(b.Cols * 0.76);
        var props = new PlayerProps { Held = FishingRod };
        var pose = Player.Idle(t, props, 0);
        // 平时竿子斜着伸向水塘；甩竿时往后扬再甩出去，拉竿时往上一提
        pose.ArmAngle = 0.8;
        pose.ItemTilt = -0.35;
        pose.HeadDip = 0;
        pose.LookBack = false;
        if (p < 0.6)
        {
            var k = Math.Sin(p / 0.6 * Math.PI);
            pose.ArmAngle = 0.8 + 1.0 * k;
            pose.ItemTilt = -0.35 + 0.9 * k;
        }
        if (p >= 6 && p <= 6.5)
        {
            var k = Math.Sin((p - 6) / 0.5 * Math.PI);
            pose.ArmAngle = 0.8 + 0.7 * k;
            pose.ItemTilt = -0.35 + 0.6 * k;
        }
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
        var tip = RodTip(px, g - 1, pose);
        var bobX = b.Cols * 0.46;
        var bite = p >= 4.6 && p <= 6;
        double bobY = g + (bite ? 2 : (Math.Sin(t * 2) > 0.4 ? 1 : 0)) - 2;
        if (p >= 0.6 && p <= 6.1)
        {
            // 鱼线：从竿尖垂到浮漂
            var f0 = Math.Min(1, (p - 0.6) / 0.6);
            var endX = tip.X + (bobX - tip.X) * f0;
            var endY = tip.Y + (bobY - tip.Y) * f0 - Math.Sin(f0 * Math.PI) * 16;
            for (var k = 0; k <= 40; k++)
            {
                var f = (double)k / 40;
                var x = tip.X + (endX - tip.X) * f;
                var y = tip.Y + (endY - tip.Y) * f + Math.Sin(f * Math.PI) * 6 * f0;
                if (k % 2 == 0) b.Set((int)x, (int)y, 0xE8E8E8u);
            }
            b.Fill((int)endX - 1, (int)endY - 2, 3, 2, 0xD8341Fu);
            b.Fill((int)endX - 1, (int)endY, 3, 1, 0xF0F0F0u);
            if (bite)
                for (var i = 0; i < 5; i++)
                    b.Set((int)bobX - 4 + i * 2, g - 1 - (int)(Math.Abs(Math.Sin(t * 8 + i)) * 3), 0xC8E4FFu);
        }
        if (p >= 6.1 && p <= 7.3)
        {
            var f = (p - 6.1) / 1.2;
            var x = bobX + (px - 4 - bobX) * f;
            var y = (double)g - Math.Sin(f * Math.PI) * 30 - 10 * f;
            b.Blit(Pufferfish, (int)x, (int)y);
        }
        if (p >= 7.3 && p <= 9) b.Blit(Pufferfish, px + 10, g - 1);
    }
}

// 1.10 Frostburn Update：冰刺之原的北极熊

/// 1.10 霜火更新加入了北极熊、尸壳、流浪者、岩浆块和化石。冰刺之原：高高的浮冰刺，北极熊妈妈带着小熊；
/// 玩家靠近一点，熊妈妈就站起来吼一声护着小熊，小熊在旁边打滚
public sealed class Frostburn : IDotScene
{
    public string Id => "1.10";
    public uint Background => 0x8FB3FFu;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 轮廓上的点压暗一点（白色的生物站在雪地上也看得清）
    public static Sprite Edged(Sprite s)
    {
        return new Sprite(s.Width, s.Height, (x, y) =>
        {
            var v = s.Px[y * s.Width + x];
            if (v == 0) return null;
            bool Empty(int ex, int ey) => ex < 0 || ey < 0 || ex >= s.Width || ey >= s.Height || s.Px[ey * s.Width + ex] == 0;
            var edge = Empty(x - 1, y) || Empty(x + 1, y) || Empty(x, y - 1) || Empty(x, y + 1);
            return edge ? DotMath.Shade(v & 0xFFFFFFu, 0.8) : v & 0xFFFFFFu;
        });
    }

    /// 北极熊（朝右，30×22，约 1.4 格高）：白色的大身子、小耳朵、黑鼻子；standing 时用后腿站起来（16×36）
    public static Sprite Bear(bool standing, bool mouth, int step, int scale = 1)
    {
        uint[] fur = { 0xF4F4F0u, 0xE8E8E2u, 0xFFFFFFu, 0xDCDCD4u };
        if (standing)
        {
            return new Sprite(18, 38, (x, y) =>
            {
                if (x >= 5 && x <= 12 && y >= 1 && y <= 8)
                {
                    if (y == 1 && (x == 6 || x == 11)) return 0xDCDCD4u;
                    if (y == 4 && x == 10) return 0x1E1E1Du;
                    return DotMath.Pick(fur, x, y, 135);
                }
                if (x >= 13 && x <= 16 && y >= 4 && y <= 8)
                {
                    if (x == 16 && y == 4) return 0x1E1E1Du;
                    if (mouth && y == 8) return 0x8A3A3Au;
                    return y >= 7 ? 0xE0E0DAu : DotMath.Pick(fur, x, y, 134);
                }
                if (x >= 3 && x <= 13 && y >= 9 && y <= 28) return x >= 11 ? 0xE0E0D8u : DotMath.Pick(fur, x / 2, y / 2, 136);
                if (x >= 13 && x <= 17 && y >= 10 && y <= 14) return y == 14 ? 0x3A3A3Au : DotMath.Pick(fur, x, y, 137);   // 举起的前爪
                if ((x >= 4 && x <= 7 || x >= 10 && x <= 13) && y >= 29 && y <= 37) return y == 37 ? 0x3A3A3Au : DotMath.Pick(fur, x, y, 138);
                return null;
            });
        }
        return new Sprite(30, 22, (x, y) =>
        {
            if (x >= 2 && x <= 21 && y >= 3 && y <= 14) return y == 14 ? 0xDCDCD4u : DotMath.Pick(fur, x / 2, y / 2, 139);
            if (x >= 20 && x <= 28 && y >= 5 && y <= 12)
            {
                if (y == 7 && x == 25) return 0x1E1E1Du;
                if (x >= 27 && y >= 9 && y <= 10) return x == 28 && y == 9 ? 0x1E1E1Du : 0xE0E0DAu;
                return DotMath.Pick(fur, x, y, 140);
            }
            if ((x == 21 || x == 24) && y == 4) return 0xDCDCD4u;
            var legXs = new[] { 3, 8, 15, 19 };
            for (var i = 0; i < legXs.Length; i++)
            {
                var lx = legXs[i];
                if (x < lx || x > lx + 3) continue;
                var up = i % 2 == step ? 1 : 0;
                if (y >= 15 && y <= 21 - up) return y == 21 - up ? 0x3A3A3Au : DotMath.Pick(fur, x, y, 141);
            }
            return null;
        });
    }

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x6A8ED8u, 0x8FB3FFu, 0xB4CCF8u, 0xD8E4FAu });
        // 浮冰刺：细高的柱子，有的顶上收尖
        for (var i = 0; i < 12; i++)
        {
            var x = (int)(DotMath.Noise(i, 0, 142) * b.Cols);
            var h = 20 + (int)(DotMath.Noise(i, 1, 142) * g * 0.7);
            var w = 6 + (int)(DotMath.Noise(i, 2, 142) * 6);
            var far = i % 2 == 1;
            for (var y = g - h; y < g; y++)
            {
                var taper = Math.Max(1, Math.Min(w, (y - (g - h)) / 3 + 1));
                for (var dx = 0; dx < taper; dx++)
                {
                    var c = dx == 0 ? 0xD8E8FCu : (dx == taper - 1 ? 0x6A90D8u : DotMath.Pick(new uint[] { 0x9CC0F0u, 0x8AB0EAu, 0xA8C8F4u }, x + dx, y, 143));
                    if (far) c = DotMath.Dither(x + dx, y, 0.5) ? 0xB4CCF8u : c;
                    b.Set(x + dx + (w - taper) / 2, y, c);
                }
            }
        }
        // 雪地
        Paint.Strata(b, g, new uint[] { 0xF4F8FFu, 0xE6EEFAu, 0xFFFFFFu }, 6,
            new uint[] { 0x8A5A33u, 0x6F4627u }, 26, new uint[] { 0x7F7F7Fu, 0x6E6E6Eu }, 144);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        Paint.Falling(b, t, 40, new uint[] { 0xFFFFFFu, 0xE6EEFAu }, 0, g, 0.04, 145);
        // 时间线（10 秒一轮）：玩家往前凑 → 熊妈妈站起来吼 → 玩家退回去 → 熊妈妈趴下
        var p = Paint.Mod(t, 10);
        var standing = p >= 3.4 && p <= 6.4;
        var roar = standing && Math.Sin(t * 9) > 0;
        var mx = (int)(b.Cols * 0.3);
        if (standing) b.Blit(Edged(Bear(true, roar, 0)), mx + 8, g - 1);
        else b.Blit(Edged(Bear(false, false, Math.Sin(t * 0.8) > 0.9 ? (int)(t * 4) % 2 : 2)), mx, g - 1);
        if (roar)
            for (var i = 0; i < 4; i++)
                b.Set(mx + 28 + i * 2, g - 30 + (int)(Math.Sin(t * 20 + i) * 2), 0xFFFFFFu);
        // 小熊（一半大）：在妈妈身后打滚、蹦跶
        var cub = Edged(Bear(false, false, (int)(t * 5) % 2));
        var cx = (int)(b.Cols * 0.12) + (int)(Math.Sin(t * 0.9) * 10);
        b.Blit(cub, cx, g - 1 - (Math.Sin(t * 4) > 0.7 ? 2 : 0), 15, 11, Math.Sin(t * 0.9 + 1.6) < 0);
        // 玩家
        var pose = Player.Idle(t, new PlayerProps(), 5);
        var near = DotMath.Ramp(p, 1.5, 3.2) * (1 - DotMath.Ramp(p, 5.6, 7.2));
        pose.Dx = -(int)(18 * near);
        // 往前凑和退回去的时候迈步；退的时候是倒着走
        if ((p >= 1.5 && p <= 3.2) || (p >= 5.6 && p <= 7.2))
        {
            pose.Walk = t * 9;
            pose.LookBack = false;
        }
        if (standing)
        {
            pose.LookBack = false;
            pose.HeadDip = 0;
        }
        Player.Draw(b, cast.Player, null, pose, (int)(b.Cols * 0.76), g - 1, t);
    }
}

// 1.11 Exploration Update：林地府邸与不死图腾

/// 1.11 探险更新加入了林地府邸、唤魔者、恼鬼、卫道士、羊驼、不死图腾、潜影盒和探险家地图。
/// 府邸前：唤魔者举手施法，一串尖牙沿着地面咬向玩家，两只恼鬼拿着铁剑绕着飞；玩家被咬中，
/// 手里的不死图腾碎开，金绿色的粒子炸开，图腾在面前转着升起来
public sealed class Exploration : IDotScene
{
    public string Id => "1.11";
    public uint Background => 0x3A4A6Au;

    private int Ground(DotBuffer b) => (int)(b.Rows * 0.66);

    /// 唤魔者：和村民一样的大鼻子、灰色的脸，黑色的长袍中间一道金边，双手抱在胸前
    public static readonly PlayerLook Evoker = BuildEvoker();

    private static PlayerLook BuildEvoker()
    {
        var look = new PlayerLook
        {
            HeadFront = new Sprite(
                new[] { "SSSSSSSS", "SSSSSSSS", "SSSSSSSS", "BBBBBBBB", "SkGSSGkS", "SSSnnSSS", "SSSnnSSS", "SSMnnMSS", "sSSnnSSs", "sSSSSSSs" },
                new Dictionary<char, uint> { ['S'] = 0x9A9C9Eu, ['s'] = 0x868A8Eu, ['B'] = 0x3A3A3Cu, ['k'] = 0x1E1E1Du, ['G'] = 0x8A8A8Au, ['n'] = 0x7E8084u, ['M'] = 0x5A5C60u }),
            HeadSide = new Sprite(
                new[] { "SSSs", "SSSs", "SSSs", "BSSs", "SSSs", "SSss", "SSSs", "SSSs", "SSSs", "ssss" },
                new Dictionary<char, uint> { ['S'] = 0x84868Au, ['s'] = 0x707276u, ['B'] = 0x343436u }),
            BodyFront = new Sprite(
                Enumerable.Repeat("RRRggRRR", 11).Concat(new[] { "rrrggrrr" }).ToArray(),
                new Dictionary<char, uint> { ['R'] = 0x1E1E22u, ['r'] = 0x141418u, ['g'] = 0xC8A030u }),
            BodySide = new Sprite(Enumerable.Repeat("Rr", 12).ToArray(),
                new Dictionary<char, uint> { ['R'] = 0x18181Cu, ['r'] = 0x101014u }),
            NearArm = new Sprite(Enumerable.Repeat("RRRRr", 12).ToArray(),
                new Dictionary<char, uint> { ['R'] = 0x1E1E22u, ['r'] = 0x141418u }),
            FarArm = new Sprite(Enumerable.Repeat("RRr", 12).ToArray(),
                new Dictionary<char, uint> { ['R'] = 0x18181Cu, ['r'] = 0x101014u }),
            Legs = new Sprite(
                Enumerable.Repeat("RRRgRRRRrr", 10).Concat(new[] { "GGGGGGGGgg", "GGGGGGGGgg" }).ToArray(),
                new Dictionary<char, uint> { ['R'] = 0x1E1E22u, ['r'] = 0x141418u, ['g'] = 0xC8A030u, ['G'] = 0x3A3A3Cu }),
            Nose = new Sprite(new[] { "nN", "nN" }, new Dictionary<char, uint> { ['n'] = 0x8A8C90u, ['N'] = 0x6E7074u }),
            CrossedArms = new Sprite(new[] { "AAAAAAAAAA..", "AhhAAAAAhhAA", "AAAAAAAAAAAa", ".aaaaaaaaaa." },
                new Dictionary<char, uint> { ['A'] = 0x2A2A2Eu, ['a'] = 0x1E1E22u, ['h'] = 0x9A9C9Eu }),
        };
        // 原版 PlayerLook 构造时 headFrontBlink 默认取 headFront
        look.HeadFrontBlink = look.HeadFront;
        return look;
    }

    public static readonly Sprite Totem = new(
        new[]
        {
            "...oooo...", "..oYYYYo..", "..oGYYGo..", "..oYYYYo..", "oooYyyYooo", "oYYYYYYYYo", "ooooYYoooo", "...oYYo...",
            "...oyyo...", "...oYYo...", "....oo....",
        },
        new Dictionary<char, uint> { ['o'] = 0xA86A1Eu, ['Y'] = 0xF0D060u, ['y'] = 0xD8A83Au, ['G'] = 0x3AD860u });

    /// 尖牙（10×12）：从地里冒出来的一对灰色的夹子，闭合时咬住；h 为露出地面的高度
    public static void Fangs(DotBuffer b, int x, int ground, int h, bool bite)
    {
        if (h <= 0) return;
        for (var dy = 0; dy < h; dy++)
        {
            var y = ground - 1 - dy;
            var gap = bite ? 0 : Math.Max(0, 3 - dy / 3);
            for (var dx = 0; dx < 4; dx++)
            {
                b.Set(x + dx - gap, y, dx == 0 ? 0x5A5A50u : 0x8A8A7Au);
                b.Set(x + 6 + dx + gap, y, dx == 3 ? 0x5A5A50u : 0x8A8A7Au);
            }
            if (dy == h - 1)
                for (var dx = 0; dx < 10; dx += 2)
                    b.Set(x + dx + (dx < 5 ? -gap : gap), y - 1, 0xF0F0E0u);
        }
    }

    public static readonly Sprite Vex = new(
        new[]
        {
            "....bbb", "...bkbk", "w..bbbb", "ww.bBb.", ".wwbb..", "..ww...", "s..b...", ".s.....",
        },
        new Dictionary<char, uint> { ['b'] = 0xA8B4C8u, ['B'] = 0x8A98B0u, ['k'] = 0x3A4A6Au, ['w'] = 0xD8E4F4u, ['s'] = 0xD8D8D8u });

    public void PaintStatic(DotBuffer b)
    {
        var g = Ground(b);
        Paint.Sky(b, g, new uint[] { 0x2A3050u, 0x3A4A6Au, 0x5A6A88u, 0x8A94A8u });
        // 黑森林的大蘑菇和树冠剪影
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)(g * 0.3 + DotMath.Noise(x / 8, 0, 146) * 12);
            for (var y = g - h; y < g; y++) b.Set(x, y, DotMath.Dither(x, y, 0.5) ? 0x1E3018u : 0x223A1Cu);
        }
        // 林地府邸：深色橡木的墙，圆石的地基，白桦的窗框，深色的屋顶
        var x0 = (int)(b.Cols * 0.02);
        var w = 96;
        var top = g - 66;
        for (var y = top; y < g; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var c = DotMath.Pick(new uint[] { 0x4A3220u, 0x3E2A1Au, 0x543A24u }, (x - x0) / 4, (y - top) / 4, 147);
                if ((x - x0) % 16 == 0) c = 0x2E1E12u;
                if (y > g - 10) c = DotMath.Pick(new uint[] { 0x7A7A7Au, 0x6A6A6Au, 0x8A8A8Au }, x / 3, y / 3, 148);
                b.Set(x, y, c);
            }
        for (var k = 0; k < 5; k++)
            for (var y = top - 10 + k * 2; y < top - 8 + k * 2; y++)
                b.Fill(x0 - 4 + k * 2, y, w + 8 - k * 4, 1, 0x3A2416u);
        b.Fill(x0 - 4, top - 2, w + 8, 2, 0x2A1A10u);
        for (var row = 0; row < 2; row++)
            for (var col = 0; col < 4; col++)
            {
                var wx = x0 + 8 + col * 22;
                var wy = top + 8 + row * 24;
                b.Fill(wx, wy, 12, 14, 0xD8D0B8u);
                b.Fill(wx + 1, wy + 1, 10, 12, 0x6A88A8u);
                b.Fill(wx + 5, wy + 1, 1, 12, 0xD8D0B8u);
                if ((row + col) % 3 == 0) b.Fill(wx + 1, wy + 1, 4, 12, 0xF0C060u);
            }
        b.Fill(x0 + 40, g - 22, 16, 22, 0x2A1A10u);
        Paint.Strata(b, g, new uint[] { 0x4F8F35u, 0x3E7A2Cu, 0x5DA03Fu }, 4,
            new uint[] { 0x6F4627u, 0x8A5A33u }, 30, new uint[] { 0x6E6E6Eu, 0x7F7F7Fu }, 149);
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var g = Ground(b);
        var px = (int)(b.Cols * 0.78);
        var ex = (int)(b.Cols * 0.46);
        // 时间线（10 秒一轮）：施法 → 尖牙一路咬过去 → 玩家被咬中、图腾生效 → 恢复
        var p = Paint.Mod(t, 10);
        var casting = p >= 0.8 && p <= 2.6;
        Player.Draw(b, Evoker, null, new PlayerPose(), ex, g - 1, t, true);
        if (casting)
        {
            for (var i = 0; i < 8; i++)
            {
                var f = Paint.Mod((p - 0.8) * 1.5 + (double)i / 8, 1);
                b.Set(ex + 2 + (int)(Math.Sin(i * 2 + t * 3) * 7), g - 34 - (int)(f * 14), i % 2 == 0 ? 0xB0B0C8u : 0x8A8AA8u);
            }
        }
        // 尖牙：从唤魔者脚下一路排到玩家
        var from = ex + 16;
        var to = px - 6;
        for (var k = 0; k < 7; k++)
        {
            var at = 2.0 + k * 0.18;
            var q = p - at;
            if (!(q > 0 && q < 0.9)) continue;
            var h = q < 0.25 ? (int)(q / 0.25 * 10) : (q < 0.6 ? 10 : (int)((0.9 - q) / 0.3 * 10));
            Fangs(b, from + (to - from) * k / 6, g, h, q > 0.3);
        }
        // 恼鬼：拿着铁剑绕着飞
        for (var i = 0; i < 2; i++)
        {
            var a = t * 1.4 + i * Math.PI;
            var vx = ex + 14 + (int)(Math.Cos(a) * 30);
            var vy = g - 46 + (int)(Math.Sin(a * 1.3) * 10);
            b.Blit(Vex, vx, vy, flip: Math.Sin(a) > 0);
        }
        // 玩家：被咬中 → 图腾生效
        var hit = p >= 3.1 && p <= 5.5;
        var pose = Player.Idle(t, new PlayerProps(), 3);
        if (hit)
        {
            pose.Hurt = Math.Max(0, 1 - (p - 3.1) / 0.6);
            pose.Lift = p < 3.4 ? 2 : 0;
        }
        var props = new PlayerProps { Held = p < 3.1 || p > 6.5 ? new HeldItem(Totem, swings: false) : null };
        Player.Draw(b, cast.Player, props, pose, px, g - 1, t);
        if (hit)
        {
            var f = (p - 3.1) / 2.4;
            // 金绿色的粒子炸开
            uint[] particleTones = { 0xF0D060u, 0x3AD860u, 0xF8F0A0u, 0x8AE85Au };
            for (var i = 0; i < 36; i++)
            {
                var a = (double)i / 36 * 2 * Math.PI + DotMath.Noise(i, 0, 150);
                var r = 4 + f * 30 * (0.5 + DotMath.Noise(i, 1, 150));
                var x = px + 4 + (int)(Math.Cos(a) * r);
                var y = g - 18 + (int)(Math.Sin(a) * r * 0.8 + f * f * 10);
                if (Math.Sin(t * 20 + i) > f * 1.6 - 0.8) b.Set(x, y, particleTones[i % 4]);
            }
            // 图腾在面前升起，放大一倍，左右翻转像在转
            var s = f < 0.7 ? 2 : 1;
            var spin = (int)(t * 6) % 2 == 0;
            if (f < 0.85)
                b.Blit(Totem, px - 6 - (s - 1) * 5, g - 26 - (int)(f * 20), Totem.Width * s, Totem.Height * s, spin);
        }
    }
}
