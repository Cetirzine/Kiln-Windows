using Kiln.Core.DotScene;

internal static class SeatedSceneTests
{
    internal static void Run(Action<string, bool> check)
    {
        var empty = new Sprite(8, 12, (_, _) => null);
        const uint trousers = 0xEC218Au;
        var look = new PlayerLook
        {
            HeadFront = empty, HeadFrontBlink = empty, HeadSide = empty,
            BodyFront = new Sprite(8, 12, (_, _) => 0x1572CCu), BodySide = empty,
            NearArm = empty, FarArm = empty, Legs = new Sprite(10, 12, (_, _) => trousers),
        };
        var contacts = true; var aboveSoil = true;
        foreach (var (cols, rows) in new[] { (200, 70), (250, 90), (300, 120) })
            for (var frame = 0; frame < 30; frame++)
            {
                var t = frame * .37; var floor = (int)(rows * .64); var seat = floor - 4; var x = (int)(cols * .62);
                var scene = new DotBuffer(cols, rows);
                new WildernessBound().PaintDynamic(scene, t, new SceneCast { Player = look });
                contacts &= (scene[x, seat - 1] & 0xFFFFFF) == trousers;
                var player = new DotBuffer(cols, rows);
                Player.Draw(player, look, null, new PlayerPose { Sitting = true }, x, seat, t, seatedFloor: floor);
                aboveSoil &= !player.Px.Skip(floor * cols).Any(p => p != 0);
            }
        check("26.3 seated player stays in contact with the cushion through breathing", contacts);
        check("Low-cushion seating keeps the player's feet above the soil", aboveSoil);
    }
}
