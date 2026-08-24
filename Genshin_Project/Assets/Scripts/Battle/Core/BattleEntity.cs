using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 元素附着：元素反应 2.0 的基础数据。
/// </summary>
[Serializable]
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
    Crystallize
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
    // ========== 行动触发计数（2026-08-14，Status_Action 的 MaxTimePerTurn/MaxTimePerLife/Cooldown） ==========
    public int TurnTriggerCount;                    // 本回合已触发次数（MaxTimePerTurn，回合开始懒重置）
    public int LifeTriggerCount;                    // 全场已触发次数（MaxTimePerLife，不重置）
    public int LastTriggerTurn = -1;                // 上次触发回合（Cooldown 冷却判定）
    public int LastActionTurn = -1;                 // 上次计数回合（懒重置对比用）
    public long ApplyOrder;                         // 施加顺序序号（全局递增，同阶段结算时先施加的先触发）
    /// <summary>BindStatus 绑定关系：父状态存在期间，这些子状态挂在目标身上；父状态移除时连带移除（2026-08-06）</summary>
    public List<StatusInstance> BoundStatuses = new List<StatusInstance>();
}

/// <summary>
/// 战斗实体类，角色和敌人共用。
/// 属性字段对应伤害计算配表术语 1.1~1.4。
/// </summary>
public class BattleEntity : MonoBehaviour
{
    public enum EntityType { Character, Enemy }

    /// <summary>全局施加序号计数器：保证状态按施加顺序结算</summary>
    public static long _applyOrderCounter;

    // ========== 身份 ==========
    public EntityType Type;
    public int EntityID;
    /// <summary>角色控制器引用（角色实体在 Init 时由 CharacterBattleController 设置，用于状态行动回找执行器）</summary>
    [NonSerialized] public CharacterBattleController CharacterCtrl;

    // ========== 位置（2026-08-06 位置系统） ==========
    /// <summary>所在阵营（Ally=我方/Enemy=敌方）</summary>
    public BattleSide Side;
    /// <summary>在己方队列中的位置编号（我方 1-4 从左往右；敌方 1-5 从右往左）</summary>
    public int SlotPosition;
    /// <summary>威胁度（Enemy_Main.Threat，自动选择目标用；空=0）</summary>
    public int Threat;
    public int ConstellationLevel;   // 命座等级 0-6（效果实装前仅存储）
    public bool IsAscended;          // 是否已突破（突破加成实装前仅存储）

    // ========== 等级 ==========
    public int Level;

    // ========== 基础属性（公式 1.1 ~ 1.4） ==========
    public float TotalHP;
    public float CurrentHP;
    public float TotalATK;
    public float TotalDEF;

    // ========== 元素精通（公式 1.11: TotalEM = EM + WeaponEM + EMBonus） ==========
    public float EM;
    public float TotalEM;

    // ========== 暴击（公式 1.6） ==========
    public float CritRate;
    public float CritDMG;

    // ========== 充能（公式 1.12） ==========
    public float TotalRechargeRate;
    public int MaxEnergy;
    public int CurrentEnergy;

    // ========== 伤害加成类（公式 1.8 DMGBonus） ==========
    public float BaseDMGBonusFlat;
    public float DMGBonus;
    public float BaseDMGBonus;
    public float LunarDMGBonus;
    public float StellarDMGBonus;

    // ========== 防御穿透类（公式 1.5 DEF） ==========
    public float DEFReduction;
    public float DEFIgnored;

    // ========== 抗性（公式 1.7 Res） ==========
    public float PhysicalRes;
    public float PyroRes;
    public float HydroRes;
    public float ElectroRes;
    public float CryoRes;
    public float AnemoRes;
    public float DendroRes;
    public float GeoRes;
    public float ResBonus;

    // ========== 元素增伤 ==========
    public float PhysicalDmgBonus;
    public float PyroDmgBonus;
    public float HydroDmgBonus;
    public float ElectroDmgBonus;
    public float CryoDmgBonus;
    public float AnemoDmgBonus;
    public float DendroDmgBonus;
    public float GeoDmgBonus;

    // ========== 防护/治疗 ==========
    public float ShieldStrength;
    public float HealBonus;
    public float BeHealedBonus;
    public float Elevation;

    // ========== 韧性（公式 1.9 Poise） ==========
    public float Poise;
    public float MaxPoise;

    // ========== 受击权重（公式 1.10 HitWeight） ==========
    public float HitWeight;

    // ========== 护盾 ==========
    public List<Shield> Shields = new List<Shield>();

