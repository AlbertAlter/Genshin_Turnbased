public sealed partial class BattleEffectParameters
{
    private static BattleEffectParameter[] DandelionFields() => new[]
    {
        P("SeedSize", "蒲公英大小", 1.5f, .25f, 4),
        P("SeedGlow", "花冠元素微光", .22f, 0, 1),
        P("GlowSize", "花冠微光范围", .12f, .03f, .5f),
        P("SeedCount", "蒲公英数量", 10, 0, 16, "count"),
        P("SeedTimingVariation", "蒲公英出生与寿命错落", .8f, 0, 1),
        P("SeedSpeedVariation", "蒲公英平滑变速幅度", .45f, 0, .75f),
        P("SeedDrift", "蒲公英随风飘摆", .12f, 0, .35f, "units"),
        P("WindCount", "风带数量", 6, 0, 12, "count"),
        P("WindOpacity", "风带透明度", .55f, 0, 1),
        P("WindTimingVariation", "风带出现与寿命错落", .75f, 0, 1),
        P("WindSpeedVariation", "风带平滑变速幅度", .4f, 0, .75f),
        P("WindWrapStrength", "风的环绕层次", .65f, 0, 1),
        P("WindCycleSeconds", "风带基准上升周期（秒）", 7.2f, .5f, 15, "seconds"),
        P("WindOrbitTurns", "风带每次上升圈数", 4, 1, 8, "count"),
        P("OrbitWidth", "横向环绕半径", 1.26f, .3f, 3, "units"),
        P("DepthRadius", "前后环绕半径", .52f, .1f, 2, "units"),
        P("RiseSpan", "从下到上总距离", 3.6f, 1, 7, "units"),
        P("SpiralTurns", "蒲公英每次上升圈数", 3, 1, 8, "count"),
        P("CycleSeconds", "蒲公英基准上升周期（秒）", 4.8f, .5f, 12, "seconds"),
        P("CameraDistanceUnits", "透视观察距离", 24, 12, 80, "units")
    };
}
