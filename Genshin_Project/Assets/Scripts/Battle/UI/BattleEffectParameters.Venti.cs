using System.Collections.Generic;

public sealed partial class BattleEffectParameters
{
    private static IEnumerable<BattleEffectParameter> VentiFields(BattleEffectKind kind)
    {
        bool field = kind == BattleEffectKind.VentiUpdraft || kind == BattleEffectKind.VentiWindDomain;
        yield return P("RibbonCount", "风带数量", field ? 3 : 7, 3, 12, "count");
        yield return P("DetailCount", field ? "上升风线数量" : "细风丝数量", field ? (kind == BattleEffectKind.VentiUpdraft ? 40 : 32) : 8, 0, field ? 64 : 24, "count");
        yield return P("ParticleSize", field ? "上升风线宽度" : "细风丝尺寸", field ? .06f : .22f, field ? .008f : .05f, field ? .12f : .5f, "units");
        yield return P("FlowSeconds", field ? "上升气流周期（秒）" : "风丝流动周期（秒）", field ? (kind == BattleEffectKind.VentiUpdraft ? .6f : .8f) : 1.2f, field ? .2f : .4f, 4, "seconds");
        if (field)
        {
            yield return P("SigilOpacity", "地面阵纹透明度", kind == BattleEffectKind.VentiUpdraft ? .8f : .24f, 0, 1);
            yield return P("CurlOpacity", "掀起风带透明度", kind == BattleEffectKind.VentiUpdraft ? .8f : .18f, 0, 1);
            yield return P("LiftLineLengthRatio", "上升风线长度／风场高度", .55f, .15f, .9f);
        }
        yield return P("CameraDistanceUnits", "风场透视相机距离", 24, 12, 80, "units");
        if (kind != BattleEffectKind.VentiUpdraft) yield return P("OrbitSeconds", "风带环绕周期（秒）",
            kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiStormPulse ? 1.2f : 7.2f, .5f, kind == BattleEffectKind.VentiWindDomain ? 12 : 6, "seconds");
        if (kind == BattleEffectKind.VentiStormEye)
        {
            yield return P("IntakeCount", "中心吸气流线数量", 12, 0, 24, "count");
            yield return P("IntakeSeconds", "中心吸气周期（秒）", .8f, .25f, 3, "seconds");
            yield return P("IntakeTurns", "单次吸气旋转圈数", .85f, .25f, 2);
            yield return P("IntakeSize", "中心吸气流线尺寸", .38f, .1f, .8f);
            yield return P("IntakeOpacity", "中心吸气透明度", .55f, 0, 1);
        }
        if (kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiStormPulse)
        {
            yield return P("OrbitSpeedVariation", "环绕变速幅度", .18f, 0, .4f);
            yield return P("BreathAmount", "风眼放缩幅度", .09f, 0, .22f);
            yield return P("EyeWidthRatio", "风眼横向半径／卡宽", .66f, .4f, 1.2f);
            yield return P("EyeHeightRatio", "风眼纵向半径／卡高", .59f, .35f, .95f);
            yield return P("DepthRatio", "风眼景深", .62f, .2f, 1.2f);
        }
        if (kind == BattleEffectKind.VentiStormPulse || kind == BattleEffectKind.VentiUpdraft)
            yield return P("ContactSeconds", "素材接触时刻（秒）", kind == BattleEffectKind.VentiUpdraft ? .12f : .28f, .05f, 1, "seconds");
        if (kind == BattleEffectKind.VentiUpdraft)
        {
            yield return P("UpdraftHeightRatio", "战技风场离地深度／卡高", 1.1f, .5f, 1.8f);
            yield return P("FlipSeconds", "击飞翻面落回（秒）", .72f, .4f, 1.6f, "seconds");
            yield return P("LiftHeightRatio", "卡牌离屏深度／卡高", .34f, .1f, .65f);
        }
        if (kind == BattleEffectKind.VentiWindDomain) yield return P("DomainHeightRatio", "持续风场离地深度／卡高", .9f, .3f, 1.5f);
    }
}
