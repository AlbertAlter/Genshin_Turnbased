using System;
using System.Collections.Generic;

[Serializable]
public sealed class ReactionOccurrence
{
    public BattleDeclaration Declaration;
    public ReactionType Type;
    public string DisplayName;
    public BattleEntity SourceEntity;
    public BattleEntity Target;
    public string SourceEffectID;
    /// <summary>本次实际生成的核心；月绽放获草露按核心去重，不改变核心普通绽放伤害来源。</summary>
    public BloomCoreInstance CreatedBloomCore;
    /// <summary>水雷接触已提交；雷暴云须在本逻辑hit全部目标汇总后整体生成。</summary>
    public PendingLunarConduct PendingLunarConduct;
    public PendingLunarCrystallize PendingLunarCrystallize;
    public PendingStellarSuperconduct PendingStellarSuperconduct;
    /// <summary>本次反应实际参与的元素；扩散、结晶等可变反应必须由 handler 写入真实元素。</summary>
    public List<string> InvolvedElements = new List<string>();
}

[Serializable]
public sealed class ReactionDerivedHit
{
    public EnemyShieldHitResult EnemyShieldHit;
    /// <summary>非0时是独立派生攻击；不同月笼出手不能共用一个hit身份。</summary>
    public long RuntimeHitID;
    [NonSerialized] internal Action<HitResolution> OnResolved;
    public DamageProperties Properties;
    public IReadOnlyList<long> CauseEventIds;
    public BattleEntity Target;
    public float Damage;
    public string DamageElement;
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
    public EquipmentEffectSource EquipmentSource;
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
    public EnemyShieldHitResult EnemyShieldHit;
    public long RuntimeHitID;
    public long EffectExecutionID;
    public int TargetIndex;
    public BattleDeclaration ContactDeclaration;
    public float FinalDamage;
    public float RemainingAttackAmount;
    /// <summary>真正改变主命中伤害的增幅或激化反应；不得用反应列表顺序推断。</summary>
    public ReactionType PrimaryDamageReactionType;
    public readonly List<ReactionOccurrence> TriggeredReactions = new List<ReactionOccurrence>();
    public readonly List<ReactionDerivedHit> DerivedHits = new List<ReactionDerivedHit>();
    public PendingShatterEffect PendingShatter;
    /// <summary>非风命中固有风：在主命中完成后沿公共扩散批次执行，不提前伤害或传播。</summary>
    public SwirlPreparedBatch PendingReverseSwirl;

    public bool HasReaction => TriggeredReactions.Count > 0;

    public string DisplayNameSummary
    {
        get
        {
            var names = new List<string>();
            foreach (ReactionOccurrence occurrence in TriggeredReactions)
            {
                if (occurrence == null || string.IsNullOrWhiteSpace(occurrence.DisplayName)
                    || names.Contains(occurrence.DisplayName))
                    continue;
                names.Add(occurrence.DisplayName);
            }
            return string.Join("、", names);
        }
    }

    public static ReactionResult Unchanged(ReactionContext context)
    {
        return new ReactionResult
        {
            EnemyShieldHit = context?.EnemyShieldHit,
            FinalDamage = context != null ? context.PreReactionDamage : 0f,
            RemainingAttackAmount = context != null ? Math.Max(0f, context.AttackAmount) : 0f
        };
    }
}
