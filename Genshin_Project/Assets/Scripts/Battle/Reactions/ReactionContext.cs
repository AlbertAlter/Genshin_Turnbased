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

[Serializable]
public sealed class ReactionDamageComponents
{
    public float SkillBaseDamage;
    public float CriticalMultiplier = 1f;
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
    /// <summary>本次命中已满足碎冰条件时，后续反应只读取普通冰附着，不读取冻结虚拟冰。</summary>
    public bool IgnoreFrozenAura;
}
