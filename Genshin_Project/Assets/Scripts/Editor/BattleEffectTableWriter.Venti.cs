public static partial class BattleEffectTableWriter
{
    private static BattleEffectAssets ResolveVenti(BattleEffectKind kind)
    {
        const string folder = "Characters/1012_Venti/Wind/Exports/";
        var assets = new BattleEffectAssets();
        if (kind == BattleEffectKind.VentiUpdraft || kind == BattleEffectKind.VentiWindDomain)
        {
            assets.Models.Add(BattleEffectPart.VentiSigil, Model(folder + "Venti_GroundSigil.fbx", 1.24523220f));
            assets.Models.Add(BattleEffectPart.VentiUprush, Model(folder + "Venti_UprushRibbon.fbx", 1.35429235f));
            assets.Models.Add(BattleEffectPart.VentiLiftLine, Model(folder + "Venti_LiftLine.fbx", 1.00717426f));
            return assets;
        }
        // Measured on the exact exported local vertices; asset unit calibration is separate from user tuning.
        assets.Models.Add(BattleEffectPart.VentiArc, Model(folder + "Venti_WindArc.fbx", 1.07544267f));
        assets.Models.Add(BattleEffectPart.VentiGust, Model(folder + "Venti_GustRibbon.fbx", 2.23606797f));
        assets.Models.Add(BattleEffectPart.VentiFeather, Model(folder + "Venti_Feather.fbx", .5f));
        return assets;
    }
}
