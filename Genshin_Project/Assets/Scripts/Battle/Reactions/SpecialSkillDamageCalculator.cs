using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>术语1.6~1.9的直接月曜/星烁技能伤害；不是雷云/月笼多来源加权伤害。</summary>
public static class SpecialSkillDamageCalculator
{
    private sealed class HitFrame
    {
        public long ID;
        public BattleEntity Source;
        public float StellarMultiplier;
    }
    private static readonly AsyncLocal<HitFrame> Current = new AsyncLocal<HitFrame>();
    private sealed class HitScope : IDisposable
    {
        private readonly HitFrame previous;
        private bool disposed;
        public HitScope(HitFrame frame) { previous = Current.Value; Current.Value = frame; }
        public void Dispose() { if (!disposed) { disposed = true; Current.Value = previous; } }
    }
    internal static void Register() => BattleSpecialRules.RegisterHitScope(BeginHit);
    private static IDisposable BeginHit(long id, BattleEntity source, IEnumerable<BattleEntity> targets)
        => new HitScope(new HitFrame { ID = id, Source = source, StellarMultiplier = ReadStellarMultiplier(source) });

    private static float ReadStellarMultiplier(BattleEntity source)
    {
        var team = BattleManager.Instance?.TeamBonuses;
        return team != null && !team.IsDisposed && team.Contains(source) ? team.Stellar.ReactionMultiplier : 1f;
    }
    private static float GetStellarMultiplier(BattleEntity source)
    {
        HitFrame frame = Current.Value;
        return frame != null && frame.ID == BattleEventHub.CurrentHitId && ReferenceEquals(frame.Source, source)
            ? frame.StellarMultiplier : ReadStellarMultiplier(source);
    }

    public static float Calculate(BattleEntity source, BattleEntity target, float skillBaseDamage,
        DamageProperties properties, string element, string effectID, string skillID, string damageType,
        out ReactionDamageComponents components, float preparedBaseDamageBonus = 0f)
    {
        properties.RequireImplementedFormula();
        using var damageQuery = DamageQueryContext.Enter(effectID, damageType, skillID);
        ReactionBuffTotals bonuses = ReactionDamageCalculator.CollectSpecialSkillBonuses(
            source, target, properties, element, effectID, skillID, damageType);
        // 在专属倍率、精通和专属增伤之后与被动固定基础加算相加；预占/消费仍由原批次负责。
        bonuses.BaseDMGBonusFlat += preparedBaseDamageBonus;
        float mastery = StatusFormulaSnapshot.For(source, effectID)?.Attribute("TotalEM", effectID, element, target)
            ?? source.GetFinalEM(effectID, skillID, -1, element, target);
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, element);
        float reactionMultiplier = properties.Reaction switch
        {
            ReactionType.LunarBloom => 1f,
            ReactionType.LunarElectroCharged => 6f,
            ReactionType.LunarCrystallize => 1.6f,
            ReactionType.StellarSuperconduct => GetStellarMultiplier(source),
            _ => throw new System.NotSupportedException("尚未实现的特殊技能伤害公式。")
        };
        components = new ReactionDamageComponents
        {
            SkillBaseDamage = skillBaseDamage, ResistanceMultiplier = resistance,
            DamageBonusMultiplier = reactionMultiplier * (1f + bonuses.BaseDMGBonus)
                * (1f + 6f * Mathf.Max(0f, mastery) / (Mathf.Max(0f, mastery) + 2000f)
                    + (properties.IsLunar ? bonuses.LunarDMGBonus : bonuses.StellarDMGBonus))
                * (1f + bonuses.Elevation),
            Properties = properties
        };
        var memory = StatusFormulaSnapshot.For(source, effectID);
        if (memory != null) LogManager.Log(LogCategory.StatusDamage,
            $"[公式快照] {memory.Identity} 效果={effectID} 目标={target?.EntityID} 位置={target?.Side}:{target?.SlotPosition} 技能基础={skillBaseDamage:R} "
            + $"专属倍率={reactionMultiplier:R} 精通={mastery:R} 基础增伤={bonuses.BaseDMGBonus:R} "
            + $"专属增伤={(properties.IsLunar ? bonuses.LunarDMGBonus : bonuses.StellarDMGBonus):R} "
            + $"基础加算={bonuses.BaseDMGBonusFlat:R} 抗性乘数={resistance:R} Elevation={bonuses.Elevation:R}");
        return CalculateBeforeCritical(skillBaseDamage, reactionMultiplier, mastery, bonuses,
            properties.IsLunar, resistance, bonuses.Elevation);
    }

    // 暴击仍由每hit每目标的原入口处理，DMGTaken仍只由HitBatch处理一次。
    public static float CalculateBeforeCritical(float skillBaseDamage, float reactionMultiplier, float mastery,
        ReactionBuffTotals bonuses, bool lunar, float resistance, float elevation)
    {
        float em = Mathf.Max(0f, mastery);
        float boost = 1f + 6f * em / (em + 2000f)
            + (lunar ? bonuses.LunarDMGBonus : bonuses.StellarDMGBonus);
        return (skillBaseDamage * reactionMultiplier * (1f + bonuses.BaseDMGBonus) * boost
            + bonuses.BaseDMGBonusFlat) * resistance * (1f + elevation);
    }
}
