namespace Kiln.Core.DotScene;

/// 斜 45° 的玩家外观：看得到正面，右侧再露出压窄、压暗的侧面（对应原版 PlayerLook）
public class PlayerLook
{
    public Sprite HeadFront = null!;      // 8×8
    public Sprite HeadSide = null!;       // 4×8（左侧面压窄）
    public Sprite HeadFrontBlink = null!;
    public Sprite BodyFront = null!;      // 8×12
    public Sprite BodySide = null!;       // 2×12
    public Sprite NearArm = null!;        // 左臂：正面 + 外侧面，靠近镜头
    public Sprite FarArm = null!;         // 右臂：在身体后面，只露出 3 列
    public Sprite Legs = null!;           // 两条腿的正面 + 侧面，10×12
    /// 村民和灾厄村民的大鼻子垂过下巴的那一截
    public Sprite? Nose;
    /// 村民抱在胸前的双手（有它时不画两只胳膊）
    public Sprite? CrossedArms;

    /// 从 64×64（或旧版 64×32）皮肤取各个面，外层（帽子、外套、袖子、裤子）叠在基础层上。
    /// pixels 为 BGRA（WinRT BitmapDecoder 的 Bgra8），自顶向下排列。
    public static PlayerLook? FromSkin(byte[] pixels, int w, int h, bool slim)
    {
        if (w != 64 || (h != 64 && h != 32)) return null;
        var legacy = h == 32;
        (uint Rgb, byte A) Pixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return (0, 0);
            var i = (y * w + x) * 4;
            return (((uint)pixels[i + 2] << 16) | ((uint)pixels[i + 1] << 8) | pixels[i], pixels[i + 3]);
        }
        /// 取一块区域；step 为 2 时隔列取样（把侧面压窄）；k 为明暗
        uint[][] Face(int x0, int y0, int fw, int fh, (int X, int Y)? overlay = null, bool flip = false, int step = 1, double k = 1)
        {
            var result = new uint[fh][];
            for (var y = 0; y < fh; y++)
            {
                var row = new List<uint>();
                for (var xx = 0; xx < fw; xx += step)
                {
                    var x = flip ? fw - 1 - xx : xx;
                    if (!legacy && overlay is { } o)
                    {
                        var (c, a) = Pixel(o.X + x, o.Y + y);
                        if (a > 127)
                        {
                            row.Add(0xFF00_0000u | DotMath.Shade(c, k));
                            continue;
                        }
                    }
                    var (bc, ba) = Pixel(x0 + x, y0 + y);
                    row.Add(ba > 0 || legacy ? 0xFF00_0000u | DotMath.Shade(bc, k) : 0);
                }
                result[y] = row.ToArray();
            }
            return result;
        }
        Sprite SpriteOf(params uint[][][] cols)
        {
            // 把几块同高的区域左右拼起来
            var height = cols.Length > 0 ? cols[0].Length : 0;
            var width = cols.Sum(c => c.Max(r => r.Length));
            return new Sprite(width, height, (px, py) =>
            {
                var xx = px;
                foreach (var c in cols)
                {
                    var row = c[py];
                    if (xx < row.Length)
                    {
                        var v = row[xx];
                        return v == 0 ? null : v & 0xFFFFFFu;
                    }
                    xx -= row.Length;
                }
                return null;
            });
        }
        const double side = 0.78;
        var front = Face(8, 8, 8, 8, overlay: (40, 8));
        var look = new PlayerLook { HeadFront = SpriteOf(front) };
        // 眨眼：眼睛所在的第 4 行换成上一行的颜色
        var closed = front.Select(r => r.ToArray()).ToArray();
        closed[4] = front[3];
        look.HeadFrontBlink = SpriteOf(closed);
        look.HeadSide = SpriteOf(Face(16, 8, 8, 8, overlay: (48, 8), step: 2, k: side));
        look.BodyFront = SpriteOf(Face(20, 20, 8, 12, overlay: (20, 36)));
        look.BodySide = SpriteOf(Face(28, 20, 4, 12, overlay: (28, 36), step: 2, k: side));
        var armW = slim ? 3 : 4;
        if (legacy)
        {
            look.NearArm = SpriteOf(Face(44, 20, 4, 12, flip: true), Face(40, 20, 4, 12, flip: true, step: 2, k: side));
            look.FarArm = SpriteOf(Face(44, 20, 4, 12, step: 1, k: 0.82).Select(r => r.Skip(r.Length - 3).ToArray()).ToArray());
            look.Legs = SpriteOf(Face(4, 20, 4, 12), Face(4, 20, 4, 12, flip: true), Face(0, 20, 4, 12, flip: true, step: 2, k: side));
        }
        else
        {
            look.NearArm = SpriteOf(Face(36, 52, armW, 12, overlay: (52, 52)),
                Face(slim ? 39 : 40, 52, 4, 12, overlay: (slim ? 55 : 56, 52), step: 2, k: side));
            look.FarArm = SpriteOf(Face(44, 20, armW, 12, overlay: (44, 36), k: 0.82).Select(r => r.Skip(r.Length - 3).ToArray()).ToArray());
            look.Legs = SpriteOf(Face(4, 20, 4, 12, overlay: (4, 36)),
                Face(20, 52, 4, 12, overlay: (4, 52)),
                Face(24, 52, 4, 12, overlay: (8, 52), step: 2, k: side));
        }
        return look;
    }

    /// 默认外观：在代码里画的史蒂夫风格皮肤（青色上衣、蓝裤子、深棕头发），或 Alex 风格（橙发、绿上衣、细手臂）
    public static PlayerLook Default(bool alex = false) =>
        FromSkin(DefaultSkinBytes(alex), 64, 64, slim: alex)!;

    public static readonly PlayerLook Steve = Default(false);
    public static readonly PlayerLook Alex = Default(true);

    internal static byte[] DefaultSkinBytes(bool alex)
    {
        var px = new uint[64 * 64];
        void Put(int x, int y, string[] rows, Dictionary<char, uint> pal)
        {
            for (var dy = 0; dy < rows.Length; dy++)
                for (var dx = 0; dx < rows[dy].Length; dx++)
                    if (pal.TryGetValue(rows[dy][dx], out var c))
                        px[(y + dy) * 64 + x + dx] = c;
        }
        var pal = alex
            ? new Dictionary<char, uint>
            {
                ['H'] = 0xE3792Bu, ['h'] = 0xC7601Du, ['S'] = 0xF2CBA4u, ['s'] = 0xE1B48Eu, ['W'] = 0xF4F4F4u, ['B'] = 0x3E8A3Au,
                ['n'] = 0xE1B48Eu, ['M'] = 0xC98E72u, ['T'] = 0x6CA644u, ['t'] = 0x578C33u, ['P'] = 0x6A4A30u, ['p'] = 0x57391Fu,
                ['G'] = 0x45372Au, ['g'] = 0x382C21u,
            }
            : new Dictionary<char, uint>
            {
                ['H'] = 0x3B2717u, ['h'] = 0x2A1B10u, ['S'] = 0xB78A6Bu, ['s'] = 0x9C7257u, ['W'] = 0xF4F4F4u, ['B'] = 0x4C5DBBu,
                ['n'] = 0x8E6249u, ['M'] = 0x6B4733u, ['T'] = 0x22A3A3u, ['t'] = 0x198282u, ['P'] = 0x3A3FA0u, ['p'] = 0x2C3080u,
                ['G'] = 0x5B5B5Bu, ['g'] = 0x464646u,
            };
        if (alex)
        {
            Put(8, 8, new[] { "HHHHHHHH", "HHHHHHHH", "HSSSSHHH", "SSSSSShH", "SWBSSBWH", "SSSSSSSH", "SSSMMSSS", "sSSSSSSs" }, pal);
            Put(16, 8, new[] { "HHHHHHHH", "HHHHHHHH", "SSHHHHHH", "SSSHHHHH", "SSSHHHHh", "SSSSHHHh", "SSSSHHHh", "sSSSHHHh" }, pal);
        }
        else
        {
            Put(8, 8, new[] { "HHHHHHHH", "HHHHHHHH", "HSSSSSSH", "SSSSSSSS", "SWBSSBWS", "SSSnnSSS", "SSMMMMSS", "sSSSSSSs" }, pal);
            Put(16, 8, new[] { "HHHHHHHH", "HHHHHHHH", "SSHHHHHH", "SSSHHHHh", "SSSSHHHh", "SSSSSSHh", "SSSSSSSh", "sSSSSSSs" }, pal);
        }
        var shirt = new[] { "TTTSSTTT" }.Concat(Enumerable.Repeat("TTTTTTTT", 10)).Concat(new[] { "tttttttt" }).ToArray();
        Put(20, 20, shirt, pal);
        Put(28, 20, Enumerable.Repeat("TTTt", 11).Concat(new[] { "tttt" }).ToArray(), pal);
        var w = alex ? 3 : 4;
        var arm = Enumerable.Repeat(new string('T', w), 4).Concat(Enumerable.Repeat(new string('S', w), 7)).Concat(new[] { new string('s', w) }).ToArray();
        var armSide = Enumerable.Repeat("TTTt", 4).Concat(Enumerable.Repeat("SSSs", 7)).Concat(new[] { "ssss" }).ToArray();
        Put(44, 20, arm, pal);
        Put(40, 20, armSide, pal);
        Put(36, 52, arm, pal);
        Put(alex ? 39 : 40, 52, armSide, pal);
        var leg = Enumerable.Repeat("PPPP", 9).Concat(new[] { "pppp", "GGGG", "gggg" }).ToArray();
        var legSide = Enumerable.Repeat("PPPp", 9).Concat(new[] { "pppp", "GGGg", "gggg" }).ToArray();
        Put(4, 20, leg, pal);
        Put(20, 52, leg, pal);
        Put(24, 52, legSide, pal);
        Put(0, 20, legSide, pal);
        // BGRA（自顶向下）
        var bytes = new byte[64 * 64 * 4];
        for (var i = 0; i < px.Length; i++)
        {
            var c = px[i];
            bytes[i * 4] = (byte)(c & 0xFF);
            bytes[i * 4 + 1] = (byte)((c >> 8) & 0xFF);
            bytes[i * 4 + 2] = (byte)((c >> 16) & 0xFF);
            bytes[i * 4 + 3] = c == 0 ? (byte)0 : (byte)255;
        }
        return bytes;
    }

    /// 掠夺者（1.14，对照原版）
    public static readonly PlayerLook Pillager = new()
    {
        HeadFront = new Sprite(
            new[] { "LLLLLLLL", "SSSSSSSS", "SSSSSSSS", "SSSSSSSS", "BBBBBBBB", "SkGSSGkS", "SSSnnSSS", "SSSnnSSS", "SMSnnSMS", "sSSnnSSs" },
            new Dictionary<char, uint> { ['L'] = 0xB4B6B8u, ['S'] = 0x9A9C9Eu, ['s'] = 0x868A8Eu, ['B'] = 0x3A3A3Cu, ['k'] = 0x1E1E1Du, ['G'] = 0x3AA04Au, ['n'] = 0x7E8084u, ['M'] = 0x5A5C60u }),
        HeadSide = new Sprite(
            new[] { "LLLL", "SSSS", "SSSs", "SSSs", "BSSs", "SSSs", "SSss", "SSSs", "SSSs", "sSss" },
            new Dictionary<char, uint> { ['L'] = 0x9EA0A2u, ['S'] = 0x84868Au, ['s'] = 0x707276u, ['B'] = 0x343436u }),
        BodyFront = new Sprite(
            new[] { "cDcDDcDc", "DmDDmDDm", "cDcmcDcD", "DmDDmDmD", "cDcDDcDc", "DmDmDDmD", "cDcDcDcD", "KKKbbKKK", "DDDbbDDD", "mDmDDmDm", "DmDmmDmD", "mmDDDDmm" },
            new Dictionary<char, uint> { ['D'] = 0x2A2828u, ['c'] = 0x3A3434u, ['m'] = 0x6A2E34u, ['K'] = 0x1A1818u, ['b'] = 0x9A9C9Eu }),
        BodySide = new Sprite(
            Enumerable.Repeat("Dm", 7).Concat(new[] { "KK", "DD", "mD", "Dm", "mm" }).ToArray(),
            new Dictionary<char, uint> { ['D'] = 0x242222u, ['m'] = 0x58262Cu, ['K'] = 0x161414u }),
        NearArm = new Sprite(
            new[] { "DmDDm", "mDmDm", "DmDDm", "mDmDm", "DmDDm", "mDmDm", "DmDDm", "cccSs", "SSSSs", "SSSSs", "SSSss", "sssss" },
            new Dictionary<char, uint> { ['D'] = 0x2A2828u, ['m'] = 0x6A2E34u, ['c'] = 0x3A3434u, ['S'] = 0x9A9C9Eu, ['s'] = 0x7E8084u }),
        FarArm = new Sprite(
            new[] { "DmD", "mDm", "DmD", "mDm", "DmD", "mDm", "DmD", "ccc", "SSS", "SSS", "SSs", "sss" },
            new Dictionary<char, uint> { ['D'] = 0x242222u, ['m'] = 0x58262Cu, ['c'] = 0x343030u, ['S'] = 0x84868Au, ['s'] = 0x6E7074u }),
        Legs = new Sprite(
            Enumerable.Repeat("PpPPPpPPRr", 9).Concat(new[] { "pppppppprr", "BBBBBBBBbb", "BBBBBBBBbb" }).ToArray(),
            new Dictionary<char, uint> { ['P'] = 0x8A4A2Au, ['p'] = 0x6A3420u, ['R'] = 0x6E3A22u, ['r'] = 0x582E1Au, ['B'] = 0x3A2418u, ['b'] = 0x2E1C12u }),
        Nose = new Sprite(new[] { "nN", "nN" }, new Dictionary<char, uint> { ['n'] = 0x8A8C90u, ['N'] = 0x6E7074u }),
    };

    /// 村民（1.14，对照原版）
    public static readonly PlayerLook Villager = new()
    {
        HeadFront = new Sprite(
            new[] { "HHHHHHHH", "SSSSSSSS", "SSSSSSSS", "SSSSSSSS", "SUUUUUUS", "SkGSSGkS", "SSSnnSSS", "SSSnnSSS", "SSMnnMSS", "sSSnnSSs" },
            new Dictionary<char, uint> { ['H'] = 0x94664Eu, ['S'] = 0xA8765Cu, ['s'] = 0x946450u, ['U'] = 0x4A3022u, ['k'] = 0x1E1E1Du, ['G'] = 0x3AA04Au, ['n'] = 0x8E5E46u, ['M'] = 0x6E4632u }),
        HeadSide = new Sprite(
            new[] { "HHHH", "SSSs", "SSSs", "SSSs", "USSs", "SSSs", "SSss", "SSSs", "SSSs", "sSss" },
            new Dictionary<char, uint> { ['H'] = 0x845A44u, ['S'] = 0x96684Eu, ['s'] = 0x845A44u, ['U'] = 0x40281Cu }),
        BodyFront = new Sprite(
            new[] { "RRRccRRR" }.Concat(Enumerable.Repeat("RRRrRRRR", 10)).Concat(new[] { "rrrrrrrr" }).ToArray(),
            new Dictionary<char, uint> { ['R'] = 0x7A5A4Au, ['r'] = 0x6A4C3Eu, ['c'] = 0x5A3E32u }),
        BodySide = new Sprite(
            Enumerable.Repeat("Rr", 12).ToArray(),
            new Dictionary<char, uint> { ['R'] = 0x6A4C3Eu, ['r'] = 0x5A4034u }),
        NearArm = new Sprite(
            Enumerable.Repeat("RRRRr", 12).ToArray(),
            new Dictionary<char, uint> { ['R'] = 0x7A5A4Au, ['r'] = 0x6A4C3Eu }),
        FarArm = new Sprite(
            Enumerable.Repeat("RRr", 12).ToArray(),
            new Dictionary<char, uint> { ['R'] = 0x6A4C3Eu, ['r'] = 0x5A4034u }),
        Legs = new Sprite(
            Enumerable.Repeat("RRRrRRRRrr", 10).Concat(new[] { "GGGGGGGGgg", "GGGGGGGGgg" }).ToArray(),
            new Dictionary<char, uint> { ['R'] = 0x7A5A4Au, ['r'] = 0x6A4C3Eu, ['G'] = 0x7A7A7Au, ['g'] = 0x5E5E5Eu }),
        Nose = new Sprite(new[] { "nN", "nN" }, new Dictionary<char, uint> { ['n'] = 0x9A6A50u, ['N'] = 0x7E5440u }),
        CrossedArms = new Sprite(
            new[] { "AAAAAAAAAA..", "AhhAAAAAhhAA", "AAAAAAAAAAAa", ".aaaaaaaaaa." },
            new Dictionary<char, uint> { ['A'] = 0x6E4E40u, ['a'] = 0x5A3E32u, ['h'] = 0xA8765Cu }),
    };

    /// 穿上护甲：头盔包住头顶和两侧，胸甲盖住身体和肩膀，护腿与靴子盖住腿
    public PlayerLook Wearing(Armor armor)
    {
        var l = new PlayerLook
        {
            HeadFront = HeadFront, HeadFrontBlink = HeadFrontBlink, HeadSide = HeadSide,
            BodyFront = BodyFront, BodySide = BodySide, NearArm = NearArm, FarArm = FarArm,
            Legs = Legs, Nose = Nose, CrossedArms = CrossedArms,
        };
        uint Tone(ArmorMaterial m, int x, int y, bool edge, bool light = false)
        {
            if (edge) return m.Tones[1];
            if (light && DotMath.Noise(x, y, 81) < 0.45) return m.Tones[4];
            return DotMath.Noise(x, y, 82) < 0.3 ? m.Tones[2] : m.Tones[3];
        }
        if (armor.Helmet is { } hm)
        {
            uint? Helm(int x, int y, int w)
            {
                if (y <= 1) return Tone(hm, x, y, edge: false, light: y == 0);
                if (y == 2) return Tone(hm, x, y, edge: x == 0 || x == w - 1);
                if (y is >= 3 and <= 5 && (x == 0 || x == w - 1)) return Tone(hm, x, y, edge: true);
                return null;
            }
            l.HeadFront = HeadFront.Painted((x, y) => Helm(x, y, 8));
            l.HeadFrontBlink = HeadFrontBlink.Painted((x, y) => Helm(x, y, 8));
            l.HeadSide = HeadSide.Painted((x, y) => y <= 2 || (y <= 6 && x == HeadSide.Width - 1)
                ? DotMath.Shade(Tone(hm, x, y, edge: y == 2), 0.85) : null);
        }
        if (armor.Chest is { } cm)
        {
            l.BodyFront = BodyFront.Painted((x, y) => y <= 9 ? Tone(cm, x, y, edge: x == 0 || x == 7 || y == 9, light: x < 3 && y < 4) : null);
            l.BodySide = BodySide.Painted((x, y) => y <= 9 ? DotMath.Shade(Tone(cm, x, y, edge: y == 9), 0.82) : null);
            l.NearArm = NearArm.Painted((x, y) => y <= 4 ? Tone(cm, x, y, edge: y == 4, light: y < 2) : null);
            l.FarArm = FarArm.Painted((x, y) => y <= 4 ? DotMath.Shade(Tone(cm, x, y, edge: y == 4), 0.85) : null);
        }
        if (armor.Legs != null || armor.Boots != null)
        {
            l.Legs = Legs.Painted((x, y) =>
            {
                if (armor.Boots is { } bm && y >= 9) return x >= 8 ? DotMath.Shade(Tone(bm, x, y, edge: false), 0.8) : Tone(bm, x, y, edge: y == 9);
                if (armor.Legs is { } lm && y <= 7) return x >= 8 ? DotMath.Shade(Tone(lm, x, y, edge: false), 0.8) : Tone(lm, x, y, edge: y == 0 || (y > 2 && (x == 3 || x == 4)));
                return null;
            });
        }
        return l;
    }
}

