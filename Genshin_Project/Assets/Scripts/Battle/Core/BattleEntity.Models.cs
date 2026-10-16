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
    public int StackCount;                          // 普通状态每份实例固定1；兼容旧脚本数据，Count点数不放这里
    public int RemainingPhaseCount;                 // 剩余持续回合数（按 Phase 结算）
    public bool IsPermanent;                        // ApplyStatus 的 Duration 留空或为0时为 true，不自动扣减/到期
    public int AddInPhase;                          // 该状态所在的计时器阶段集合（1-6）
    public int TriggerPhase;                        // 该状态的触发阶段（1-6），配表 SE_Burst_Amber2 等通过 TriggerPhase 定义
    public BattleEntity Caster;                     // 兼容字段：状态来源实体；执行状态效果时仍由其控制器承载
    public int SourceEntityID;                      // 状态来源实体ID；来源退场后仍可查询归属
    public BattleSide SourceSide;                   // 状态来源阵营
    public bool HasSourceEntity;                    // 全局反应状态等无角色来源时为 false
    public Dictionary<string, float> Snapshot;      // 快照属性（状态生成时读取施法者面板）
    /// <summary>
    /// 钩子可读写的实例级元素记录；空字符串表示尚未记录，不等同于物理元素 None。
    /// 使用 Pyro/Hydro/Electro/Cryo/Anemo/Dendro/Geo 等既有元素标识。
    /// 同一实例叠层/刷新时保留，新建实例为空；不自动继承给绑定子状态，也不自动改变伤害元素。
    /// 何时写入、锁定或清除由使用该字段的钩子决定，不修改共享配表。
    /// </summary>
    public string RuntimeElement = string.Empty;
    /// <summary>
    /// 特殊代码管理的状态运行时数值，不是层数或配表常量。
    /// 生命之契保存剩余绝对值；Count 由各自的专用处理器管理，可存小数且不自动归零移除。
    /// </summary>
    [UnityEngine.Serialization.FormerlySerializedAs("RuntimeValue")]
    [SerializeField] private float _runtimeValue;
    [SerializeField] private int _runtimeIntValue;
    [SerializeField] private bool _hasIntegerRuntimeValue;

    /// <summary>原有浮点Count仍原样读写；整数Count只提供浮点只读视图供Buff公式使用。</summary>
    public float RuntimeValue
    {
        get { return _hasIntegerRuntimeValue ? _runtimeIntValue : _runtimeValue; }
        set
        {
            if (_hasIntegerRuntimeValue)
                throw new InvalidOperationException("整数Count必须通过整数入口修改，不能写入float。");
            _runtimeValue = value;
        }
    }

    public bool HasIntegerRuntimeValue { get { return _hasIntegerRuntimeValue; } }

    /// <summary>整数余额的唯一存储；不经float中转，保证超过2^24后仍能逐点增减。</summary>
    public int RuntimeIntValue
    {
        get
        {
            if (!_hasIntegerRuntimeValue)
                throw new InvalidOperationException("此状态没有整数Count数值。");
            return _runtimeIntValue;
        }
        internal set
        {
            _hasIntegerRuntimeValue = true;
            _runtimeIntValue = value;
            _runtimeValue = 0f;
        }
    }
    /// <summary>专用 Count 自己拥有的附加记录；公共系统不解释这些规则。</summary>
    public object CountRuntimeState;
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

    /// <summary>记录状态来源。角色、敌人、武器和圣遗物状态均记录所属实体；全局反应状态不调用本方法。</summary>
    public void RecordSource(BattleEntity source)
    {
        Caster = source;
        HasSourceEntity = source != null;
        SourceEntityID = source != null ? source.EntityID : 0;
        SourceSide = source != null ? source.Side : default;
    }

    public bool IsOwnedBy(BattleSide side)
    {
        return HasSourceEntity && SourceSide == side;
    }
}

[Serializable]
public sealed class StatusActionRuntimeState
{
    [NonSerialized] public int PendingTriggerCount; // Accepted but not executed/cancelled
    public int TurnTriggerCount;
    public int LifeTriggerCount;
    public int LastTriggerTurn = -1;
    public int LastActionTurn = -1;
}
