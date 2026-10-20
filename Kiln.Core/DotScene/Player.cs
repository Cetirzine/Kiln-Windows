namespace Kiln.Core.DotScene;

/// 玩家的绘制（对应原版 enum Player）：画到局部画布再贴进场景，支持姿势动画
public static class Player
{
    /// 局部画布里头部正面左边缘的位置（Draw 的 x 对应这里）
    public const int CanvasX = 36;

    /// 鞘翅滑翔：身体放平，头在最前面、保持竖直往前看，腿拖在后面
    public static void DrawFlying(DotBuffer b, PlayerLook look, int x, int y, double t, bool facingRight)
    {
        var s = new DotBuffer(64, 44);
        const int ox = 26, oy = 22;   // 身体中心
        var flex = Math.Sin(t * 2.4) * 1.2;
        Sprite RotCcw(Sprite sp) => new(sp.Height, sp.Width,
            (xx, yy) => { var v = sp.Px[xx * sp.Width + (sp.Width - 1 - yy)]; return v == 0 ? null : v & 0xFFFFFFu; });
        /// 一片翅膀：根部在肩膀，很细，往后越来越宽，末端圆钝
        void Wing(int sx, int sy, int length, double spread, double dir, double tone)
        {
            for (var k = 0; k < length; k++)
            {
                var f = (double)k / (length - 1);
                var center = sy + dir * spread * f;
                var half = 0.6 + 4.4 * Math.Min(1, f / 0.7);
                if (f > 0.82) half *= 1 - (f - 0.82) / 0.18 * 0.55;
                var lo = (int)Math.Round(center - half);
                var hi = (int)Math.Round(center + half);
                for (var dy = lo; dy <= Math.Max(lo, hi); dy++)
                {
                    var outer = dir < 0 ? dy == lo : dy == hi;
                    var c = outer ? 0x4A5868u : (k % 4 == 0 && f > 0.15 ? 0x7A8A9Eu : (dy < center ? 0xB4C0CCu : 0x8A9AAEu));
                    if (f < 0.12) c = 0x5E6E80u;
                    s[sx + k, dy] = 0xFF00_0000u | DotMath.Shade(c, tone);
                }
            }
        }
        // 远处的翅膀：在身体上方往后上展开
        Wing(ox - 3, oy - 4, 26, 8 + flex, -1, 0.8);
        // 腿：拖在身体后面，两条腿一上一下地踢
        void Leg(int loCol, int hiCol, double kick, double tone)
        {
            var sp = new Sprite(4, 12, (xx, yy) =>
            {
                var v = look.Legs.Px[yy * look.Legs.Width + loCol + xx];
                return v == 0 ? null : DotMath.Shade(v & 0xFFFFFFu, tone);
            });
            s.BlitRotated(sp, ox + 5, oy, (2, 0), -Math.PI / 2 + kick);
        }
        var kickAngle = Math.Sin(t * 5.5) * 0.22;
        Leg(4, 7, -kickAngle, 0.8);
        Leg(0, 3, kickAngle, 1);
        // 身体：放平
        var body = RotCcw(look.BodyFront);
        s.Blit(body, ox - 6, oy + 3, body.Width, 7);
        // 近处的手臂贴在身体下侧
        var arm = RotCcw(look.NearArm);
        s.Blit(arm, ox - 5, oy + 5, arm.Width, 3);
        // 头：保持竖直，往前看
        s.Blit(look.HeadFront, ox - 15, oy + 2);
        s.Blit(look.HeadSide, ox - 7, oy + 2);
        // 近处的翅膀：在身体下方往后下展开
        Wing(ox - 2, oy + 3, 24, 5 - flex, 1, 1.0);
        for (var sy = 0; sy < s.Rows; sy++)
            for (var sx = 0; sx < s.Cols; sx++)
            {
                var v = s.Px[sy * s.Cols + sx];
                if (v == 0) continue;
                var tx = facingRight ? 2 * ox - sx : sx;
                b[x + tx - ox, y + sy - oy] = v;
            }
    }