/// 护甲材质：从暗到亮五个颜色
public class ArmorMaterial(uint[] tones)
{
    public readonly uint[] Tones = tones;
    public static readonly ArmorMaterial Diamond = new(new uint[] { 0x0F5E55u, 0x1A9B8Au, 0x2BC7ACu, 0x4AEDD9u, 0xA1FBE8u });
    public static readonly ArmorMaterial Netherite = new(new uint[] { 0x1E1B1Eu, 0x2B282Bu, 0x3E3A3Eu, 0x4D494Du, 0x7A6F7Au });
    public static readonly ArmorMaterial Turtle = new(new uint[] { 0x1F4F22u, 0x2D7A30u, 0x3A9C3Du, 0x47BF4Au, 0x8FE08Au });
    public static readonly ArmorMaterial Gold = new(new uint[] { 0x7A5A12u, 0xB08A1Eu, 0xD9B02Eu, 0xF5D74Au, 0xFFF39Au });
    public static readonly ArmorMaterial Copper = new(new uint[] { 0x6E3420u, 0x9C4E36u, 0xC0644Au, 0xE0805Au, 0xF5A47Eu });
}

/// 穿在身上的护甲（每件可以单独给）
public class Armor
{
    public ArmorMaterial? Helmet;
    public ArmorMaterial? Chest;
    public ArmorMaterial? Legs;
    public ArmorMaterial? Boots;
    public static Armor Full(ArmorMaterial m) => new() { Helmet = m, Chest = m, Legs = m, Boots = m };
}

