using System;

public enum ReactionSourceKind
{
    CharacterSkill,
    StatusEffect,
    EnemySkill,
    DerivedReaction
}

public enum ReactionType
{
    None,
    Vaporize,
    Melt,
    Overloaded,
    Frozen,
    Shatter,
    Superconduct,
    ElectroCharged,
    Burning,
    Bloom,
    Hyperbloom,
    Burgeon,
    Quicken,
    Aggravate,
    Spread,
    Crystallize,
    Swirl
}

public enum ReactionPoolKind
{
    Any,
    NormalAura,
    Frozen,
    Burning
}

[Serializable]
public sealed class ReactionDamageComponents
{
    public float SkillBaseDamage;
    public float DamageBonusMultiplier = 1f;
    public float DefenseMultiplier = 1f;
    public float ResistanceMultiplier = 1f;
}

/// <summary>
/// 单个 hit 进入元素反应系统时的完整上下文。
/// 伤害统计不在这里累计；来源信息用于反应状态快照和派生伤害归属。
/// </summary>
[Serializable]
public sealed class ReactionContext
{
    public BattleEntity SourceEntity;
    public BattleEntity Target;
    public ReactionSourceKind SourceKind;
    public string SourceSkillID;
    public string SourceEffectID;
    public long EffectExecutionID;
    /// <summary>本次命中发生的六阶段编号；反应状态以此作为计时原点。</summary>
    public int ApplicationPhase;
    public string AttackElement;
    public float AttackAmount;
    public float PreReactionDamage;
    /// <summary>激化需要在技能倍率伤害上增加基础伤害后重新经过原命中的各乘区。</summary>
    public ReactionDamageComponents DamageComponents;
    public string DamageType;
    public float PoiseDamage;
    public bool CanTriggerReaction = true;
    public bool CanApplyAura = true;
    /// <summary>持续反应派生命中沿用创建时来源快照，不重新读取来源当前属性。</summary>
    public ReactionSourceSnapshot SourceSnapshotOverride;
    public bool SkipImmediateAuraDecay;
    /// <summary>残留清算把场上已有附着作为“攻击侧”时，不先执行同元素新附着覆盖。</summary>
    public bool PreserveIncomingAuraBeforeReaction;
    /// <summary>本次命中已满足碎冰条件时，后续反应只读取普通冰附着，不读取冻结虚拟冰。</summary>
    public bool IgnoreFrozenAura;

    /// <summary>多元素规划阶段用于锁定本次分份只能与哪个元素池反应。</summary>
    public string RestrictedAuraElement;
    public ReactionPoolKind RestrictedAuraKind;

    public bool AllowsReactionPool(string element, ReactionPoolKind kind)
    {
        if (string.IsNullOrEmpty(RestrictedAuraElement)) return true;
        return string.Equals(RestrictedAuraElement, element, StringComparison.Ordinal)
            && (RestrictedAuraKind == ReactionPoolKind.Any || RestrictedAuraKind == kind);
    }
}

public static class ReactionPairRules
{
    public static ReactionType GetReaction(string attack, string aura)
    {
        if ((attack == "Electro" && aura == "Cryo") || (attack == "Cryo" && aura == "Electro")) return ReactionType.Superconduct;
        if ((attack == "Electro" && aura == "Dendro") || (attack == "Dendro" && aura == "Electro")) return ReactionType.Quicken;
        if ((attack == "Pyro" && aura == "Electro") || (attack == "Electro" && aura == "Pyro")) return ReactionType.Overloaded;
        if ((attack == "Hydro" && aura == "Dendro") || (attack == "Dendro" && aura == "Hydro")) return ReactionType.Bloom;
        if ((attack == "Hydro" && aura == "Pyro") || (attack == "Pyro" && aura == "Hydro")) return ReactionType.Vaporize;
        if ((attack == "Pyro" && aura == "Cryo") || (attack == "Cryo" && aura == "Pyro")) return ReactionType.Melt;
        if ((attack == "Hydro" && aura == "Cryo") || (attack == "Cryo" && aura == "Hydro")) return ReactionType.Frozen;
        if ((attack == "Pyro" && aura == "Dendro") || (attack == "Dendro" && aura == "Pyro")) return ReactionType.Burning;
        if ((attack == "Hydro" && aura == "Electro") || (attack == "Electro" && aura == "Hydro")) return ReactionType.ElectroCharged;
        return ReactionType.None;
    }

    public static void GetConsumption(
        ReactionType type,
        string attackElement,
        float attackQuota,
        float auraQuota,
        out float attackConsumed,
        out float auraConsumed)
    {
        float attackPerUnit = 1f;
        float auraPerUnit = 1f;
        if (type == ReactionType.Vaporize)
        {
            attackPerUnit = attackElement == "Pyro" ? 2f : 1f;
            auraPerUnit = attackElement == "Pyro" ? 1f : 2f;
        }
        else if (type == ReactionType.Melt)
        {
            attackPerUnit = attackElement == "Cryo" ? 2f : 1f;
            auraPerUnit = attackElement == "Cryo" ? 1f : 2f;
        }
        else if (type == ReactionType.Bloom)
        {
            attackPerUnit = attackElement == "Hydro" ? 2f : 1f;
            auraPerUnit = attackElement == "Hydro" ? 1f : 2f;
        }

        float units = Math.Min(attackQuota / attackPerUnit, auraQuota / auraPerUnit);
        if (type == ReactionType.ElectroCharged) units = Math.Min(1f, units);
        attackConsumed = Math.Max(0f, units * attackPerUnit);
        auraConsumed = Math.Max(0f, units * auraPerUnit);
    }
}