    /// 默认待机：一直在呼吸；手里有东西时不时抬起来看一眼，有可摘的头盔时摘下来再戴回去，什么都没有就偶尔回头看看
    public static PlayerPose Idle(double t, PlayerProps props, double phase = 0)
    {
        var pose = new PlayerPose();
        var p = Mod(t + phase, 15);
        pose.ArmAngle = 0.06 + Math.Sin(t * 1.1) * 0.07;
        if (props.Held is { } held && p is >= 2.0 and <= 5.6)
        {
            var k = DotMath.Ramp(p, 2.0, 2.7) * (1 - DotMath.Ramp(p, 4.9, 5.6));
            pose.ArmAngle = (held.Swings ? 1.1 : 1.45) * k;
            pose.ItemUp = held.Swings ? 0 : k;
            pose.ItemTilt = held.Swings ? -0.5 * k : 0;
            pose.HeadDip = k > 0.6 ? 1 : 0;
        }
        if (props.RemovableHelmet != null && p is >= 8.0 and <= 11.6)
        {
            pose.ArmAngle = 2.75 * DotMath.Ramp(p, 8.0, 8.5) * (1 - DotMath.Ramp(p, 11.1, 11.6));
            pose.HelmetLift = 9 * DotMath.Ramp(p, 8.5, 9.1) * (1 - DotMath.Ramp(p, 10.4, 11.0));
        }
        else if ((props.Held == null && p is >= 6.5 and <= 7.7) || (props.RemovableHelmet == null && p is >= 10.0 and <= 10.9))
        {
            pose.LookBack = true;
        }
        return pose;
    }

    private static double Mod(double a, double b) => ((a % b) + b) % b;