/// 拿在手里的东西。剑、锤这类会跟着手臂转动；方块、地图这类不转
public class HeldItem(Sprite sprite, bool swings = true)
{
    public readonly Sprite Sprite = sprite;
    public readonly bool Swings = swings;

    public static readonly HeldItem DiamondSword = new(new Sprite(
        new[] { "..o..", ".oLo.", ".oLo.", ".oMo.", ".oLo.", ".oMo.", ".oLo.", ".oMo.", ".oLo.", "tTTTt", "..h..", "..h..", ".tTt." },
        new Dictionary<char, uint> { ['o'] = 0x0F5E55u, ['L'] = 0xA1FBE8u, ['M'] = 0x4AEDD9u, ['t'] = 0x1A9B8Au, ['T'] = 0x2BC7ACu, ['h'] = 0x6B4A2Cu }));
    public static readonly HeldItem NetheriteSword = new(new Sprite(
        new[] { "..o..", ".oLo.", ".oLo.", ".oMo.", ".oLo.", ".oMo.", ".oLo.", ".oMo.", ".oLo.", "tTTTt", "..h..", "..h..", ".tTt." },
        new Dictionary<char, uint> { ['o'] = 0x1E1B1Eu, ['L'] = 0x8A7E8Au, ['M'] = 0x4D494Du, ['t'] = 0x2B282Bu, ['T'] = 0x5A4E44u, ['h'] = 0x3A2A22u }));
    public static readonly HeldItem Mace = new(new Sprite(
        new[] { ".k.k.k.", "kHhHhHk", "kHHhHHk", "dHhHHhd", "kHHHHHk", ".kdHdk.", "..kck..", "...b...", "...B...", "...b...", "...B...", "...b...", "...B...", "..kbk.." },
        new Dictionary<char, uint> { ['k'] = 0x24262Au, ['d'] = 0x3A3D42u, ['H'] = 0x5A5E66u, ['h'] = 0x9EA3ADu, ['c'] = 0x7A6A4Au, ['b'] = 0x8A93D8u, ['B'] = 0x5A63A8u }));
    public static readonly HeldItem Trident = new(new Sprite(
        TridentRows(),
        new Dictionary<char, uint> { ['l'] = 0xD9F2ECu, ['L'] = 0x9FE3D6u, ['M'] = 0x6FB8A8u, ['S'] = 0x4E8B7Eu, ['s'] = 0x3E7066u, ['G'] = 0x5FA394u, ['g'] = 0x2E5A52u }));
    public static readonly HeldItem Brush = new(new Sprite(
        new[] { ".FFF.", "FFfFF", "FfFfF", ".FFF.", ".cCc.", "..w..", "..W..", "..w..", "..W.." },
        new Dictionary<char, uint> { ['F'] = 0xE6D3A8u, ['f'] = 0xC9B184u, ['c'] = 0xB0602Eu, ['C'] = 0xD9844Au, ['w'] = 0x8B5A2Bu, ['W'] = 0xA36C38u }));
    /// 望远镜（1.17）：看的时候举到眼前
    public static readonly HeldItem Spyglass = new(new Sprite(
        new[] { "aAa", "oCo", "oCo", "ocO", "oCo", "dDd", "dDd" },
        new Dictionary<char, uint> { ['a'] = 0xB9A6E8u, ['A'] = 0x8E6FD6u, ['o'] = 0x6E3A1Eu, ['C'] = 0xE08A3Au, ['c'] = 0xF5B46Au, ['O'] = 0xB05A2Eu, ['d'] = 0x4A2414u, ['D'] = 0x6A3420u }), swings: false);
    /// 弩（1.14）
    public static readonly HeldItem Crossbow = new(new Sprite(
        new[] { "..s..", "..b..", "wWWWw", ".SbS.", "..W..", "..b..", "..W..", "..i..", "..W..", "..W..", "..w..", "..w.." },
        new Dictionary<char, uint> { ['W'] = 0x8A5A2Eu, ['w'] = 0x5E3A1Eu, ['b'] = 0xC8A878u, ['s'] = 0xC4C8CEu, ['S'] = 0x9A9EA4u, ['i'] = 0x6E7276u }));
    /// 蜂蜜瓶（1.15）
    public static readonly HeldItem HoneyBottle = new(new Sprite(
        new[] { "..cc..", "..gg..", ".gHHg.", "gHhHHg", "gHHHHg", "gHHhHg", ".gggg." },
        new Dictionary<char, uint> { ['c'] = 0xB05A3Au, ['g'] = 0xC8E4F0u, ['H'] = 0xF5A42Eu, ['h'] = 0xFFD06Au }), swings: false);
    /// 追溯指针（1.19）
    public static readonly HeldItem RecoveryCompass = new(new Sprite(
        new[] { "..oooo..", ".oGggGo.", "oGkCkkGo", "oGkCCkGo", "ogkkCkgo", "oGkkkkGo", ".oCCCCo.", "..oooo.." },
        new Dictionary<char, uint> { ['o'] = 0x1E2226u, ['G'] = 0x8A9096u, ['g'] = 0x5A6066u, ['k'] = 0x0E1A1Eu, ['C'] = 0x2AD6E8u }), swings: false);
    public static readonly HeldItem ExplorerMap = new(new Sprite(
        new[] { "oooooooo", "oPPPPPPo", "oPgGPPPo", "oPPGgPbo", "oPPPPbPo", "oPrPPPPo", "oPPPPPPo", "oooooooo" },
        new Dictionary<char, uint> { ['o'] = 0x8A6A3Au, ['P'] = 0xF2E6C2u, ['g'] = 0x5DA03Fu, ['G'] = 0x3E7A2Cu, ['b'] = 0x4A78EEu, ['r'] = 0xD8341Fu }), swings: false);

