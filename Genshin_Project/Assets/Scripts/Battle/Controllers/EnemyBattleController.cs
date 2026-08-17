using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人战斗控制器。
/// 职责：
///  - 初始化敌人属性（Enemy_Main + Base_HP/Base_ATK 曲线 + 系数）
///  - 每回合行动（TakeEnemyTurn）：按 EnemyAI_Rule 权重挑选可用技能并释放
///  - 技能效果执行（Damage 基础版；ApplyStatus 等扩展后续轮实现）
/// </summary>
public class EnemyBattleController : MonoBehaviour
{
    public BattleEntity Entity { get; private set; }

    public int EnemyID { get; private set; }

    // ========== AI 运行时状态 ==========
    private int _currentPatternID = 0;
    private List<EnemyAIRuleData> _currentRules = new List<EnemyAIRuleData>();
    private Dictionary<int, int> _skillCooldownLeft = new Dictionary<int, int>(); // 技能ID -> 剩余冷却回合
    private Dictionary<int, int> _skillUsedThisTurn = new Dictionary<int, int>(); // 技能ID -> 本回合已用次数

    // ================================================================
    //  初始化
    // ================================================================
    /// <summary>
    /// 初始化敌人实体属性。
    /// 公式（配表术语 1.14/1.15）：
    ///   TotalHP  = BaseHP(曲线) × HP_coeff
    ///   TotalATK = BaseATK(曲线,按ATK_Curve) × ATK_coeff
    /// 抗性/增伤直接读 Enemy_Main。
    /// </summary>
    public void InitEnemy(int enemyID, int level)
    {
        EnemyID = enemyID;

        var entity = GetComponent<BattleEntity>();
        if (entity == null) entity = gameObject.AddComponent<BattleEntity>();
        Entity = entity;

        var dm = DataManager.Instance;
        entity.Type = BattleEntity.EntityType.Enemy;
        entity.EntityID = enemyID;
        entity.Level = level;

        if (dm.EnemyMainDict.TryGetValue(enemyID, out var main))
        {
            float baseHP = dm.GetEnemyBaseHP(level);
            float baseATK = dm.GetEnemyBaseATK(main.ATK_Curve, level);

            entity.TotalHP = baseHP * main.HP_coeff;
            entity.TotalATK = baseATK * main.ATK_coeff;
            entity.CurrentHP = entity.TotalHP;

            entity.MaxPoise = main.Poise;
            entity.Poise = main.Poise;

            entity.Threat = main.Threat;

            entity.PhysicalRes = main.PhysicalRes;
            entity.PyroRes = main.PyroRes;
            entity.HydroRes = main.HydroRes;
            entity.ElectroRes = main.ElectroRes;
            entity.CryoRes = main.CryoRes;
            entity.AnemoRes = main.AnemoRes;
            entity.DendroRes = main.DendroRes;
            entity.GeoRes = main.GeoRes;

            entity.PhysicalDmgBonus = main.PhysicalDmgBonus;
            entity.PyroDmgBonus = main.PyroDmgBonus;
            entity.HydroDmgBonus = main.HydroDmgBonus;
            entity.ElectroDmgBonus = main.ElectroDmgBonus;
            entity.CryoDmgBonus = main.CryoDmgBonus;
            entity.AnemoDmgBonus = main.AnemoDmgBonus;
            entity.DendroDmgBonus = main.DendroDmgBonus;
            entity.GeoDmgBonus = main.GeoDmgBonus;

            // AI：加载第一个行为模式
            if (dm.EnemyAIDict.TryGetValue(enemyID, out var aiList) && aiList.Count > 0)
            {
                _currentPatternID = aiList[0].PatternID;
                LoadPatternRules(_currentPatternID);
            }

            LogManager.Log(LogCategory.Enemy, $"{enemyID} lv{level} initialized: HP={entity.TotalHP} (cur {entity.CurrentHP}) ATK={entity.TotalATK}");
        }
        else
        {
            LogManager.LogWarning(LogCategory.Enemy, $"EnemyMainDict missing enemyID={enemyID}");
        }
    }