    /// 画玩家：脚底在 feet 行，x 为面朝左时头部正面的左边缘；facingRight 时整个人左右翻转
    public static void Draw(DotBuffer b, PlayerLook look, PlayerProps? props, PlayerPose pose,
        int x, int feet, double t, bool facingRight = false)
    {
        props ??= new PlayerProps();
        look = look.Wearing(props.Armor);
        // 先画到一张局部画布上：左边留够地方，长矛、钓竿这类往前伸的东西不会被截掉
        var s = new DotBuffer(84, 64);
        var ax = CanvasX;
        var f = 60 - pose.Lift;
        var breath = Math.Sin(t * 2 * Math.PI / 3.4) > 0.35 ? 1 : 0;
        var legsH = pose.Sitting ? 10 : 12;
        // 潜行：腿保持直立，上身往前倾
        var tilt = pose.Sneaking ? 0.4 : 0.0;
        var bodyBottom = f - legsH - breath;
        var reach = (int)Math.Round(11 * tilt);
        var drop = pose.Sneaking ? 2 : 0;
        var shoulder = bodyBottom - 11 + drop;
        // 鞘翅：从肩膀后面垂到膝盖，往背后展开，微微扇动
        if (props.Elytra && !pose.Flying)
        {
            var spread = 2 + (int)Math.Round(Math.Sin(t * 1.8) * 1.2);
            for (var dy = 0; dy < 20; dy++)
            {
                var w = 3 + dy / 3 + spread * dy / 20;
                for (var dx = 0; dx < w; dx++)
                {
                    var c = dx == w - 1 ? 0x4A5868u : (dy % 4 == 0 ? 0xB4C0CCu : (dx % 3 == 0 ? 0x7A8898u : 0x8A98A8u));
                    s[ax + 9 + dx - reach / 2, shoulder - 1 + dy] = 0xFF00_0000u | c;
                }
            }
        }
        // 后手：绕肩膀旋转，拿东西也用这只手
        var pivotX = ax - reach - 1.5;
        var pivotY = (double)shoulder;
        // 走路时手脚的摆幅
        var swing = pose.Walk is { } w0 ? Math.Sin(w0) * 0.42 : 0;
        var walking = pose.Walk != null && !pose.Sitting;
        if (look.CrossedArms == null)
        {
            s.BlitRotated(look.FarArm, pivotX, pivotY, (1.5, 0), pose.ArmAngle + (walking ? swing * 0.7 : 0));
        }
        if (pose.Sitting)
        {
            // 坐着：大腿从髋部往前伸出去，小腿从膝盖垂下来
            var hipY = bodyBottom + 1;
            for (var leg = 0; leg < 2; leg++)
            {
                var c0 = leg == 0 ? 4 : 0;
                double tone = leg == 0 ? 0.82 : 1.0;
                for (var r = 0; r < 4; r++)
                    for (var k = 0; k < 8; k++)
                    {
                        var v = look.Legs.Px[Math.Min(5, k * 6 / 8) * look.Legs.Width + c0 + r];
                        if (v != 0) s[ax + 3 - k - leg, hipY + r] = 0xFF00_0000u | DotMath.Shade(v & 0xFFFFFFu, tone);
                    }
                for (var r = 0; r < 6; r++)
                    for (var c = 0; c < 4; c++)
                    {
                        var v = look.Legs.Px[(6 + r) * look.Legs.Width + c0 + c];
                        if (v != 0) s[ax - 5 + c - leg, hipY + 4 + r] = 0xFF00_0000u | DotMath.Shade(v & 0xFFFFFFu, tone);
                    }
            }
        }
        else if (walking)
        {
            // 走路：两条腿各自绕髋部转动，远处那条（暗一点）先画
            var legs = look.Legs;
            Sprite LegSprite(int[] cols) => new(cols.Length, legs.Height,
                (xx, yy) => { var v = legs.Px[yy * legs.Width + cols[xx]]; return v == 0 ? null : v & 0xFFFFFFu; });
            var hip = f - legs.Height + 1.0;
            // 右边那条腿连着侧面（第 8、9 列），左边那条只有正面
            var far = LegSprite(Enumerable.Range(4, 4).Concat(legs.Width > 8 ? Enumerable.Range(8, legs.Width - 8) : Enumerable.Empty<int>()).ToArray());
            var near = LegSprite(new[] { 0, 1, 2, 3 });
            s.BlitRotated(far, ax + 6, hip, (2, 0), -swing);
            s.BlitRotated(near, ax + 2, hip, (2, 0), swing);
        }
        else
        {
            s.Blit(look.Legs, ax, f);
        }
        // 上身：前倾时逐行往前错开，并略微压低
        void Torso(Sprite sp, int x0)
        {
            for (var r = 0; r < sp.Height; r++)
            {
                var up = sp.Height - 1 - r;
                var off = (int)Math.Round(up * tilt);
                var y0 = bodyBottom - up + (pose.Sneaking ? (int)Math.Round(up * 0.18) : 0);
                for (var c = 0; c < sp.Width; c++)
                {
                    var v = sp.Px[r * sp.Width + c];
                    if (v != 0) s[x0 + c - off, y0] = v;
                }
            }
        }
        Torso(look.BodyFront, ax);
        Torso(look.BodySide, ax + 8);
        if (look.CrossedArms is { } arms)
        {
            s.Blit(arms, ax - reach - 1, shoulder + 7);
        }
        else if (walking)
        {
            s.BlitRotated(look.NearArm, ax + 10 + look.NearArm.Width / 2.0, shoulder,
                (look.NearArm.Width / 2.0, 0), -swing * 0.7);
        }
        else
        {
            Torso(look.NearArm, ax + 10);
        }
        // 头：正面 + 侧面；回头时左右翻过来
        var blink = Mod(t + 1.3, 4.3) < 0.16;
        var headBottom = bodyBottom - 12 + pose.HeadDip + drop;
        // 未单独定义眨眼帧时沿用睁眼帧（对应原版 init 的默认值）
        var front = (blink ? look.HeadFrontBlink : look.HeadFront) ?? look.HeadFront;
        var hx = ax - reach;
        if (pose.LookBack)
        {
            s.Blit(look.HeadSide, hx, headBottom, flip: true);
            s.Blit(front, hx + 4, headBottom);
        }
        else
        {
            s.Blit(front, hx, headBottom);
            s.Blit(look.HeadSide, hx + 8, headBottom);
        }
        if (look.Nose is { } nose && !pose.LookBack)
        {
            // 接在脸正中间的鼻子下面，垂到下巴以下
            s.Blit(nose, hx + 3, headBottom + nose.Height);
        }
        if (props.RemovableHelmet is { } m)
        {
            // 可以摘下的头盔：一个包住头顶的壳，跟着手上下
            var lift = (int)Math.Round(pose.HelmetLift);
            var shell = new Sprite(14, 6, (xx, yy) =>
            {
                if (yy == 5 && (xx == 0 || xx == 13)) return m.Tones[1];
                if (yy == 5) return null;
                if (yy == 0 && (xx == 0 || xx == 13)) return null;
                return DotMath.Noise(xx, yy, 91) < 0.25 ? m.Tones[4] : (yy < 2 ? m.Tones[3] : m.Tones[2]);
            });
            s.Blit(shell, ax - 1, headBottom - 3 - lift);
        }
        if (props.Offhand is { } off)
        {
            if (pose.Blocking) s.Blit(off, ax - reach - off.Width + 3, shoulder + off.Height - 2);
            else s.Blit(off, ax + 12 - reach, bodyBottom + 4);
        }
        if (props.Shoulder is { } sh)
        {
            s.Blit(sh, ax + 7, shoulder - 1 + (Math.Sin(t * 2.3) > 0.7 ? -1 : 0));
        }
        // 手里的东西：剑与锤跟着手臂转，方块和地图举在眼前
        if (props.Held is { } held2)
        {
            var handX = pivotX - Math.Sin(pose.ArmAngle) * 10.5;
            var handY = pivotY + Math.Cos(pose.ArmAngle) * 10.5;
            if (held2.Swings)
            {
                s.BlitRotated(held2.Sprite, handX, handY, (held2.Sprite.Width / 2.0, held2.Sprite.Height - 2.0),
                    pose.ArmAngle - Math.PI / 2 + 0.45 + pose.ItemTilt);
            }
            else
            {
                s.Blit(held2.Sprite, (int)handX - held2.Sprite.Width / 2 - (int)(pose.ItemUp * 3),
                    (int)handY + held2.Sprite.Height / 2 - (int)(pose.ItemUp * 3));
            }
        }
        // 受击：整个人泛红
        if (pose.Hurt > 0.01)
        {
            for (var i = 0; i < s.Px.Length; i++)
            {
                if (s.Px[i] == 0) continue;
                var c = s.Px[i] & 0xFFFFFFu;
                var r = (double)((c >> 16) & 0xFF);
                var g = (double)((c >> 8) & 0xFF);
                var bl = (double)(c & 0xFF);
                var k = pose.Hurt * 0.6;
                s.Px[i] = 0xFF00_0000u | ((uint)(r + (255 - r) * k) << 16) | ((uint)(g * (1 - k)) << 8) | (uint)(bl * (1 - k));
            }
        }
        if (pose.Flying)
        {
            DrawFlying(b, look, x + pose.Dx, feet, t, facingRight);
            return;
        }
        // 贴到场景里；朝右时以身体中线翻转
        var cx = ax + 5;
        for (var sy = 0; sy < s.Rows; sy++)
            for (var sx = 0; sx < s.Cols; sx++)
            {
                var v = s.Px[sy * s.Cols + sx];
                if (v == 0) continue;
                var tx = facingRight ? 2 * cx - sx : sx;
                b[x + pose.Dx + tx - ax, feet - 60 + sy] = v;
            }
    }
}