    private static string[] TridentRows()
    {
        var rows = new List<string> { "l.l.l", "L.L.L", "M.M.M", "MMMMM", ".MLM.", "..M.." };
        for (var i = 0; i < 9; i++)
        {
            if (i % 3 == 0) rows.Add("..s..");
            rows.Add("..S..");
        }
        rows.AddRange(new[] { "..G..", "..g..", "..G..", "..S..", "..s.." });
        return rows.ToArray();
    }
}

/// 一帧里玩家的姿势；场景可以自己算，也可以用 Player.Idle 的默认待机动作
public class PlayerPose
{
    public double ArmAngle = 0.06;    // 后手（右手，拿东西）向前抬起的角度
    public double ItemTilt;           // 物品在手里额外转的角度
    public double ItemUp;             // 不转的物品举到眼前的程度
    public int Lift;                  // 跳起的高度
    public int Dx;                    // 左右挪动
    public int HeadDip;
    public bool LookBack;
    public double HelmetLift;         // 摘下的头盔离开头顶多高
    public double Hurt;               // 受击变红
    public bool Sitting;
    public bool Sneaking;             // 潜行：腿保持直立，上身往前倾
    public bool Blocking;             // 举盾
    public bool Flying;               // 鞘翅滑翔
    public double? Walk;              // 走路步伐的相位（弧度）；null 为站着
}

/// 场景给玩家的道具
public class PlayerProps
{
    public HeldItem? Held;
    public Armor Armor = new();
    /// 摘下再戴回去的那顶头盔
    public ArmorMaterial? RemovableHelmet;
    /// 肩膀上的东西（比如 1.12 的鹦鹉）
    public Sprite? Shoulder;
    /// 副手的东西（比如 1.9 的盾）
    public Sprite? Offhand;
    /// 背上的鞘翅（1.9）
    public bool Elytra;
}



