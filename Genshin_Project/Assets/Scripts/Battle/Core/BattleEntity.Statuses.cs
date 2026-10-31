using System;
using System.Collections.Generic;
using System.Linq;
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
        if (!PublicControlStates.CanApply(this, statusID2, caster) || EnemyTraits.IsStatusImmune(this, statusID2)) return null;
        if (statusID2 == TeamPublicStatuses.MoonCage)
            throw new InvalidOperationException("月笼属于敌方格子，须由月结晶公共处理器创建。");
        if (StatusDict.TryGetValue(statusID2, out var existing))
        {
            if (existing.MainData != null)
                return AddStatus(statusID2, caster, initialPhaseCount, existing.AddInPhase,
                    existing.TriggerPhase, existing.MainData, existing.WeaponContext,
                    EnchantStatusSystem.IsEnchant(existing.MainData) ? existing.Enchant.ElementAmount.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    existing.EquipmentSource, existing.Enchant.Element, existing.Enchant.CanBeOverridden ? "0" : "1");
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
        inst.Host = this;
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

    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int duration, int addInPhase, int triggerPhase, StatusMainData mainData, WeaponRuntimeContext weaponContext, string param2 = null,
        EquipmentEffectSource equipmentSource = null, string enchantElement = null, string enchantOverride = null)
    {
        if (statusID2 == TeamPublicStatuses.MoonCage)
            throw new InvalidOperationException("月笼属于敌方格子，须由月结晶公共处理器创建。");
        using var declarationResolution = BattleEventHub.BeginResolution();
        using var declarations = BattleResolutionContext.BeginCollection();
        if (!PublicControlStates.CanApply(this, statusID2, caster) || EnemyTraits.IsStatusImmune(this, statusID2, mainData)) return null;
        // 先拒绝非法敌人盾配置，不能先覆盖旧盾后才发现新公式无效。
        if (Type == EntityType.Enemy) EnemyShieldEffectSystem.ValidateStatusDefinition(mainData);
        using var intrinsicElements = ElementQuery.IsIntrinsicStatus(statusID2)
            ? ElementEventScope.Begin("IntrinsicStatusApplied", this) : null;
        equipmentSource = EquipmentStatusLifetime.Resolve(statusID2, caster, weaponContext, equipmentSource);
        if (equipmentSource != null && !equipmentSource.IsAvailable) return null;
        // 配表留空解析为0；按术语取实际施加阶段，不能把状态登记到不会推进的阶段0。
        if (addInPhase == 0 && BattleManager.Instance != null)
            addInPhase = (int)BattleManager.Instance.CurrentPhase;
        OverallShieldSystem.ValidateHost(mainData, this, caster);
        CountStatusSystem.Validate(mainData);
        var enchant = EnchantStatusSystem.Parse(mainData, enchantElement, param2, enchantOverride);
        var application = new CountStatusApplication
        {
            Caster = caster, Duration = duration, AddInPhase = addInPhase, TriggerPhase = triggerPhase,
            MainData = mainData, WeaponContext = weaponContext, Param2 = param2, EquipmentSource = equipmentSource
        };
        StatusInstance currentCount;
        if (CountStatusSystem.IsCount(mainData) && !CountStatusSystem.UsesStandardApplication(mainData)
            && StatusDict.TryGetValue(statusID2, out currentCount))
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
        // 已明确要求同源刷新、异源共存的武器标记；不改变普通状态的全局MaxStack规则。
        if (statusID2 == "ST_Weapon_4304203" || statusID2 == "ST_Weapon_4304103" || statusID2 == "ST_Weapon_4304104")
        {
            int expectedWeapon = statusID2 == "ST_Weapon_4304203" ? 3042 : 3041;
            if (weaponContext?.WeaponID != expectedWeapon || weaponContext.Equipper != caster)
                throw new InvalidOperationException("同源刷新状态必须保留原武器装备者与来源身份。");
            existingInstances.RemoveAll(status => status.Caster != caster
                || status.WeaponContext?.SourceInstanceID != weaponContext.SourceInstanceID);
        }
        int maxStack = OverallShieldSystem.IsOverall(mainData) ? 1 : mainData != null ? mainData.MaxStack : 0;
        if (maxStack > 0 && existingInstances.Count >= maxStack)
        {
            if (!OverallShieldSystem.IsOverall(mainData) && mainData.WhenMax == "1") return existingInstances[0];
            // 满额只替换最早的那一份，不能删掉整个同 ID 分组或刷新其他实例。
            int removeCount = existingInstances.Count - maxStack + 1;
            for (int index = 0; index < removeCount; index++)
                RemoveStatusInstance(existingInstances[index], "Replaced");
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
            EquipmentSource = equipmentSource,
            Enchant = enchant,
            Snapshot = null,
            IsActive = true,
            ApplyOrder = ++_applyOrderCounter
        };
        inst.RecordSource(caster);
        inst.Host = this;
        CountStatusSystem.OnCreated(inst, this, application);
        BattleSpecialRules.OnStatusCreated(inst, this);
        StatusDict.Add(inst);
        if (ElementQuery.IsIntrinsicStatus(statusID2))
            foreach (string element in ElementQuery.Elements)
                if (statusID2 == ElementQuery.IntrinsicStatusID(element)) RemoveAura(element);
        var appliedFact = BattleEventHub.Publish(new StatusEventData(inst, this, true, "Applied"));
        intrinsicElements?.AddCause(appliedFact.Header.EventId, this);
        using var appliedDeclaration = BattleEventHub.Enter(appliedFact);
        EnemyShieldEffectSystem.ApplyStatus(this, inst);

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
        else if (inst.Caster?.EnemyCtrl != null)
            inst.Caster.EnemyCtrl.OnStatusApplied(inst, this);
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
        StatusHostAccess.OnExtraBaseHPChanged(inst, this);

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
    public bool RemoveStatusInstance(StatusInstance status, string reason = "Removed")
    {
        using var declarationResolution = BattleEventHub.BeginResolution();
        using var declarations = BattleResolutionContext.BeginCollection();
        using var intrinsicElements = ElementQuery.IsIntrinsicStatus(status?.StatusID2)
            ? ElementEventScope.Begin("IntrinsicStatusRemoved", this) : null;
        if (!StatusDict.Remove(status)) return false;
        status.IsActive = false; // 先失效，已收集的旧实例响应不能作用于其他同 ID 实例。
        BattleSpecialRules.OnStatusRemoved(status, this);
        var removedFact = BattleEventHub.Publish(new StatusEventData(status, this, false, reason));
        intrinsicElements?.AddCause(removedFact.Header.EventId, this);
        using var removedDeclaration = BattleEventHub.Enter(removedFact);
        StatusRemovalActions.OnRemoved(status, this, reason);
        StatusBindingSystem.RemoveBindings(status);
        CharacterCtrl?.OnStatusChangeControlRemoved(status);
        BattleManager.Instance?.UnregisterStatusTick(status);
        PreDamageHookSystem.UnregisterPreDamageHook(status);
        KillHookSystem.Unregister(status);
        RemoveShieldOwnedByStatus(status);
        CountStatusSystem.OnRemoved(status, this);
        StatusHostAccess.OnExtraBaseHPChanged(status, this);
        if (status.StatusID2 == PublicControlStates.OffField)
            BattleManager.Instance?.OnAllyReturnedToField(this);
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
    /// 获取"生效状态"列表 = 自身状态 + 所在位置状态 + 同阵营队伍宿主的被动Buff/Debuff/Immune/Enchant。
    /// 不复制队伍状态进个人容器；个人HasStatus/Count和状态栏仍只读真实个人挂载。
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
        var team = BattleManager.Instance?.GetTeamFor(this);
        if (team != null)
            foreach (var status in team.GetStatusList())
                if (StatusHostAccess.IsTeamBuff(status) && !list.Contains(status)) list.Add(status);
        list.RemoveAll(status => !EquipmentStatusLifetime.CanExecute(status));
        list.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        return list;
    }

    /// <summary>累加目标身上适用于当前元素的 ResBonus 状态效果。</summary>
    public float GetStatusResBonus(string element)
    {
        string configuredElement = element;
        return GetTeamResistanceBonus(element)
            + GetStatusAdditiveAttributeBonus("ResBonus", "", "", -1, configuredElement, this);
    }

    /// <summary>固定攻击力位于攻击百分比之后，不放大白值，也不再次乘攻击百分比。</summary>
    public float GetStatusATKBonusFlat(string effectID2, string skillID2, int skillType, string element, BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("ATKBonusFlat", effectID2, skillID2, skillType, element, target);

    /// <summary>普通直伤的基础伤害加数；只读适用Buff，不再次收集CountDamageContribution。</summary>
    public float GetStatusBaseDMGBonusFlat(string effectID, string skillID, int skillType, string element, BattleEntity target)
        => GetStatusAdditiveAttributeBonus("BaseDMGBonusFlat", effectID, skillID, skillType, element, target);

    public float GetFinalATK(string effectID2, string skillID2, int skillType, string element)
        => GetTotalBaseATK(effectID2, skillID2, skillType, element) * (1f + CharacterATKBonus + WeaponATKBonus
            + GetStatusATKBonus(effectID2, skillID2, skillType, element))
            + GetStatusATKBonusFlat(effectID2, skillID2, skillType, element);

    /// <summary>白值：角色成长/突破+武器白值+ExtraBaseATK；不含任何攻击百分比或ATKBonusFlat。</summary>
    public float GetTotalBaseATK(string effectID = "", string skillID = "", int skillType = -1, string element = "")
        => TotalATK + WeaponATK + GetStatusAdditiveAttributeBonus("ExtraBaseATK", effectID, skillID, skillType, element, null);

    public float GetStatusExtraBaseHP(string effectID = "", string skillID = "", int skillType = -1,
        string element = "", BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("ExtraBaseHP", effectID, skillID, skillType, element, target);

    public float GetStatusExtraBaseDEF(string effectID = "", string skillID = "", int skillType = -1,
        string element = "", BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("ExtraBaseDEF", effectID, skillID, skillType, element, target);

    /// <summary>按实际伤害/技能范围累加百分比攻击力。</summary>
    public float GetStatusATKBonus(string effectID2, string skillID2, int skillType, string element)
        => (BattleManager.Instance?.TeamBonuses?.Resonance.ATKBonus(this) ?? 0f)
            + GetStatusAdditiveAttributeBonus("ATKBonus", effectID2, skillID2, skillType, element, null);

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
        => GetStatusCritRate(effectID2, skillID2, skillType, element, target, null);

    public float GetStatusCritRate(string effectID2, string skillID2, int skillType, string element, BattleEntity target, ReactionType? damageReaction)
        => (BattleManager.Instance?.TeamBonuses?.Resonance.CritBonus(this, target) ?? 0f)
            + GetStatusAdditiveAttributeBonus("CritRate", effectID2, skillID2, skillType, element, target, damageReaction: damageReaction);

    /// <summary>本次实际伤害的状态暴伤，限定反应仅在明确传入对应身份时汇总。</summary>
    public float GetStatusCritDMG(string effectID, string skillID, int skillType, string element, BattleEntity target,
        ReactionType? damageReaction = null)
        => GetStatusAdditiveAttributeBonus("CritDMG", effectID, skillID, skillType, element, target, damageReaction: damageReaction);

    /// <summary>
    /// 累加当前适用的状态防御百分比；Count表达式只读来源角色数值，不修改面板或计数。
    /// 与其他状态属性查询统一放在本分片，避免调用方依赖尚未导入的新分片。
    /// </summary>
    public float GetStatusDEFBonus(string effectID = "", string skillID = "", int skillType = 0,
        string element = "", BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("DEFBonus", effectID, skillID, skillType, element, target);

    /// <summary>固定防御位于百分比乘区之外；仍按状态来源、条件、范围和MaxCount只读汇总。</summary>
    public float GetStatusDEFBonusFlat(string effectID = "", string skillID = "", int skillType = 0,
        string element = "", BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("DEFBonusFlat", effectID, skillID, skillType, element, target);

    /// <summary>保留已有面板与武器加成；基础防御乘状态百分比后再加固定值，不回写TotalDEF。</summary>
    public float GetFinalDEF(string effectID = "", string skillID = "", int skillType = 0,
        string element = "", BattleEntity target = null)
    {
        float basis = CharacterCtrl != null ? CharacterCtrl.GetDEFBeforePercentBonuses() : TotalDEF;
        if (basis <= 0f) basis = TotalDEF;
        float percent = GetStatusDEFBonus(effectID, skillID, skillType, element, target);
        float extra = GetStatusExtraBaseDEF(effectID, skillID, skillType, element, target);
        return Mathf.Max(0f, TotalDEF + basis * percent
            + extra * (1f + CharacterDEFBonus + WeaponDEFBonus + percent)
            + GetStatusDEFBonusFlat(effectID, skillID, skillType, element, target));
    }

    /// <summary>只读状态精通加成；TotalEM保留基础、武器及已有队伍常驻值，不反复回写。</summary>
    public float GetStatusEMBonus(string effectID = "", string skillID = "", int skillType = -1,
        string element = "", BattleEntity target = null)
        => GetStatusAdditiveAttributeBonus("EMBonus", effectID, skillID, skillType, element, target);

    public float GetFinalEM(string effectID = "", string skillID = "", int skillType = -1,
        string element = "", BattleEntity target = null)
        => Mathf.Max(0f, TotalEM + GetStatusEMBonus(effectID, skillID, skillType, element, target));

    public float GetStatusRechargeBonus()
        => GetStatusAdditiveAttributeBonus("RechargeBonus", "", "", -1, "", null);

    /// <summary>缺少基础充能配置时沿用原1.0兜底；状态百分比加算，Flat回能不调用本入口。</summary>
    public float GetFinalRechargeRate()
        => Mathf.Max(0f, (TotalRechargeRate > 0f ? TotalRechargeRate : 1f) + GetStatusRechargeBonus());

    /// <summary>只汇总调用者明确提供的开战固定生命状态；不在属性查询中修改生命或发布声明。</summary>
    internal float GetInitialArtifactHPBonusFlat(IEnumerable<StatusInstance> statuses)
        => GetStatusAdditiveAttributeBonus("HPBonusFlat", "", "", -1, "", null, statuses);

    internal float GetInitialArtifactHPBonus(IEnumerable<StatusInstance> statuses)
        => GetStatusAdditiveAttributeBonus("HPBonus", "", "", -1, "", null, statuses);

    private IEnumerable<StatusInstance> GetUnbakedHPStatuses()
    {
        foreach (var status in GetEffectiveStatusList())
            if (status != null && !status.HPBonusIncludedInPanel && EquipmentStatusLifetime.CanExecute(status)) yield return status;
    }

    /// <summary>纯查询：生命百分比与已有百分比同乘区，固定生命最后相加；不额外改变当前HP或生成治疗。</summary>
    public float GetFinalHP(string effectID = "", string skillID = "", int skillType = -1,
        string element = "", BattleEntity target = null)
    {
        float percent = GetStatusAdditiveAttributeBonus("HPBonus", effectID, skillID, skillType, element, target, GetUnbakedHPStatuses());
        float flat = GetStatusAdditiveAttributeBonus("HPBonusFlat", effectID, skillID, skillType, element, target, GetUnbakedHPStatuses());
        float basis = CharacterCtrl != null ? CharacterCtrl.GetHPBeforePercentBonuses() : TotalHP;
        float extra = GetStatusExtraBaseHP(effectID, skillID, skillType, element, target);
        // 已烘入TotalHP的百分比不重复求值；ExtraBaseHP仍须吃到它们，固定生命始终留在乘区外。
        float result = TotalHP + basis * percent + flat + extra * (1f + BakedHPBonus + percent);
        if (float.IsNaN(result) || float.IsInfinity(result)) throw new InvalidOperationException("最终生命上限必须为有限值。");
        return Mathf.Max(0f, result);
    }

    /// <summary>真实生命上限降低后的提交入口；不在查询中钳制，不伪造伤害或治疗。</summary>
    internal void ClampCurrentHPToMaximum()
    {
        float before = CurrentHP;
        CurrentHP = Mathf.Min(CurrentHP, GetFinalHP());
        var battle = BattleManager.Instance;
        if (before != CurrentHP && battle != null && battle.IsBattleRunning && !battle.IsBattleOver)
            BattleEventHub.Publish(new HPChangedEventData(this, before, CurrentHP, "MaxHPAdjustment"));
    }

    /// <summary>造盾来源的百分比/固定盾量加成；沿用范围、钩子、Count及MaxCount过滤，查询不修改旧盾。</summary>
    public float GetStatusShieldBonus(string part, string effectID, string skillID, string element, BattleEntity recipient,
        ReactionType? reaction = null)
    {
        if (part != "ShieldBonus" && part != "ShieldBonusFlat")
            throw new ArgumentException("非护盾量加成分类：" + part, nameof(part));
        return GetStatusAdditiveAttributeBonus(part, effectID, skillID, -1, element, recipient, damageReaction: reaction);
    }

    /// <summary>原始盾量生成公式；固定加数不吃百分比，允许减盾，最终最低为0。不处理敌方元素量盾。</summary>
    public static float CalculateShieldAmount(float basis, float bonus, float flat)
    {
        if (float.IsNaN(basis) || float.IsInfinity(basis) || float.IsNaN(bonus) || float.IsInfinity(bonus)
            || float.IsNaN(flat) || float.IsInfinity(flat))
            throw new InvalidOperationException("护盾量及其加成必须为有限数值。");
        float amount = basis * (1f + bonus) + flat;
        if (float.IsNaN(amount) || float.IsInfinity(amount))
            throw new InvalidOperationException("最终护盾量必须为有限数值。");
        return Mathf.Max(0f, amount);
    }

    /// <summary>受盾者自身的状态护盾强效，来源与Count仍按原Buff实例只读汇总。</summary>
    public float GetStatusShieldStrengthBonus(string damageElement = "")
        => GetStatusAdditiveAttributeBonus("ShieldStrength", "", "", -1, damageElement, this);

    /// <summary>基础1 + 原字段 + 队伍 + 状态加算；不改写任何盾的原始Value。</summary>
    public float GetShieldStrengthMultiplier(string damageElement = "")
    {
        float value = 1f + ShieldStrength + GetTeamShieldStrength() + GetStatusShieldStrengthBonus(damageElement);
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException("护盾强效必须为有限数值。");
        return Mathf.Max(0f, value);
    }

    internal float GetLiveTargetStatusBonus(string part, string effectID, string element, BattleEntity target,
        string damageType, ReactionType? reaction)
    {
        using var query = DamageQueryContext.Enter(effectID, damageType);
        return GetStatusAdditiveAttributeBonus(part, effectID, "", -1, element, target,
            GetEffectiveStatusList().Where(StatusSnapshotConditions.IsLiveTargetStatus), reaction);
    }

    private float GetStatusAdditiveAttributeBonus(string part, string effectID, string skillID, int skillType,
        string element, BattleEntity target, IEnumerable<StatusInstance> suppliedStatuses = null, ReactionType? damageReaction = null)
    {
        var dm = DataManager.Instance;
        if (dm == null) return 0f;
        float total = 0f;
        var used = new Dictionary<string, int>();
        foreach (var status in suppliedStatuses ?? GetEffectiveStatusList())
        {
            var main = status?.MainData;
            if (main == null || !status.IsActive || main.GetMultiplierPart() != part) continue;
            if (!StatusAppliesTo(status, effectID, skillID, skillType, element, target, damageReaction)) continue;
            int count = StatusAggregation.Take(status, used);
            if (count == 0) continue;
            bool collected = false;
            foreach (var effect in dm.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID / 100 != main.StatusID
                    || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")) continue;
                if (part == "BaseDMGBonusFlat" && BattleSpecialRules.IsPreparedBaseDamageEffect(status, effect)) continue;
                if (!ScriptHookEvaluator.Evaluate(effect.ScriptHook, new ScriptHookContext
                { Caster = status.Caster, Status = status, Target = target, Recipient = this,
                    StatusHost = StatusHostAccess.FindHost(status, this), HitEffectID = effectID, DamageElement = element,
                    DamageType = DamageQueryContext.ResolveType(effectID),
                    DamageReaction = damageReaction,
                    DamageSkillID = DamageQueryContext.ResolveSkillID(effectID, skillID) })) continue;
                if (!HexereiStatusEnhancement.TryResolveScalar(effect, status, out float value)) continue;
                collected = true;
                total += value * count;
            }
            if (!collected && main.MaxCount > 0)
            {
                string id = status.StatusID2 ?? main.StatusID.ToString();
                used[id] -= count;
                if (used[id] == 0) used.Remove(id);
            }
        }
        return total;
    }

    /// <summary>累加适用于当前命中目标的状态增伤；武器 Index(n) 在状态实例上下文内求值。</summary>
    public float GetStatusDMGBonus(string effectID2, string skillID2, int skillType, string element, BattleEntity target)
        => GetTeamDamageBonus(element)
            + GetStatusAdditiveAttributeBonus("DMGBonus", effectID2, skillID2, skillType, element, target);

    public float GetStatusHealingBonus(string part, string effectID2, string skillID2,
        int skillType, string element, BattleEntity target)
        => GetStatusAdditiveAttributeBonus(part, effectID2, skillID2, skillType, element, target);

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
    bool StatusAppliesTo(StatusInstance status, string effectID2, string skillID2, int skillType, string element, BattleEntity target,
        ReactionType? damageReaction = null)
    {
        if (status == null || !status.IsActive || status.MainData == null) return false;
        if (!string.IsNullOrWhiteSpace(status.MainData.ApplyReactionType)
            && (!damageReaction.HasValue || !ReactionFilterMatcher.Matches(status.MainData.ApplyReactionType,
                damageReaction.Value, null, element))) return false;
        skillID2 = DamageQueryContext.ResolveSkillID(effectID2, skillID2);
        if (!StatusAppliesTo(status.MainData, effectID2, skillID2, skillType, element)) return false;
        if (!string.IsNullOrEmpty(status.MainData.ScriptHook))
        {
            var context = new ScriptHookContext
            {
                Caster = status.Caster,
                Target = target,
                Status = status,
                Recipient = this,
                StatusHost = StatusHostAccess.FindHost(status, this),
                HitEffectID = effectID2,
                DamageSkillID = skillID2,
                DamageType = DamageQueryContext.ResolveType(effectID2),
                DamageReaction = damageReaction,
                DamageElement = element
            };
            if (!ScriptHookEvaluator.Evaluate(status.MainData.ScriptHook, context))
                return false;
        }
        return true;
    }

    internal static bool StatusAppliesTo(StatusMainData md, string effectID2, string skillID2, int skillType, string element)
        // skillType仅为旧属性查询签名保留，不参与伤害类别过滤。
        => StatusAppliesTo(md, effectID2, DamageQueryContext.ResolveSkillID(effectID2, skillID2),
            DamageQueryContext.ResolveType(effectID2), element);

    internal static bool StatusAppliesTo(StatusMainData md, string effectID2, string skillID2, string damageType, string element)
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
            bool matched = false;
            foreach (string configuredType in md.ApplyDamageType.Split(';'))
            {
                if (string.Equals(configuredType.Trim(), "All", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(damageType)
                        && string.Equals(configuredType.Trim(), damageType.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }
        if (!string.IsNullOrEmpty(md.ApplyElementType))
        {
            bool matched = false;
            // Physical 与 None（直接伤害）独立匹配，空白查询不假定伤害性质。
            string actualElement = element?.Trim();
            foreach (string configured in md.ApplyElementType.Split(';'))
                if (string.Equals(configured.Trim(), "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(configured.Trim(), actualElement, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            if (!matched) return false;
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
