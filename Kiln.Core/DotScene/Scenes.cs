namespace Kiln.Core.DotScene;

/// 26.2 Chaos Cubed：硫磺洞穴与硫磺立方（对照原版 SulfurCaves）
public class SulfurCaves : IDotScene
{
    public string Id => "26.2";
    public uint Background => 0x0D0C09u;

    private static readonly uint[] Wall = { 0x3B3523u, 0x433C27u, 0x4C4429u, 0x35301Fu, 0x3F3824u };
    private static readonly uint[] Sulfur = { 0xB89F3Eu, 0xCFB54Bu, 0x9E8834u, 0xC2A945u };
    private static readonly uint[] Cinnabar = { 0xA9443Cu, 0xB8504Au, 0x94362Fu, 0xA03D36u };
    private static readonly uint[] CinnabarDeep = { 0x7E2C26u, 0x8A3029u, 0x6E2621u };

    /// 硫磺立方：半透明的米黄色方块，脸在下半部。look 为眼睛的水平偏移，blink 时闭眼
    public static Sprite Cube(int n, int look, bool blink)
    {
        return new Sprite(n, n, (x, y) =>
        {
            if (x == 0 || x == n - 1 || y == 0 || y == n - 1) return 0xB4A462u;
            var big = n >= 12;
            var eyeW = big ? 2 : 1;
            var eyeLo = big ? n / 2 : n / 2;
            var eyeHi = big ? n / 2 + 2 : n / 2 + 1;
            var eyes = big ? new[] { 3, n - 5 } : new[] { 2, n - 3 };
            bool InEyes(int ex) => ex + look <= x && x < ex + look + eyeW;
            if (!blink && y >= eyeLo && y <= eyeHi && eyes.Any(InEyes)) return 0x2A2216u;
            if (blink && y == eyeHi && eyes.Any(InEyes)) return 0x6A5A34u;
            if (x - look >= n / 2 - 1 && x - look <= n / 2 && y == (big ? n / 2 + 4 : n / 2 + 2)) return 0x4E3D24u;
            if (y < n / 3 && x < n / 3 + 1) return 0xFFF6C8u;
            return DotMath.Noise(x, y, 7) < 0.18 ? 0xE2D18Cu : 0xF0E2A0u;
        });
    }

    /// 玩家手里的硫磺块（小方块，带一点立体感）
    public static readonly Sprite SulfurBlock = new(
        new[] { ".yyyyy.", "yYYYYYy", "yYyYYYy", "yYYYyYy", "dYYYYYd", "ddyyydd", ".ddddd." },
        new Dictionary<char, uint> { ['y'] = 0xCFB54Bu, ['Y'] = 0xE3CB5Eu, ['d'] = 0x9E8834u });

    private (int Ground, int PoolLo, int PoolHi, int PoolTop, int PoolBottom) Layout(DotBuffer b)
    {
        var ground = (int)(b.Rows * 0.6);
        var poolLo = (int)(b.Cols * 0.38);
        var poolHi = (int)(b.Cols * 0.6);
        return (ground, poolLo, poolHi, ground + 3, ground + 15);
    }

