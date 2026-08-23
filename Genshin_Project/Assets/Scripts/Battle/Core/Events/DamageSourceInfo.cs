using System;

/// <summary>
/// 单次伤害来源声明（2026-08-19，状态钩子任务）。
/// 只回答"谁 / 哪个技能 / 哪个效果造成了这次伤害"，不做任何伤害统计。
/// SourceEntityID 与两个字符串ID在创建时立即记录，即使来源实体之后退场仍然可用。
/// </summary>
[Serializable]
public sealed class DamageSourceInfo
{
    /// <summary>来源实体引用（实体退场后不保证有效，判断请用 SourceEntityID）。</summary>
    public BattleEntity SourceEntity;

    /// <summary>来源实体数字ID（创建时立即记录，实体退场后仍可用）。</summary>
    public int SourceEntityID;

    /// <summary>来源阵营。</summary>
    public BattleSide SourceSide;

    /// <summary>来源种类：角色技能 / 状态效果 / 敌人技能 / 派生反应。</summary>
    public ReactionSourceKind SourceKind;

    /// <summary>来源技能ID2（状态伤害等无技能来源时为空）。</summary>
    public string SourceSkillID;

    /// <summary>实际造成该次伤害的效果ID2。</summary>
    public string SourceEffectID;

    /// <summary>该次伤害对应的反应类型（不是反应伤害时为 None）。</summary>
    public ReactionType ReactionType;

    /// <summary>无来源占位：所有旧接口兜底使用，共用同一套扣血逻辑。</summary>
    public static DamageSourceInfo CreateUnknown()
    {
        return new DamageSourceInfo
        {
            SourceEntity = null,
            SourceEntityID = 0,
            SourceSide = BattleSide.Ally,
            SourceKind = ReactionSourceKind.DerivedReaction,
            SourceSkillID = string.Empty,
            SourceEffectID = string.Empty,
            ReactionType = ReactionType.None
        };
    }

    /// <summary>手动构造（角色直伤 / 状态伤害 / 敌人技能伤害等路径统一入口）。</summary>
    public static DamageSourceInfo Create(
        BattleEntity sourceEntity,
        ReactionSourceKind sourceKind,
        string sourceSkillID,
        string sourceEffectID,
        ReactionType reactionType = ReactionType.None)
    {
        return new DamageSourceInfo
        {
            SourceEntity = sourceEntity,
            SourceEntityID = sourceEntity != null ? sourceEntity.EntityID : 0,
            SourceSide = sourceEntity != null ? sourceEntity.Side : BattleSide.Ally,
            SourceKind = sourceKind,
            SourceSkillID = sourceSkillID ?? string.Empty,
            SourceEffectID = sourceEffectID ?? string.Empty,
            ReactionType = reactionType
        };
    }

    /// <summary>从反应上下文构造（主命中 / 立即结算的派生伤害用）。</summary>
    public static DamageSourceInfo FromReactionContext(
        ReactionContext context,
        ReactionType reactionType = ReactionType.None)
    {
        BattleEntity source = context != null ? context.SourceEntity : null;
        return new DamageSourceInfo
        {
            SourceEntity = source,
            SourceEntityID = source != null ? source.EntityID : 0,
            SourceSide = source != null ? source.Side : BattleSide.Ally,
            SourceKind = context != null ? context.SourceKind : ReactionSourceKind.DerivedReaction,
            SourceSkillID = context != null ? context.SourceSkillID : string.Empty,
            SourceEffectID = context != null ? context.SourceEffectID : string.Empty,
            ReactionType = reactionType
        };
    }

    /// <summary>从持续反应来源快照构造（燃烧/感电等跨回合派生伤害用，保留原始归属）。</summary>
    public static DamageSourceInfo FromReactionSnapshot(
        ReactionSourceSnapshot snapshot,
        ReactionType reactionType = ReactionType.None)
    {
        BattleEntity source = snapshot != null ? snapshot.SourceEntity : null;
        return new DamageSourceInfo
        {
            SourceEntity = source,
            SourceEntityID = snapshot != null ? snapshot.SourceEntityID : 0,
            SourceSide = snapshot != null ? snapshot.SourceSide : BattleSide.Ally,
            SourceKind = snapshot != null ? snapshot.SourceKind : ReactionSourceKind.DerivedReaction,
            SourceSkillID = snapshot != null ? snapshot.SourceSkillID : string.Empty,
            SourceEffectID = snapshot != null ? snapshot.SourceEffectID : string.Empty,
            ReactionType = reactionType
        };
    }
}
