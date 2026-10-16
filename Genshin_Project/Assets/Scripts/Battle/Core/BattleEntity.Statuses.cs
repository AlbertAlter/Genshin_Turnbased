using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    // ========== 状态操作 ==========
    public bool HasStatus(string statusID2)
    {
        return StatusDict.GetAll(statusID2).Exists(status => status.IsActive);
    }

    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int initialPhaseCount)
    {
        if (StatusDict.TryGetValue(statusID2, out var existing))
        {
            if (existing.MainData != null)
                return AddStatus(statusID2, caster, initialPhaseCount, existing.AddInPhase,
                    existing.TriggerPhase, existing.MainData, existing.WeaponContext);
        }
        if (CountStatusSystem.IsRegistered(statusID2))
            throw new InvalidOperationException("新建 Count 状态必须使用携带主数据和来源上下文的完整 ApplyStatus 入口。");
        var inst = new StatusInstance
        {
            StatusID2 = statusID2,
            StackCount = 1,
            RemainingPhaseCount = initialPhaseCount,
            IsPermanent = initialPhaseCount <= 0,
            Snapshot = null,
            IsActive = false,
            ApplyOrder = ++_applyOrderCounter
        };
        inst.RecordSource(caster);
        StatusDict.Add(inst);
        return inst;
    }

    // ========== 状态接口扩展（状态机 BattleManager 使用） ==========

    /// <summary>
    /// 添加状态（完整版）：支持 AddInPhase / TriggerPhase / Duration / MaxStack / WhenMax 规则。
    ///  - 每次普通施加新建实例，各自保留来源、计时、快照和行动次数，StackCount=1
    ///  - MaxStack&lt;=0：不限同 ID 实例数量
    ///  - MaxStack&gt;0 且已满：WhenMax=="1" 时本次不施加；WhenMax!="1"（默认0）时移除最早施加的该状态再重新施加
    ///  - IsActive 置为 true，表示该状态已完成 OnApply（若有）
    /// </summary>
    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int duration, int addInPhase, int triggerPhase, StatusMainData mainData)
    {
        return AddStatus(statusID2, caster, duration, addInPhase, triggerPhase, mainData, null);
    }

    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int duration, int addInPhase, int triggerPhase, StatusMainData mainData, WeaponRuntimeContext weaponContext, string param2 = null)
    {
        CountStatusSystem.Validate(mainData);
        var application = new CountStatusApplication
        {
            Caster = caster, Duration = duration, AddInPhase = addInPhase, TriggerPhase = triggerPhase,
            MainData = mainData, WeaponContext = weaponContext, Param2 = param2
        };
        StatusInstance currentCount;
        if (CountStatusSystem.IsCount(mainData) && StatusDict.TryGetValue(statusID2, out currentCount))
            return CountStatusSystem.OnReapplied(currentCount, this, application);
        // 敌方 Shield 状态采用“后施加者覆盖前者”，包括同 ID 重放。
        // 统一走 RemoveStatus，确保旧状态的绑定、钩子和元素盾一并清理。
        if (IsEnemyShieldStatus(mainData))
        {
            var oldShieldStatuses = new List<StatusInstance>();
            foreach (StatusInstance status in StatusDict.Values)
            {
                if (status != null && IsEnemyShieldStatus(status.MainData))
                    oldShieldStatuses.Add(status);
            }
            oldShieldStatuses.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
            foreach (StatusInstance status in oldShieldStatuses)
                RemoveStatus(status.StatusID2, -1);
        }

        var existingInstances = StatusDict.GetAll(statusID2);
        int maxStack = mainData != null ? mainData.MaxStack : 0;
        if (maxStack > 0 && existingInstances.Count >= maxStack)
        {
            if (mainData.WhenMax == "1") return existingInstances[0];
            // 满额只替换最早的那一份，不能删掉整个同 ID 分组或刷新其他实例。
            int removeCount = existingInstances.Count - maxStack + 1;
            for (int index = 0; index < removeCount; index++)
                RemoveStatusInstance(existingInstances[index]);
        }

        var inst = new StatusInstance
        {
            StatusID2 = statusID2,
            StackCount = 1,
            RemainingPhaseCount = duration,
            IsPermanent = duration <= 0,
            AddInPhase = addInPhase,
            TriggerPhase = triggerPhase,
            MainData = mainData,
            WeaponContext = weaponContext,
            Snapshot = null,
            IsActive = true,
            ApplyOrder = ++_applyOrderCounter
        };
        inst.RecordSource(caster);
        CountStatusSystem.OnCreated(inst, this, application);
        StatusDict.Add(inst);

        // ========== BindStatus 自动绑定（2026-08-06） ==========
        // 术语表原文："只要该效果来源的状态存在，其所定义的目标或场地位置就一直存在其 Param1 所填写的子状态"
        // 所以状态施加成功后，立即扫描该状态的所有 StatusEffect，对 BindStatus 类型执行绑定。
        StatusBindingSystem.ApplyBindings(inst, this);

        // ========== ChangeControl 按键绑定（2026-08-11） ==========
        // 术语：ChangeControl 修改该角色按键绑定，状态效果表挂载时跟状态 duration 走——
        // 状态施加成功 → 执行（替换按钮/冻结按钮）；状态消失 → 解除（返回原技能/解冻）。
        CharacterCtrl?.OnStatusChangeControlApplied(inst);

        // ========== OnApply：状态施加时立刻生效（2026-08-12） ==========
        // 术语：OnApply = 状态施加时立刻生效的效果，由状态施放者（角色控制器）执行
        if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
            inst.Caster.CharacterCtrl.OnStatusApplied(inst, this);
        if (!HasStatusInstance(inst)) return inst;

        // ========== 状态结算登记（2026-08-12） ==========
        // 以状态为中心：按 AddInPhase/TriggerPhase 登记到 BattleManager 的阶段桶，
        // 阶段结算按登记顺序（=施加顺序=大顺序）执行 OnTrigger/OnItsTurn/OnEnd。
        BattleManager.Instance?.RegisterStatusTick(inst, this, null);

        // ========== PreAlliesDamage 钩子登记（2026-08-14，逻辑见 PreDamageHookSystem.cs） ==========
        // 状态带 OnTrigger+ScriptHook=PreAlliesDamage 时登记，我方主动行为造成伤害前触发。
        PreDamageHookSystem.RegisterPreDamageHook(inst, this);

        // ========== Kill 钩子登记（2026-08-19，逻辑见 KillHookSystem.cs） ==========
        // 状态带 OnTrigger+ScriptHook=Kill(...) 时登记，死亡声明时按 ApplyOrder 触发。
        KillHookSystem.Register(inst, this);

        return inst;
    }

    /// <summary>
    /// 移除指定状态（支持移除指定层数，-1=全部移除）。
    /// 若该状态有 BindStatus 绑定关系，移除时连带移除所有子状态（2026-08-06）。
    /// 返回是否真的发生了移除（false=该状态不存在或未实际移除）。
    /// </summary>
    public bool RemoveStatus(string statusID2, int stackCount = -1)
    {
        if (stackCount == 0) return false;
        bool removed = false;
        foreach (StatusInstance status in StatusDict.GetAll(statusID2))
        {
            if (stackCount == 0) break;
            int layers = Math.Max(1, status.StackCount);
            if (stackCount > 0 && stackCount < layers && !CountStatusSystem.IsCount(status.MainData))
            {
                status.StackCount -= stackCount; // 兼容旧脚本构造的聚合层数，不用于新 ApplyStatus。
                return true;
            }
            removed |= RemoveStatusInstance(status);
            if (stackCount > 0) stackCount = Math.Max(0, stackCount - layers);
        }
        return removed;
    }

    /// <summary>只移除这一份实例；到期、绑定清理、满层替换必须用此入口。</summary>
    public bool RemoveStatusInstance(StatusInstance status)
    {
        if (!StatusDict.Remove(status)) return false;
        status.IsActive = false; // 先失效，已收集的旧实例响应不能作用于其他同 ID 实例。
        StatusBindingSystem.RemoveBindings(status);
        CharacterCtrl?.OnStatusChangeControlRemoved(status);
        BattleManager.Instance?.UnregisterStatusTick(status);
        PreDamageHookSystem.UnregisterPreDamageHook(status);
        KillHookSystem.Unregister(status);
        RemoveShieldOwnedByStatus(status);
        CountStatusSystem.OnRemoved(status, this);
        return true;
    }

    public bool HasStatusInstance(StatusInstance status) => StatusDict.Contains(status);

    public List<StatusInstance> GetStatusInstances(string statusID2) => StatusDict.GetAll(statusID2);

    public int GetStatusStackCount(string statusID2)
    {
        int total = 0;
        foreach (StatusInstance status in GetStatusInstances(statusID2))
            if (status.IsActive) total += Math.Max(1, status.StackCount);
        return total;
    }

    /// <summary>
    /// 查询指定 ID 最早一份的剩余回合数；逐份时间请读 GetStatusInstances。
    /// 未施加返回0，永久性读取 StatusInstance.IsPermanent。
    /// </summary>
    public int GetStatusRemainingPhase(string statusID2)
    {
        return StatusDict.TryGetValue(statusID2, out var inst) ? inst.RemainingPhaseCount : 0;
    }

    /// <summary>
    /// 获取所有状态实例（迭代快照，避免遍历时修改集合报错）。
    /// </summary>
    public List<StatusInstance> GetStatusList()
    {
        var list = new List<StatusInstance>(StatusDict.Values);
        list.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        return list;
    }

    /// <summary>
    /// 获取"生效状态"列表 = 自身身上的状态 + 所在位置上的状态。
    /// 位置上的状态对该位置的单位同样生效（位置状态不随单位死亡消失）。
    /// </summary>
    public List<StatusInstance> GetEffectiveStatusList()
    {
        var list = GetStatusList();
        if (Position != null && Position.StatusList != null)
        {
            foreach (var s in Position.StatusList)
            {
                // 避免重复（同一状态同时挂在实体和位置时去重）
                if (s != null && !list.Contains(s))
                    list.Add(s);
            }
        }
        list.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        return list;
    }

    /// <summary>累加目标身上适用于当前元素的 ResBonus 状态效果。</summary>
    public float GetStatusResBonus(string element)
    {
        DataManager dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.StatusEffectDict == null) return 0f;

        string configuredElement = string.IsNullOrEmpty(element) || element == "None"
            ? "Physical"
            : element;

        float total = 0f;
        var used = new Dictionary<string, int>();
        foreach (StatusInstance instance in GetEffectiveStatusList())
        {
            StatusMainData mainData = instance != null ? instance.MainData : null;
            if (mainData == null || !instance.IsActive) continue;
            if (mainData.GetMultiplierPart() != "ResBonus") continue;
            if (!StatusAppliesTo(
                    mainData,
                    string.Empty,
                    string.Empty,
                    0,
                    configuredElement)) continue;

            int count = StatusAggregation.Take(instance, used);
            if (count == 0) continue;
            foreach (StatusEffectData effect in dataManager.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (BuffExpressionEvaluator.TryResolveScalar(effect.Param1, instance, out float value))
                    total += value * count;
            }
        }
        return total;
    }

    /// <summary>
    /// 累加生效状态中的 ATKBonus（百分比攻击力加成，如跳舞+10%）。
    /// 按状态生效范围过滤：ApplyString（只对该效果生效）/ ApplyDamageType（适用伤害类型）/ ApplyElementType（适用元素）。
    /// </summary>
    public float GetStatusATKBonus(string effectID2, string skillID2, int skillType, string element)
    {
        float total = 0f;
        var used = new Dictionary<string, int>();
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst.MainData == null) continue;
            string part = inst.MainData.GetMultiplierPart();
            if (part != "ATKBonus") continue;
            if (!StatusAppliesTo(inst, effectID2, skillID2, skillType, element, null)) continue;
            int count = StatusAggregation.Take(inst, used);
            if (count == 0) continue;
            foreach (var kv in DataManager.Instance.StatusEffectDict)
            {
                // 效果ID数值前缀匹配：StatusEffectID / 100 == 父状态 StatusID
                var eff = kv.Value;
                if (eff.StatusEffectID == 0) continue;
                if (eff.StatusEffectID / 100 != inst.MainData.StatusID) continue;
                if (eff.EffectType != "Buff" && eff.EffectType != "Debuff") continue;
                if (BuffExpressionEvaluator.TryResolveScalar(eff.Param1, inst, out float v))
                    total += v * count;
            }
        }
        return total;
    }

    /// <summary>
    /// 累加生效状态中的 CritRate（百分比暴击率加成，如安柏天赋1 +10%）。
    /// 按状态生效范围过滤：ApplyString（只对该效果生效）/ ApplyDamageType（适用伤害类型）/ ApplyElementType（适用元素）。
    /// 旧重载（无攻击目标）：目标相关钩子（如 Kaeya_C1）视为不触发，仅供无目标上下文兼容调用。
    /// </summary>
    public float GetStatusCritRate(string effectID2, string skillID2, int skillType, string element)
    {
        return GetStatusCritRate(effectID2, skillID2, skillType, element, null);
    }

    /// <summary>
    /// 累加生效状态中的 CritRate（带攻击目标版，2026-08-19）：Kaeya_C1 等目标相关钩子在聚合时求值。
    /// 每个目标独立判断（不同目标分别决定是否计入）。
    /// </summary>
    public float GetStatusCritRate(string effectID2, string skillID2, int skillType, string element, BattleEntity target)
    {
        float total = 0f;
        var used = new Dictionary<string, int>();
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst.MainData == null) continue;
            string part = inst.MainData.GetMultiplierPart();
            if (part != "CritRate") continue;
            if (!StatusAppliesTo(inst, effectID2, skillID2, skillType, element, target)) continue;
            int count = StatusAggregation.Take(inst, used);
            if (count == 0) continue;
            foreach (var kv in DataManager.Instance.StatusEffectDict)
            {
                var eff = kv.Value;
                if (eff.StatusEffectID == 0) continue;
                if (eff.StatusEffectID / 100 != inst.MainData.StatusID) continue;
                if (eff.EffectType != "Buff" && eff.EffectType != "Debuff") continue;
                if (BuffExpressionEvaluator.TryResolveScalar(eff.Param1, inst, out float v))
                    total += v * count;
            }
        }
        return total;
    }

    /// <summary>累加适用于当前命中目标的状态增伤；武器 Index(n) 在状态实例上下文内求值。</summary>
    public float GetStatusDMGBonus(string effectID2, string skillID2, int skillType, string element, BattleEntity target)
    {
        float total = 0f;
        var dm = DataManager.Instance;
        if (dm == null) return total;
        var used = new Dictionary<string, int>();
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst?.MainData == null || inst.MainData.GetMultiplierPart() != "DMGBonus") continue;
            if (!StatusAppliesTo(inst, effectID2, skillID2, skillType, element, target)) continue;
            int count = StatusAggregation.Take(inst, used);
            if (count == 0) continue;
            foreach (var eff in dm.StatusEffectDict.Values)
            {
                if (eff == null || eff.StatusEffectID / 100 != inst.MainData.StatusID) continue;
                if (eff.EffectType != "Buff" && eff.EffectType != "Debuff") continue;
                if (BuffExpressionEvaluator.TryResolveScalar(eff.Param1, inst, out float value)) total += value * count;
            }
        }
        return total;
    }

    public float GetStatusHealingBonus(string part, string effectID2, string skillID2,
        int skillType, string element, BattleEntity target)
    {
        var dm = DataManager.Instance;
        if (dm == null) return 0f;
        float total = 0f;
        var used = new Dictionary<string, int>();
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst?.MainData == null || !inst.IsActive || inst.MainData.GetMultiplierPart() != part) continue;
            if (!StatusAppliesTo(inst, effectID2, skillID2, skillType, element, target)) continue;
            int count = StatusAggregation.Take(inst, used);
            if (count == 0) continue;
            foreach (var effect in dm.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID / 100 != inst.MainData.StatusID
                    || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")) continue;
                if (!ScriptHookEvaluator.Evaluate(effect.ScriptHook, new ScriptHookContext
                { Caster = inst.Caster, Target = target, Status = inst, HitEffectID = effectID2 })) continue;
                if (BuffExpressionEvaluator.TryResolveScalar(effect.Param1, inst, out float value))
                    total += value * count;
            }
        }
        return total;
    }

    /// <summary>
    /// 状态生效范围判定（术语 L313-L331）：
    /// ApplyString：只对该 Damage/Heal/Shield 效果生效（空=全部）；
    /// ApplyDamageType：适用伤害类型 普攻/重击/战技/爆发（空=全部）；
    /// ApplyElementType：适用元素（空=全部）。
    /// </summary>
    /// <summary>
    /// 状态生效范围判定（带实际状态实例与攻击目标版，2026-08-19，状态钩子任务）：
    /// 在 ApplyString / ApplyDamageType / ApplyElementType 检查之后，追加 StatusMainData.ScriptHook 判定
    /// （如 Kaeya_C1：目标攻击前已有冰附着/冻结才计入暴击率；返回假时该状态的加成不得加入）。
    /// </summary>
    static bool StatusAppliesTo(StatusInstance status, string effectID2, string skillID2, int skillType, string element, BattleEntity target)
    {
        if (status == null || !status.IsActive || status.MainData == null) return false;
        if (!StatusAppliesTo(status.MainData, effectID2, skillID2, skillType, element)) return false;
        if (!string.IsNullOrEmpty(status.MainData.ScriptHook))
        {
            var context = new ScriptHookContext
            {
                Caster = status.Caster,
                Target = target,
                Status = status,
                HitEffectID = effectID2
            };
            if (!ScriptHookEvaluator.Evaluate(status.MainData.ScriptHook, context))
                return false;
        }
        return true;
    }

    static bool StatusAppliesTo(StatusMainData md, string effectID2, string skillID2, int skillType, string element)
    {
        if (!string.IsNullOrEmpty(md.ApplyString))
        {
            if (md.ApplyString.Contains(";"))
            {
                // 技能ID分号列表（如 SK_Normal_Kaeya;SK_Heavy_Kaeya）：当前伤害所属技能 ∈ 列表才生效
                if (string.IsNullOrEmpty(skillID2)) return false;
                bool inList = false;
                foreach (var sid in md.ApplyString.Split(';'))
                {
                    if (sid.Trim() == skillID2) { inList = true; break; }
                }
                if (!inList) return false;
            }
            else
            {
                // 效果ID（如 SE_Burst_Amber1）：当前伤害效果 == 该效果才生效
                if (string.IsNullOrEmpty(effectID2) || effectID2 != md.ApplyString) return false;
            }
        }
        if (!string.IsNullOrEmpty(md.ApplyDamageType))
        {
            string typeName = skillType switch
            {
                0 => "Normal",
                1 => "Heavy",
                2 => "Skill",
                _ => "Burst",
            };
            bool matched = false;
            foreach (string configuredType in md.ApplyDamageType.Split(';'))
            {
                if (string.Equals(configuredType.Trim(), typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }
        if (!string.IsNullOrEmpty(md.ApplyElementType))
        {
            if (element != md.ApplyElementType) return false;
        }
        return true;
    }

    /// <summary>
    /// 获取指定 ID 最早的一份实例（不存在返回 null）；所有实例用 GetStatusInstances。
    /// </summary>
    public StatusInstance GetStatus(string statusID2)
    {
        StatusDict.TryGetValue(statusID2, out var inst);
        return inst;
    }

    private bool IsEnemyShieldStatus(StatusMainData mainData)
    {
        return Type == EntityType.Enemy
            && mainData != null
            && string.Equals(mainData.StatusType, "Shield", StringComparison.OrdinalIgnoreCase);
    }
}