    // ========== 元素附着 ==========
    public List<ElementalAura> ElementalAuras = new List<ElementalAura>();

    // ========== 状态 ==========
    public Dictionary<string, StatusInstance> StatusDict = new Dictionary<string, StatusInstance>();

    // 场地位置引用：该单位当前占据的格子（可能为null=不在场上）
    public FieldPosition Position;

    // ========== 生死判断 ==========
    public bool IsDead => CurrentHP <= 0;
    public bool IsAlive => CurrentHP > 0;

    // ========== 初始化（子类覆写） ==========
    // ========== 护盾操作 ==========
    public void AddShield(
        float value,
        string element,
        float duration,
        float strength = 1f,
        ShieldKind kind = ShieldKind.Skill,
        int originPhase = -1)
    {
        int resolvedOriginPhase = originPhase > 0
            ? originPhase
            : (BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1);
        Shields.Add(new Shield
        {
            Value = value,
            Element = element,
            Duration = duration,
            Strength = strength,
            Kind = kind,
            OriginPhase = resolvedOriginPhase
        });
    }

    public Shield AddOrReplaceCrystallizeShield(
        float value,
        string element,
        int duration,
        int originPhase)
    {
        if (value <= 0f || duration <= 0) return null;

        int resolvedOriginPhase = originPhase > 0
            ? originPhase
            : (BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1);
        Shield shield = null;
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield candidate = Shields[i];
            if (candidate == null)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (candidate.Kind != ShieldKind.Crystallize) continue;
            if (shield == null) shield = candidate;
            else Shields.RemoveAt(i);
        }
        if (shield == null)
        {
            shield = new Shield();
            Shields.Add(shield);
        }

