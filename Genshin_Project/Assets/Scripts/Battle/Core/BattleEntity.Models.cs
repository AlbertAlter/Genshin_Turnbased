using System;
using System.Collections.Generic;
using UnityEngine;

public class ElementalAura
{
    public string Element;          // Pyro / Hydro / Electro / Cryo / Anemo / Dendro / Geo
    public float AuraAmount;        // 剩余附着量
    public int SourceEntityID;      // 附着来源实体ID（用于扩散感电/结晶追踪）
    public string SourceSkillID;    // 附着来源技能
    public string SourceEffectID;   // 附着来源效果
    public int OriginPhase = -1;    // 施加时的阶段（2026-08-14）：元素量每回合-0.5，按施加阶段定原点
    public bool SkipNextOriginDecay; // 在阶段结算内部刚施加时，跳过紧随其后的同阶段衰减
}

/// <summary>
/// 护盾。
/// </summary>
[Serializable]
public enum ShieldKind
{
    Skill,
    Crystallize,
    EnemyElemental
}

[Serializable]
public class Shield
{
    public float Value;             // 当前盾值
    public string Element;          // 护盾元素类型（影响吸收效率：同元素250%，其他元素150%）
    public float Duration;          // 剩余持续回合
    public float Strength;          // 单盾强度倍率
    public ShieldKind Kind;         // 技能盾 / 结晶盾
    public int OriginPhase = -1;    // 生命周期计时原点；仅结晶盾按该阶段递减
    public string SourceStatusID2;  // 敌方元素盾来源状态；普通护盾为空
    public long SourceStatusApplyOrder;
    [NonSerialized] public StatusInstance SourceStatus;
}

/// <summary>
/// 战斗时状态实例（Status 实时数据）。
/// </summary>
[Serializable]
public class StatusInstance
{
    public string StatusID2;                        // 对应 StatusMainData.StatusID2
    public int StackCount;                          // 当前层数
    public int RemainingPhaseCount;                 // 剩余持续回合数（按 Phase 结算）
    public int AddInPhase;                          // 该状态所在的计时器阶段集合（1-6）
    public int TriggerPhase;                        // 该状态的触发阶段（1-6），配表 SE_Burst_Amber2 等通过 TriggerPhase 定义
    public BattleEntity Caster;                     // 施放者
    public Dictionary<string, float> Snapshot;      // 快照属性（状态生成时读取施法者面板）
    public bool IsActive;                           // 是否已执行过 OnApply
    public StatusMainData MainData;                 // 配表状态主数据（MaxStack/WhenMax 等规则）
    public WeaponRuntimeContext WeaponContext;      // 武器来源上下文；非武器状态为空
    // ========== 行动触发计数（2026-08-14，Status_Action 的 MaxTimePerTurn/MaxTimePerLife/Cooldown） ==========
    public int TurnTriggerCount;                    // 本回合已触发次数（MaxTimePerTurn，回合开始懒重置）
    public int LifeTriggerCount;                    // 全场已触发次数（MaxTimePerLife，不重置）
    public int LastTriggerTurn = -1;                // 上次触发回合（Cooldown 冷却判定）
    public int LastActionTurn = -1;                 // 上次计数回合（懒重置对比用）
    public long ApplyOrder;                         // 施加顺序序号（全局递增，同阶段结算时先施加的先触发）
    public Dictionary<string, StatusActionRuntimeState> ActionRuntime = new Dictionary<string, StatusActionRuntimeState>();
    /// <summary>BindStatus 绑定关系：父状态存在期间，这些子状态挂在目标身上；父状态移除时连带移除（2026-08-06）</summary>
    public List<StatusInstance> BoundStatuses = new List<StatusInstance>();
}

[Serializable]
public sealed class StatusActionRuntimeState
{
    public int TurnTriggerCount;
    public int LifeTriggerCount;
    public int LastTriggerTurn = -1;
    public int LastActionTurn = -1;
}
