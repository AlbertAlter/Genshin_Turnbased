using System;

/// <summary>
/// 单次伤害结算结果（2026-08-19，状态钩子任务）。
///  - ActualHPDamage = HPBefore - HPAfter（不小于0）
///  - CausedDeath = HPBefore > 0 且 HPAfter &lt;= 0；同一已经死亡的目标再次受击不会再次产生死亡声明
///  - HitLanded 表示有效命中了目标，不要求实际扣血（护盾全吸收时也可为 true）
/// </summary>
[Serializable]
public sealed class DamageResolvedEvent
{
    /// <summary>所属 HitBatch 的运行时序号；直接调用 TakeDamage 时为0。</summary>
    public long HitBatchID;
    public long RuntimeHitID;
    public BattleDeclaration Declaration;
    public float DamageBeforeShield;
    public CriticalHitResult? Critical;
    public float ShieldAbsorbed;
    /// <summary>本次对应元素的伤害被Immune效果免疫；不同于未命中、护盾吸收或普通零伤害。</summary>
    public bool IsImmune;
    /// <summary>免疫阻止的伤害，不计护盾吸收或实际扣血；DamageBeforeShield保留免疫前计算值。</summary>
    public float ImmuneDamage;
    public string Element;
    public string DamageType;
    public float ElementAmount;
    public bool IsNightSoulDamage;
    public DamageProperties Properties;
    public bool IsPneumaDamage => Properties.IsPneuma;
    public bool IsOusiaDamage => Properties.IsOusia;

    /// <summary>效果内第几个 hit（从1开始）；未知时为0。</summary>
    public int HitIndex;

    /// <summary>同一 hit 内的目标结算序号（从1开始）；未知时为0。</summary>
    public int TargetIndex;

    /// <summary>受击目标。</summary>
    public BattleEntity Target;

    /// <summary>伤害来源声明（谁/哪个技能/哪个效果）。</summary>
    public DamageSourceInfo Source;

    /// <summary>本次正式受击提交前的来源编队/前台事实；未知不能按当前IsActive倒推。</summary>
    public bool SourceWasPartyMember;
    public bool SourceWasOnField;

    /// <summary>受击前生命值。</summary>
    public float HPBefore;

    /// <summary>受击后生命值。</summary>
    public float HPAfter;

    /// <summary>请求扣除的伤害量（可能被护盾/上限截断）。</summary>
    public float RequestedHPDamage;

    /// <summary>实际扣血量 = HPBefore - HPAfter。</summary>
    public float ActualHPDamage;

    /// <summary>是否有效命中（不要求实际扣血）。</summary>
    public bool HitLanded;

    /// <summary>本次伤害是否导致目标从存活变为死亡。</summary>
    public bool CausedDeath;

    public DamageResolvedEvent Snapshot()
    {
        var copy = (DamageResolvedEvent)MemberwiseClone();
        copy.Source = Source?.Snapshot();
        return copy;
    }
}