        shield.Value = value;
        shield.Element = element;
        shield.Duration = duration;
        shield.Strength = 1f;
        shield.Kind = ShieldKind.Crystallize;
        shield.OriginPhase = resolvedOriginPhase;
        return shield;
    }

    public void RemoveShields(ShieldKind kind)
    {
        Shields.RemoveAll(shield => shield == null || shield.Kind == kind);
    }

    public void TickShields(int currentPhase)
    {
        if (currentPhase <= 0) return;

        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (shield.Kind != ShieldKind.Crystallize || shield.OriginPhase != currentPhase)
                continue;

            shield.Duration -= 1f;
            if (shield.Duration <= 0f)
                Shields.RemoveAt(i);
        }
    }

    /// <summary>
    /// 多个护盾同时承伤而非串联叠加，因此返回当前最强单盾的有效基础盾值。
    /// 不含元素吸收倍率；需要查询某种伤害的实际最大吸收量时使用带元素参数的重载。
    /// </summary>
    public float GetTotalShieldHP()
    {
        return GetMaximumShieldCapacity(null, false);
    }

    /// <summary>返回面对指定伤害元素时，当前所有护盾中最大的实际吸收量。</summary>
    public float GetTotalShieldHP(string damageElement)
    {
        return GetMaximumShieldCapacity(damageElement, true);
    }

    private float GetMaximumShieldCapacity(string damageElement, bool includeAbsorptionMultiplier)
    {
        float maximum = 0f;
        foreach (Shield shield in Shields)
        {
            if (shield == null || shield.Value <= 0f) continue;

            float factor = (1f + ShieldStrength) * shield.Strength;
            if (includeAbsorptionMultiplier)
                factor *= GetShieldAbsorptionMultiplier(shield.Element, damageElement);
            if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor)) continue;

            float capacity = shield.Value * factor;
            if (!float.IsNaN(capacity) && capacity > maximum)
                maximum = capacity;
        }
        return maximum;
    }

    /// <summary>治疗：恢复HP（不超过上限，2026-08-14）。</summary>
    public void Heal(float amount)
    {
        if (amount <= 0 || IsDead || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        CurrentHP = Mathf.Min(TotalHP, CurrentHP + amount);
    }

    public float AbsorbDamageWithShield(float incomingDamage, string damageElement)
    {
        if (incomingDamage <= 0f || Shields.Count == 0)
            return Mathf.Max(0f, incomingDamage);

        float maxBlocked = 0f;
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null || shield.Value <= 0f)
            {
                Shields.RemoveAt(i);
                continue;
            }

            float absorbMultiplier = GetShieldAbsorptionMultiplier(shield.Element, damageElement);
            float factor = absorbMultiplier * (1f + ShieldStrength) * shield.Strength;
            if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor))
                continue;

            float capacity = shield.Value * factor;
            if (capacity <= 0f || float.IsNaN(capacity))
                continue;

            float blocked = Mathf.Min(incomingDamage, capacity);
            if (blocked > 0f)
            {
                shield.Value = Mathf.Max(0f, shield.Value - blocked / factor);
                maxBlocked = Mathf.Max(maxBlocked, blocked);
            }
            if (shield.Value <= 0f)
                Shields.RemoveAt(i);
        }

        return Mathf.Max(0f, incomingDamage - maxBlocked);
    }

    private static float GetShieldAbsorptionMultiplier(string shieldElement, string damageElement)
    {
        if (string.IsNullOrEmpty(shieldElement) || shieldElement == "None") return 1f;
        if (shieldElement == "Geo") return 2.5f;
        return shieldElement == damageElement ? 2.5f : 1.5f;
    }

    // ========== 元素附着操作 ==========
    public void ApplyAura(string element, float amount, int sourceEntityID, string sourceEffectID = "", string sourceSkillID = "")
    {
        if (string.IsNullOrEmpty(element) || element == "None" || amount <= 0f)
            return;

        // 同元素再次附着时，无论新元素量高低，均由新附着完整覆盖。
        var existing = ElementalAuras.Find(a => a.Element == element);
        int originPhase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1;
        if (existing != null)
        {
            existing.AuraAmount = amount;
            existing.SourceEntityID = sourceEntityID;
            existing.SourceSkillID = sourceSkillID;
            existing.SourceEffectID = sourceEffectID;
            existing.OriginPhase = originPhase;
            existing.SkipNextOriginDecay = false;
        }
        else
        {
            ElementalAuras.Add(new ElementalAura
            {
                Element = element,
                AuraAmount = amount,
                SourceEntityID = sourceEntityID,
                SourceSkillID = sourceSkillID,
                SourceEffectID = sourceEffectID,
                OriginPhase = originPhase
            });
        }
    }

    public ElementalAura GetAura(string element)
    {
        return ElementalAuras.Find(a => a.Element == element);
    }

    public void RemoveAura(string element)
    {
        ElementalAuras.RemoveAll(a => a.Element == element);
    }

    public void ConsumeAura(string element, float amount)
    {
        var aura = GetAura(element);
        if (aura != null)
        {
            aura.AuraAmount -= amount;
            if (aura.AuraAmount <= 0) RemoveAura(element);
        }
    }

    // ================================================================
    //  元素量回合递减
    //  设计文档：元素附着每回合递减（1元素量持续2回合，每回合-0.5，最低至0）
    //  调用时机：每个阶段结算的"元素量回合递减"步骤（时间线第4步）
    // ================================================================
    public void TickAuras()
    {
        // 元素量每回合-0.5（2026-08-14）：根据施加的阶段定原点——
        // 附着只在"自己的原点阶段"（施加/刷新时的阶段）衰减，一回合只减一次
        int curPhase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1;
        for (int i = ElementalAuras.Count - 1; i >= 0; i--)
        {
            var aura = ElementalAuras[i];
            if (curPhase >= 0 && aura.OriginPhase >= 0 && aura.OriginPhase != curPhase)
                continue;
            if (aura.SkipNextOriginDecay)
            {
                aura.SkipNextOriginDecay = false;
                continue;
            }
            aura.AuraAmount -= 0.5f;
            if (aura.AuraAmount <= 0f)
                ElementalAuras.RemoveAt(i);
        }
    }

    // ========== 状态操作 ==========
    public bool HasStatus(string statusID2)
    {
        return StatusDict.ContainsKey(statusID2) && StatusDict[statusID2].IsActive;
    }

    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int initialPhaseCount)
    {
        if (StatusDict.TryGetValue(statusID2, out var existing))
        {
            existing.StackCount++;
            return existing;
        }
        var inst = new StatusInstance
        {
            StatusID2 = statusID2,
            StackCount = 1,
            RemainingPhaseCount = initialPhaseCount,
            Caster = caster,
            Snapshot = null,
            IsActive = false,
            ApplyOrder = ++_applyOrderCounter
        };
        StatusDict[statusID2] = inst;
        return inst;
    }

    // ========== 状态接口扩展（状态机 BattleManager 使用） ==========

    /// <summary>
    /// 添加状态（完整版）：支持 AddInPhase / TriggerPhase / Duration / MaxStack / WhenMax 规则。
    ///  - MaxStack&lt;=0：可无限叠加，每次 StackCount+1
    ///  - MaxStack&gt;0 且已满：WhenMax=="1" 时本次不施加；WhenMax!="1"（默认0）时移除最早施加的该状态再重新施加
    ///  - IsActive 置为 true，表示该状态已完成 OnApply（若有）
    /// </summary>
    public StatusInstance AddStatus(string statusID2, BattleEntity caster, int duration, int addInPhase, int triggerPhase, StatusMainData mainData)
    {
        if (StatusDict.TryGetValue(statusID2, out var existing))
        {
            // 已存在：按 MaxStack / WhenMax 规则处理
            int maxStack = mainData != null ? mainData.MaxStack : 0;
            if (maxStack > 0 && existing.StackCount >= maxStack)
            {
                if (mainData != null && mainData.WhenMax == "1")
                {
                    // 本次不施加
                    return existing;
                }
                else
                {
                    // 移除最早施加的该状态，重新施加
                    StatusBindingSystem.RemoveBindings(existing);
                    // ChangeControl 解除（2026-08-11）：状态消失时返回原技能/解冻
                    CharacterCtrl?.OnStatusChangeControlRemoved(existing);
                    // 状态结算登记注销 + PreAlliesDamage 钩子注销（2026-08-14，逻辑见 PreDamageHookSystem.cs）
                    BattleManager.Instance?.UnregisterStatusTick(existing);
                    PreDamageHookSystem.UnregisterPreDamageHook(existing.StatusID2);
                    // Kill 钩子注销（2026-08-19）：覆盖重建时旧实例不再参与死亡触发
                    KillHookSystem.Unregister(existing);
                    StatusDict.Remove(statusID2);
                    existing = null;
                }
            }
            else
            {
                existing.StackCount++;
                // 刷新持续时间（取大者，避免刷新导致提前消失）
                existing.RemainingPhaseCount = Mathf.Max(existing.RemainingPhaseCount, duration);
                existing.TriggerPhase = triggerPhase;
                existing.MainData = mainData;
                return existing;
            }
        }

        var inst = new StatusInstance
        {
            StatusID2 = statusID2,
            StackCount = 1,
            RemainingPhaseCount = duration,
            AddInPhase = addInPhase,
            TriggerPhase = triggerPhase,
            Caster = caster,
            MainData = mainData,
            Snapshot = null,
            IsActive = true,
            ApplyOrder = ++_applyOrderCounter
        };
        StatusDict[statusID2] = inst;

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

        // ========== 状态结算登记（2026-08-12） ==========
        // 以状态为中心：按 AddInPhase/TriggerPhase 登记到 BattleManager 的阶段桶，
        // 阶段结算按登记顺序（=施加顺序=大顺序）执行 OnTrigger/OnItsTurn/OnEnd。
        BattleManager.Instance?.RegisterStatusTick(inst, this, null);

        // ========== PreAlliesDamage 钩子登记（2026-08-14，逻辑见 PreDamageHookSystem.cs） ==========
        // 状态带 OnTrigger+ScriptHook=PreAlliesDamage 时登记，我方主动行为造成伤害前触发。
        PreDamageHookSystem.RegisterPreDamageHook(inst);

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
        if (!StatusDict.TryGetValue(statusID2, out var existing)) return false;
        if (stackCount < 0 || existing.StackCount <= stackCount)
        {
            // 连带移除绑定子状态（按子状态真实宿主查找，不再误用 Caster）。
            StatusBindingSystem.RemoveBindings(existing);
            // ChangeControl 解除（2026-08-11）：状态消失（移除/到期统一走这里）时返回原技能/解冻
            CharacterCtrl?.OnStatusChangeControlRemoved(existing);
            // 状态结算登记注销（2026-08-12）
            BattleManager.Instance?.UnregisterStatusTick(existing);
            // PreAlliesDamage 钩子注销（2026-08-14，逻辑见 PreDamageHookSystem.cs）
            PreDamageHookSystem.UnregisterPreDamageHook(existing.StatusID2);
            // Kill 钩子注销（2026-08-19，逻辑见 KillHookSystem.cs）
            KillHookSystem.Unregister(existing);
            StatusDict.Remove(statusID2);
            return true;
        }
        else
        {
            existing.StackCount -= stackCount;
            return true;
        }
    }

    /// <summary>
    /// 查询指定状态剩余回合数（未施加返回 0）。
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
        return new List<StatusInstance>(StatusDict.Values);
    }

    /// <summary>
    /// 获取"生效状态"列表 = 自身身上的状态 + 所在位置上的状态。
    /// 位置上的状态对该位置的单位同样生效（位置状态不随单位死亡消失）。
    /// </summary>
    public List<StatusInstance> GetEffectiveStatusList()
    {
        var list = new List<StatusInstance>(StatusDict.Values);
        if (Position != null && Position.StatusList != null)
        {
            foreach (var s in Position.StatusList)
            {
                // 避免重复（同一状态同时挂在实体和位置时去重）
                if (!list.Exists(x => x.StatusID2 == s.StatusID2))
                    list.Add(s);
            }
        }
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

            int count = Mathf.Max(1, instance.StackCount);
            if (mainData.MaxCount > 0) count = Mathf.Min(count, mainData.MaxCount);
            foreach (StatusEffectData effect in dataManager.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (float.TryParse(effect.Param1, out float value))
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
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst.MainData == null) continue;
            string part = inst.MainData.GetMultiplierPart();
            if (part != "ATKBonus") continue;
            if (!StatusAppliesTo(inst.MainData, effectID2, skillID2, skillType, element)) continue;
            foreach (var kv in DataManager.Instance.StatusEffectDict)
            {
                // 效果ID数值前缀匹配：StatusEffectID / 100 == 父状态 StatusID
                var eff = kv.Value;
                if (eff.StatusEffectID == 0) continue;
                if (eff.StatusEffectID / 100 != inst.MainData.StatusID) continue;
                float.TryParse(eff.Param1, out float v);
                total += v;
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
        foreach (var inst in GetEffectiveStatusList())
        {
            if (inst.MainData == null) continue;
            string part = inst.MainData.GetMultiplierPart();
            if (part != "CritRate") continue;
            if (!StatusAppliesTo(inst, effectID2, skillID2, skillType, element, target)) continue;
            foreach (var kv in DataManager.Instance.StatusEffectDict)
            {
                var eff = kv.Value;
                if (eff.StatusEffectID == 0) continue;
                if (eff.StatusEffectID / 100 != inst.MainData.StatusID) continue;
                float.TryParse(eff.Param1, out float v);
                total += v;
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
        if (status == null || status.MainData == null) return false;
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
                0 => "普攻",
                1 => "重击",
                2 => "战技",
                _ => "爆发",
            };
            if (md.ApplyDamageType != typeName) return false;
        }
        if (!string.IsNullOrEmpty(md.ApplyElementType))
        {
            if (element != md.ApplyElementType) return false;
        }
        return true;
    }

    /// <summary>
    /// 获取指定状态实例（不存在返回 null）。
    /// </summary>
    public StatusInstance GetStatus(string statusID2)
    {
        StatusDict.TryGetValue(statusID2, out var inst);
        return inst;
    }

    // ========== 伤害/治疗 ==========
    /// <summary>
    /// 带来源的扣血接口（2026-08-19，状态钩子任务）。固定顺序：
    ///  1. 记录 HPBefore；2. 扣除并限制血量不低于0；3. 生成 DamageResolvedEvent；
    ///  4. HitLanded 时触发目标的统一 OnHit（StatusOnHitHookSystem）；
    ///  5. CausedDeath 时广播死亡声明（KillHookSystem，先于战斗结束通知，确保击杀最后敌人仍能收到）；
    ///  6. 最后调用 BattleManager.CheckBattleEnd()；7. 返回事件对象。
    /// </summary>
    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source, bool hitLanded = true)
    {
        if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
            damage = 0f;
        if (IsDead)
            hitLanded = false;

        float hpBefore = CurrentHP;
        CurrentHP -= damage;
        if (CurrentHP < 0f) CurrentHP = 0f;

        var damageEvent = new DamageResolvedEvent
        {
            Target = this,
            Source = source ?? DamageSourceInfo.CreateUnknown(),
            HPBefore = hpBefore,
            HPAfter = CurrentHP,
            RequestedHPDamage = damage,
            ActualHPDamage = Mathf.Max(0f, hpBefore - CurrentHP),
            HitLanded = hitLanded,
            CausedDeath = hpBefore > 0f && CurrentHP <= 0f
        };

        // 统一 OnHit：所有伤害路径（角色/敌人/状态/反应/燃烧）都走这里，防止同一 hit 重复触发
        if (hitLanded)
            StatusOnHitHookSystem.NotifyHit(damageEvent);
        // 死亡声明：目标从存活变死亡只广播一次（已死亡目标再次受击 CausedDeath=false）
        if (damageEvent.CausedDeath)
            KillHookSystem.NotifyDeath(damageEvent);

        // 战斗结束判定：敌方全灭=胜利，我方全灭=失败（2026-08-13）
        if (IsDead)
            BattleManager.Instance?.CheckBattleEnd();
        return damageEvent;
    }

    /// <summary>无来源扣血接口（兼容旧调用）：内部使用 DamageSourceInfo.CreateUnknown()，与带来源版共用同一套扣血逻辑。</summary>
}