    // ================================================================
    //  AI 行动
    // ================================================================
    /// <summary>
    /// 敌人回合行动入口：按权重选技能并执行。
    /// 每回合至少行动一次；可用行动次数由 Enemy_Main.ActsGiven 决定（当前简化：先按1次）。
    /// </summary>
    public void TakeEnemyTurn()
    {
        if (Entity == null || Entity.IsDead) return;

        int acts = 1; // 简化：暂时固定每次行动1次，ActsGiven 扩展后续轮
        for (int i = 0; i < acts; i++)
        {
            var rule = PickSkillByWeight();
            if (rule == null) break;

            if (dm.EnemySkillMainDict.TryGetValue(rule.SkillID, out var skill))
            {
                ExecuteEnemySkill(skill);
            }
        }

        // 回合结束：重置本回合使用次数（冷却递减移至 BattleManager 全局跨回合处理）
        _skillUsedThisTurn.Clear();
    }

    private DataManager dm => DataManager.Instance;

    /// <summary>
    /// 按权重挑选当前模式下的可用技能。
    /// 处于冷却或已达每回合使用上限的技能不参与权重求和。
    /// </summary>
    private EnemyAIRuleData PickSkillByWeight()
    {
        var candidates = new List<EnemyAIRuleData>();
        int totalWeight = 0;
        foreach (var rule in _currentRules)
        {
            if (_skillCooldownLeft.TryGetValue(rule.SkillID, out int cd) && cd > 0) continue;

            if (dm.EnemySkillMainDict.TryGetValue(rule.SkillID, out var skill))
            {
                int used = _skillUsedThisTurn.TryGetValue(rule.SkillID, out int u) ? u : 0;
                if (skill.UsePerTurn > 0 && used >= skill.UsePerTurn) continue;
            }

            candidates.Add(rule);
            totalWeight += rule.Weight;
        }

        if (candidates.Count == 0 || totalWeight <= 0)
        {
            LogManager.Log(LogCategory.AI, $"无可用候选技能。冷却={string.Join(",", _skillCooldownLeft)}");
            return null;
        }

        int roll = Random.Range(0, totalWeight);
        int acc = 0;
        // [AI调试] 打印候选与随机数，确认跳舞是否进入候选
        LogManager.Log(LogCategory.AI, $"候选技能: {string.Join(",", candidates.ConvertAll(c => c.SkillID))} (总权重{totalWeight}) roll={roll}");
        foreach (var rule in candidates)
        {
            acc += rule.Weight;
            if (roll < acc) return rule;
        }
        return candidates[candidates.Count - 1];
    }

    /// <summary>
    /// 执行敌人技能：遍历其全部效果（EnemySkill_Effect + EnemySkill_Param）。
    /// 当前实现 Damage 类型；ApplyStatus 等留后续轮。
    /// </summary>
    private void ExecuteEnemySkill(EnemySkillMainData skill)
    {
        LogManager.Log(LogCategory.Enemy, $"{EnemyID} uses skill {skill.EnemySkillID2} ({skill.EnemySkillName})");

        // 标记本回合使用次数
        _skillUsedThisTurn[skill.EnemySkillID] = (_skillUsedThisTurn.TryGetValue(skill.EnemySkillID, out int u) ? u : 0) + 1;

        // 施放后设置冷却
        if (skill.Cooldown > 0)
            _skillCooldownLeft[skill.EnemySkillID] = Mathf.CeilToInt(skill.Cooldown);

        // 按 EffectIndex 排序执行效果
        // EnemySkill_Effect 的 SkillEffectID 规则：技能ID*100 + 序号（如 2000101 -> 效果1:200010101）
        var effects = new List<EnemySkillEffectData>();
        foreach (var kv in dm.EnemySkillEffectDict)
        {
            // SkillEffectID / 100 == 技能ID（200010101/100 = 2000101）
            if (kv.Value.SkillEffectID / 100 == skill.EnemySkillID)
                effects.Add(kv.Value);
        }

        effects.Sort((a, b) => a.EffectIndex.CompareTo(b.EffectIndex));

        foreach (var eff in effects)
        {
            ExecuteEnemyEffect(eff);
        }
    }