    public void PaintStatic(DotBuffer b)
    {
        var (ground, poolLo, poolHi, _, poolBottom) = Layout(b);
        var poolCount = poolHi - poolLo + 1;
        var poolCx = (poolLo + poolHi) / 2.0;
        // 洞壁：从洞顶的暗褐到下方的土黄，按有序抖动分档；硫磺池的光在洞壁上铺开
        uint[] wallRamp = { 0x1E180Cu, 0x2E2514u, 0x41351Cu, 0x574624u, 0x6B5A2Cu };
        uint[] glowRamp = { 0x574624u, 0x6E7A33u, 0x8DA344u, 0xB3C25Au };
        for (var y = 0; y < ground; y++)
        {
            for (var x = 0; x < b.Cols; x++)
            {
                var depth = (double)y / ground;
                var c = DotMath.DitherRamp(wallRamp, 0.15 + depth * 0.8, x, y);
                var dx = (x - poolCx) / (poolCount * 1.6);
                var dy = (ground - y) / (ground * 0.75);
                var g = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy));
                if (g > 0.08) c = DotMath.DitherRamp(glowRamp, g, x, y);
                // 墙面的方块纹理：稀疏的亮点与暗点
                var n = DotMath.Noise(x, y, 1);
                if (n > 0.965) c = DotMath.Shade(c, 1.25);
                else if (n < 0.03) c = DotMath.Shade(c, 0.7);
                b.Set(x, y, c);
            }
        }
        // 远处的岩柱：比洞壁更暗，增加层次
        var pillars = new[] { 0.05, 0.47, 0.93 };
        for (var k = 0; k < pillars.Length; k++)
        {
            var px = (int)(b.Cols * pillars[k]);
            for (var y = 0; y < ground; y++)
            {
                var w = 7 + (int)(4 * Math.Sin(y * 0.07 + k * 2));
                for (var dx = 0; dx < w; dx++)
                    if (DotMath.Noise(px + dx, y, 20 + k) > 0.08)
                        b.Set(px + dx, y, DotMath.DitherRamp(new uint[] { 0x16120Au, 0x231C0Fu, 0x2E2514u }, (double)y / ground, px + dx, y));
            }
        }
        // 从洞顶垂下的硫磺钟乳：尖端更亮
        var x0 = 9;
        while (x0 < b.Cols)
        {
            var len = 8 + (int)(DotMath.Noise(x0, 0, 9) * ground * 0.36);
            var w = 3 + (int)(DotMath.Noise(x0, 1, 9) * 5);
            for (var dy = 0; dy < len; dy++)
            {
                var ww = Math.Max(1, w - dy * w / len);
                for (var dx = 0; dx < ww; dx++)
                    b.Set(x0 + dx + (w - ww) / 2, dy,
                        DotMath.DitherRamp(new uint[] { 0x6E5A22u, 0x9E8834u, 0xCFB54Bu, 0xEFD769u }, (double)dy / len, x0 + dx, dy));
            }
            x0 += 11 + (int)(DotMath.Noise(x0, 2, 9) * 20);
        }
        // 墙上的硫磺晶簇
        for (var i = 0; i < 9; i++)
        {
            var cx = (int)(DotMath.Noise(i, 0, 51) * b.Cols);
            var cy = (int)(ground * (0.25 + DotMath.Noise(i, 1, 51) * 0.6));
            var offsets = new (int Dx, int Dy, uint C)[]
            {
                (0, -2, 0xFFF08Au), (-1, -1, 0xE8D25Au), (0, -1, 0xFFF08Au), (1, -1, 0xE8D25Au), (-1, 0, 0xCFB54Bu),
                (0, 0, 0xE8D25Au), (1, 0, 0xCFB54Bu), (2, 0, 0xB89F3Eu), (-2, 0, 0xB89F3Eu),
            };
            foreach (var (dx, dy, c) in offsets) b.Set(cx + dx, cy + dy, c);
        }
        // 地面：顶面是亮的辰砂，下面辰砂与硫磺方块交错，越深越暗
        for (var y = ground; y < b.Rows; y++)
        {
            var d = y - ground;
            for (var x = 0; x < b.Cols; x++)
            {
                uint c;
                if (d == 0) c = DotMath.Noise(x, y, 2) < 0.3 ? 0xF08A80u : 0xE0706Au;
                else if (d < 4) c = DotMath.Pick(new uint[] { 0xD0605Au, 0xC4554Cu, 0xDA6A62u }, x, y);
                else if (d < 16) c = DotMath.Pick(new uint[] { 0xB04A42u, 0xA9443Cu, 0xBC524Au, 0x9C3E36u }, x, y);
                else
                {
                    var blockC = ((x / 16) + (d / 16)) % 3 == 0
                        ? DotMath.Pick(new uint[] { 0xB89F3Eu, 0xA38C36u }, x, y, 5)
                        : DotMath.Pick(new uint[] { 0x8A3029u, 0x7E2C26u }, x, y, 6);
                    c = DotMath.Dither(x, y, (double)(d - 16) / Math.Max(1, b.Rows - ground - 16) * 0.85)
                        ? DotMath.Shade(blockC, 0.45) : blockC;
                }
                b.Set(x, y, c);
            }
        }
        // 硫磺池：地面挖下去一格。水面以上是被池水照亮的洞壁，池壁是辰砂，池水在动态层
        for (var y = ground; y < poolBottom; y++)
            for (var x = poolLo; x <= poolHi; x++)
                b.Set(x, y, DotMath.DitherRamp(glowRamp, 0.95 - (y - ground) * 0.02, x, y));
        for (var y = ground; y < poolBottom + 1; y++)
        {
            b.Set(poolLo - 1, y, 0xA03D36u);
            b.Set(poolHi + 1, y, 0xA03D36u);
        }
        for (var x = poolLo; x <= poolHi; x++) b.Set(x, poolBottom, 0xA03D36u);
        // 地面上的硫磺草丛
        for (var gx = 4; gx < b.Cols; gx += 19)
        {
            if ((gx >= poolLo && gx <= poolHi) || (gx + 7 >= poolLo && gx + 7 <= poolHi)) continue;
            var h = 3 + (int)(DotMath.Noise(gx, 0, 11) * 4);
            for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < 7 - dy; dx++)
                    if (DotMath.Noise(gx + dx, dy, 12) > 0.25)
                        b.Set(gx + dx + dy / 2, ground - 1 - dy,
                            DotMath.DitherRamp(new uint[] { 0xB89F3Eu, 0xE8D25Au, 0xFFF08Au }, (double)dy / h, gx + dx, dy));
        }
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var (ground, poolLo, poolHi, poolTop, poolBottom) = Layout(b);
        var poolCount = poolHi - poolLo + 1;
        // 池水：表面起伏，水体明暗缓慢流动
        for (var x = poolLo; x <= poolHi; x++)
        {
            var surface = poolTop + (Math.Sin(x * 0.45 + t * 2.6) > 0.55 ? -1 : 0);
            for (var y = surface; y < poolBottom; y++)
            {
                var w = Math.Sin(x * 0.28 + y * 0.55 - t * 1.8);
                var c = y == surface ? 0xC8FFEAu : (w > 0.55 ? 0x5BE3B4u : (w < -0.6 ? 0x2AA582u : 0x3BC79Au));
                b.Set(x, y, c);
            }
        }
        // 气泡：从池底升起，到水面破掉
        for (var i = 0; i < 7; i++)
        {
            var p = Paint.Mod(t * (0.32 + i % 3 * 0.08) + i * 0.37, 1);
            var bx = poolLo + 3 + (int)(DotMath.Noise(i, 0, 21) * (poolCount - 6));
            var by = poolBottom - 1 - (int)(p * (poolBottom - poolTop));
            if (p < 0.9)
            {
                b.Set(bx, by, 0xFFF27Au);
                if (p > 0.4) b.Set(bx + 1, by, 0xFFF27Au);
            }
            else
            {
                foreach (var (dx, dy) in new[] { (-2, -1), (2, -1), (0, -3), (-1, -2), (1, -2) })
                    b.Set(bx + dx, poolTop + dy, 0xF2EC9Cu);
            }
        }
        // 硫磺立方：节奏不一的跳，小的会在地上滑一段；眼睛看向玩家，会眨眼
        void CubeAt(int n, int x, (double Start, double Height)[] hops, double cycle,
            (double S0, double S1, int Dist)? slide, (double Lo, double Hi)? lookAway, double blinkEvery)
        {
            var p = Paint.Mod(t, cycle);
            double lift = 0, squash = 0, dx = 0;
            foreach (var h in hops)
            {
                var k = (p - h.Start) / 0.62;
                if (k is >= 0 and < 1) lift = 4 * k * (1 - k) * h.Height;
                var land = (p - h.Start - 0.62) / 0.3;
                if (land is >= 0 and < 1) squash = Math.Max(squash, (1 - land) * 0.22);
            }
            if (slide is { } s)
            {
                // 滑出去再滑回来，滑的时候身体压低拉长
                var @out = DotMath.Ramp(p, s.S0, s.S0 + (s.S1 - s.S0) * 0.4);
                var back = DotMath.Ramp(p, s.S0 + (s.S1 - s.S0) * 0.6, s.S1);
                dx = s.Dist * (@out - back);
                if (p > s.S0 && p < s.S1) squash = Math.Max(squash, 0.12);
            }
            // 静止时也有一点果冻似的起伏
            if (lift == 0 && squash == 0) squash = Math.Max(0, Math.Sin(t * 3.1 + n)) * 0.05;
            var looking = lookAway is { } la && p >= la.Lo && p <= la.Hi ? -1 : 1;
            var blink = Paint.Mod(t + n, blinkEvery) < 0.14;
            var sprite = Cube(n, looking, blink);
            var sq = (int)Math.Round(n * squash);
            var cx = x + (int)Math.Round(dx);
            var shadowW = Math.Max(2, n - 2 - (int)(lift / 3));
            for (var i = 0; i < shadowW; i++) b.Set(cx + (n - shadowW) / 2 + i, ground, 0x5A1F1Au);
            b.Blit(sprite, cx - sq / 2, ground - 1 - (int)Math.Round(lift), n + sq, n - sq);
        }
        CubeAt(16, (int)(b.Cols * 0.13), new[] { (0.4, 12.0), (3.0, 12.0), (3.75, 7.0), (6.6, 14.0) }, 8.4, null, null, 3.7);
        CubeAt(9, (int)(b.Cols * 0.25), new[] { (1.1, 9.0), (2.4, 8.0), (6.9, 10.0) }, 7.3, (3.6, 5.6, 9), (4.4, 5.2), 2.9);
        // 洞里飘着的硫磺微粒
        for (var i = 0; i < 36; i++)
        {
            var speed = 0.015 + DotMath.Noise(i, 1, 31) * 0.03;
            var p = Paint.Mod(t * speed + DotMath.Noise(i, 2, 31), 1);
            var px = (int)(DotMath.Noise(i, 3, 31) * b.Cols) + (int)(Math.Sin(t * 0.7 + i) * 2);
            var py = ground - 2 - (int)(p * (ground - 4));
            if (Math.Sin(t * 2 + i) > -0.3) b.Set(px, py, i % 4 == 0 ? 0xF2E68Au : 0xB9A64Bu);
        }
        // 玩家手里拿着一块硫磺：硫磺立方对方块很感兴趣
        var props = new PlayerProps { Held = new HeldItem(SulfurBlock, swings: false) };
        Player.Draw(b, cast.Player, props, Player.Idle(t, props), (int)(b.Cols * 0.76), ground - 1, t);
    }
}

