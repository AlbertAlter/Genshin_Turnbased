public static partial class BattleEffectTableWriter
{
    private static BattleEffectAssets ResolveDandelion()
    {
        const string folder = "Characters/1013_Jean/DandelionField/Exports/";
        var assets = new BattleEffectAssets();
        assets.Models.Add(BattleEffectPart.DandelionWind, Model(folder + "Jean_WindRibbon.fbx", 1.07256495f));
        assets.Models.Add(BattleEffectPart.DandelionSeed, Model(folder + "Jean_DandelionSeed.fbx", .277818812f));
        assets.Models.Add(BattleEffectPart.DandelionGlow, Model(folder + "Jean_SoftGlow.fbx", 1.00000004f));
        return assets;
    }
}