    /// <summary>
    /// 执行单个敌人效果。当前实现 Damage；其他类型打印日志占位。
    /// </summary>
    private void ExecuteEnemyEffect(EnemySkillEffectData eff)
    {
        switch (eff.EffectType)
        {
            case "Damage":
                ExecuteEnemyDamage(eff);
                break;
            case "ApplyStatus":
                ExecuteEnemyApplyStatus(eff);
                break;
            default:
                LogManager.Log(LogCategory.Enemy, $"effect {eff.SkillEffectID2} type {eff.EffectType} not implemented yet");
                break;
        }
    }

    /// <summary>
    /// 敌人施加状态：与角色共用 BattleEntity.AddStatus（同一套状态系统）。
    /// 目标：TargetType=Self → 自己；Enemy → 敌方（我方角色）；EnemyField → 我方场地（简化=全部我方角色）。
    /// </summary>
    private void ExecuteEnemyApplyStatus(EnemySkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;

        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.Enemy, $"状态不存在: {statusID2}");
            return;
        }

        List<BattleEntity> targets = new List<BattleEntity>();
        switch (eff.TargetType)
        {
            case "Self":
            case null:
            case "":
                if (Entity != null) targets.Add(Entity);
                break;
            case "Enemy":
            case "EnemyField":
                foreach (var a in BattleManager.Instance.Allies)
                    if (a.Entity != null && a.Entity.IsAlive)
                        targets.Add(a.Entity);
                break;
            default:
                if (Entity != null) targets.Add(Entity);
                break;
        }

        foreach (var t in targets)
        {
            // Duration/AddInPhase/TriggerPhase 从效果行取（配表术语：AddInPhase 为空默认放施放技能阶段）
            int addPhase = eff.AddInPhase > 0 ? eff.AddInPhase : 5; // 敌方行动阶段=5
            t.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase, statusMain);
            LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {t.EntityID} (dur={eff.Duration}, phase={addPhase})");
        }
    }

    /// <summary>
    /// 敌人伤害：倍率×TotalATK×抗性承伤（敌人公式无暴击/无增伤状态遍历）。
    /// 目标：当前简化为我方第一个存活角色。
    /// </summary>
    private void ExecuteEnemyDamage(EnemySkillEffectData eff)
    {
        if (!dm.EnemySkillParamDict.TryGetValue(eff.SkillEffectID2, out var param))
        {
            LogManager.LogWarning(LogCategory.Enemy, $"no param for effect {eff.SkillEffectID2}");
            return;
        }

        // 敌人 Hits 格式：倍率;削韧;元素量（无属性token，统一乘 TotalATK）
        // 复用 HitDataParser.Parse（无 *属性 后缀时直接取倍率数字）
        HitData hit = HitDataParser.Parse(param.Hits1);
        if (hit.Multiplier <= 0)
        {
            LogManager.LogWarning(LogCategory.Enemy, $"parse hit failed: {param.Hits1}");
            return;
        }

        // 找目标：我方第一个存活角色
        CharacterBattleController target = FindTarget();
        if (target == null || target.Entity == null) return;

        // 状态 ATKBonus（敌人无 ApplyString/类型限制的上下文，传空=全部生效）
        float baseDmg = Entity.TotalATK * hit.Multiplier * (1f + Entity.GetStatusATKBonus("", "", 0, ""));

        // 增伤区（配表术语 1.16）：TotalDMG = TotalATK × SkillMultiplier × (1+DMGBonus) × DEFRes × Res
        // DMGBonus = 敌人自身所有状态中 MultiplierPart=DMGBonus 的效果数值累加（如 ST_Bunny_debuff=-0.1 → ×0.9）
        float dmgBonus = 0f;
        var allStatuses = Entity.GetEffectiveStatusList();
        LogManager.Log(LogCategory.DmgBonus, $"{Entity.EntityID} 行动前身上状态数={allStatuses.Count}");
        foreach (var inst in allStatuses)
        {
            dmgBonus += GetStatusDMGBonus(inst);
        }
        float dmgBonusFactor = 1f + dmgBonus;
        if (dmgBonusFactor < 0f) dmgBonusFactor = 0f;

        // 抗性承伤（配表术语 1.7）：目标对应元素抗性
        float res = GetTargetResistance(target.Entity, eff.Element);
        float resFactor = res < 0 ? 1f - res / 2f : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));

        // 对角色防御承伤（配表术语 1.12）：1 - DEF/(DEF + 5×攻击方等级 + 500)
        float defRes = 1f - target.Entity.TotalDEF / (target.Entity.TotalDEF + 5f * Entity.Level + 500f);
        if (defRes < 0f) defRes = 0f;

        float dmg = baseDmg * dmgBonusFactor * resFactor * defRes;
        target.Entity.TakeDamage(dmg);
        target.Entity.Poise -= hit.Poise;

        // 元素附着
        if (!string.IsNullOrEmpty(eff.Element) && eff.Element != "None" && hit.ElementAura > 0)
        {
            target.Entity.ApplyAura(eff.Element, hit.ElementAura, Entity.EntityID);
        }

        LogManager.Log(LogCategory.Enemy, $"{eff.SkillEffectID2} deals {dmg:F1} dmg to {target.gameObject.name} (hp left {target.Entity.CurrentHP:F1})");
    }

    /// <summary>
    /// 累加单个状态提供的 DMGBonus（配表 MultiplierPart 任一列==DMGBonus 的效果数值）。
    /// 效果数值在状态的 StatusEffect 里（如 ST_Bunny_debuff -> STE_Bunny_debuff Param1=-0.1）。
    /// </summary>
    private float GetStatusDMGBonus(StatusInstance inst)
    {
        if (inst == null || inst.MainData == null) return 0f;
        LogManager.Log(LogCategory.DmgBonus, $"状态{inst.StatusID2} MultiplierPart={inst.MainData?.GetMultiplierPart() ?? "<null>"}");
        if (inst.MainData.GetMultiplierPart() != "DMGBonus") return 0f;

        var dm = DataManager.Instance;
        if (dm == null || dm.StatusEffectDict == null) return 0f;

        float total = 0f;
        int statusID = inst.MainData.StatusID;
        foreach (var kv in dm.StatusEffectDict)
        {
            var eff = kv.Value;
            if (eff.StatusEffectID / 100 != statusID) continue;
            if (eff.EffectType != "Debuff" && eff.EffectType != "Buff") continue;
            if (float.TryParse(eff.Param1, out float val))
                total += val;
        }
        return total;
    }

    /// <summary>
    /// 获取目标实体对应元素的抗性。
    /// </summary>
    private float GetTargetResistance(BattleEntity target, string element)
    {
        switch (element)
        {
            case "Pyro": return target.PyroRes;
            case "Hydro": return target.HydroRes;
            case "Electro": return target.ElectroRes;
            case "Cryo": return target.CryoRes;
            case "Anemo": return target.AnemoRes;
            case "Dendro": return target.DendroRes;
            case "Geo": return target.GeoRes;
            default: return target.PhysicalRes;
        }
    }

    /// <summary>
    /// 查找目标：我方第一个存活角色（简化版，目标选择系统后续轮完善）。
    /// </summary>
    private CharacterBattleController FindTarget()
    {
        var bm = BattleManager.Instance;
        if (bm == null) return null;
        foreach (var ally in bm.Allies)
        {
            if (ally != null && ally.Entity != null && ally.Entity.IsAlive)
                return ally;
        }
        return null;
    }

    // ================================================================
    //  AI 辅助
    // ================================================================
    private void LoadPatternRules(int patternID)
    {
        _currentRules.Clear();
        foreach (var kv in dm.EnemyAIRuleDict)
        {
            // 规则归属校验：Rule 行的 EnemyID（空值继承后）必须等于当前敌人ID
            if (kv.Key != EnemyID) continue;
            foreach (var rule in kv.Value)
            {
                if (rule.PatternID == patternID) _currentRules.Add(rule);
            }
        }
        if (_currentRules.Count > 0)
        {
            _currentPatternID = patternID;
            LogManager.Log(LogCategory.Enemy, $"{EnemyID} loaded pattern {patternID}: {_currentRules.Count} rules");
        }
    }

    /// <summary>
    /// 技能冷却全局递减（由 BattleManager 在全局跨回合时统一调用）。
    /// </summary>
    public void TickSkillCooldowns()
    {
        var keys = new List<int>(_skillCooldownLeft.Keys);
        foreach (var k in keys)
        {
            _skillCooldownLeft[k]--;
            if (_skillCooldownLeft[k] <= 0) _skillCooldownLeft.Remove(k);
        }
    }
}
