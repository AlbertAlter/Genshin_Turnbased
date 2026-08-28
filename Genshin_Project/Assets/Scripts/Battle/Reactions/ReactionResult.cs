using System;
using System.Collections.Generic;

[Serializable]
public sealed class ReactionOccurrence
{
    public ReactionType Type;
    public string DisplayName;
    public BattleEntity SourceEntity;
    public BattleEntity Target;
    public string SourceEffectID;
    /// <summary>本次反应实际参与的元素；扩散、结晶等可变反应必须由 handler 写入真实元素。</summary>
    public List<string> InvolvedElements = new List<string>();
}

[Serializable]
public sealed class ReactionDerivedHit
{
    public BattleEntity Target;
    public float Damage;
    public string DamageElement;
    public float PoiseDamage;
    public float ElementAmount;
    /// <summary>派生伤害来源声明（2026-08-19）：保留原始角色/技能/效果归属，供 Kill 等钩子识别。</summary>
    public DamageSourceInfo Source;
    /// <summary>该派生伤害对应的反应类型（非反应为 None）。</summary>
    public ReactionType ReactionType;
}

[Serializable]
public sealed class PendingShatterEffect
{
    public BattleEntity Source;
    public BattleEntity Target;
    public string SourceEffectID;
    public ReactionSourceKind SourceKind;
    public string SourceSkillID;
    public float Damage;
}

/// <summary>单个 hit 的反应结算结果。</summary>
[Serializable]
public sealed class ReactionResult
{
    public float FinalDamage;
    public float RemainingAttackAmount;
    public readonly List<ReactionOccurrence> TriggeredReactions = new List<ReactionOccurrence>();
    public readonly List<ReactionDerivedHit> DerivedHits = new List<ReactionDerivedHit>();
    public PendingShatterEffect PendingShatter;

    public bool HasReaction => TriggeredReactions.Count > 0;

    public static ReactionResult Unchanged(ReactionContext context)
    {
        return new ReactionResult
        {
            FinalDamage = context != null ? context.PreReactionDamage : 0f,
            RemainingAttackAmount = context != null ? Math.Max(0f, context.AttackAmount) : 0f
        };
    }
}