/// 还没有专属场景的版本：主世界平原（对照原版 Plains）
public class Plains : IDotScene
{
    public string Id => "plains";
    public uint Background => 0x0B0D12u;

    public void PaintStatic(DotBuffer b)
    {
        var ground = (int)(b.Rows * 0.6);
        for (var y = 0; y < ground; y++)
        {
            var k = (double)y / ground;
            var c = k < 0.5 ? 0x6E98ECu : (k < 0.8 ? 0x86AAF2u : 0xA5C1F6u);
            for (var x = 0; x < b.Cols; x++) b.Set(x, y, DotMath.Noise(x, y, 1) < 0.04 ? DotMath.Shade(c, 1.06) : c);
        }
        // 远山
        for (var x = 0; x < b.Cols; x++)
        {
            var h = (int)((Math.Sin(x * 0.03) * 0.5 + 0.5) * 18 + DotMath.Noise(x / 6, 0, 2) * 6);
            for (var y = ground - h; y < ground; y++) b.Set(x, y, DotMath.Pick(new uint[] { 0x5F8C4Eu, 0x547F45u, 0x6A9757u }, x, y, 3));
        }
        // 草方块与泥土
        for (var y = ground; y < b.Rows; y++)
        {
            var depth = Math.Max(0.4, 1 - (double)(y - ground) / (b.Rows - ground) * 0.6);
            for (var x = 0; x < b.Cols; x++)
            {
                uint c;
                if (y - ground < 3 || (y - ground < 6 && DotMath.Noise(x, y, 4) < 0.4))
                    c = DotMath.Pick(new uint[] { 0x5DA03Fu, 0x6DB34Au, 0x4F8F35u }, x, y, 5);
                else if (y - ground < 48)
                    c = DotMath.Pick(new uint[] { 0x8A5A33u, 0x6F4627u, 0x9B6B3Fu, 0x7C4F2Cu }, x, y, 6);
                else
                    c = DotMath.Pick(new uint[] { 0x7F7F7Fu, 0x6E6E6Eu, 0x8C8C8Cu }, x, y, 7);
                b.Set(x, y, DotMath.Shade(c, depth));
            }
        }
        // 一棵橡树
        var tx = (int)(b.Cols * 0.22);
        b.Fill(tx, ground - 40, 4, 40, 0x6B5133u);
        for (var y = ground - 64; y < ground - 32; y++)
            for (var x = tx - 18; x < tx + 22; x++)
                if (DotMath.Noise(x, y, 8) > 0.12)
                    b.Set(x, y, DotMath.Pick(new uint[] { 0x3E7A2Cu, 0x4A8A34u, 0x356A26u }, x, y, 9));
    }

    public void PaintDynamic(DotBuffer b, double t, SceneCast cast)
    {
        var ground = (int)(b.Rows * 0.6);
        // 方块云从右往左飘
        for (var i = 0; i < 5; i++)
        {
            var w = 24 + (int)(DotMath.Noise(i, 0, 41) * 30);
            var y = 6 + (int)(DotMath.Noise(i, 1, 41) * ground * 0.35);
            var span = b.Cols + (double)w;
            var x = (int)(span - Paint.Mod(t * (2 + DotMath.Noise(i, 2, 41) * 2) + DotMath.Noise(i, 3, 41) * span, span)) - w;
            b.Fill(x, y, w, 4, 0xF4F7FFu);
            b.Fill(x + 6, y - 3, Math.Max(4, w - 14), 3, 0xF4F7FFu);
            for (var dx = 0; dx < w; dx += 2) b.Set(x + dx, y + 4, 0xD5E1FBu);
        }
        Player.Draw(b, cast.Player, null, Player.Idle(t, new PlayerProps()), (int)(b.Cols * 0.76), ground - 1, t);
    }
}



