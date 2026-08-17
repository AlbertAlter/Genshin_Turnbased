using UnityEngine;

/// <summary>
/// 元素反应管理器（2026-08-14）。
/// 集中管理所有元素反应：增幅类（融化/蒸发）倍率、剧变类（超载/感电/冻结/结晶/激化/绽放）后续扩展。
/// 接入点：伤害结算（CalculateDamage/CalculateStatusDamage 之后、护盾吸收之前）。
/// 附着系统（BattleEntity.ElementalAuras / ApplyAura / ConsumeAura）已就绪。
/// </summary>
public static class ElementReactionManager
{
    // 增幅反应倍率（原神：火打冰 ×2.0，冰打火 ×1.5）
    public const float MeltFireOnCryo = 2.0f; // 火伤 → 目标冰附着：×2.0
    public const float MeltCryoOnFire = 1.5f; // 冰伤 → 目标火附着：×1.5

    // ========== 冻结（剧变反应：水×冰） ==========
    // 元素反应文档：水冰消耗比 1:1，消耗到至少一方耗尽；赋予冻结状态（无法行动），
    // 持续回合数 = 本次反应消耗元素量向上取整（2水攻3冰→2回合；4冰攻1.5水→2回合）；
    // 允许过量水/冰残留；单次冻结结束前不再陷入新冻结；
    // 冻结状态视作剩余回合数的冰附着参与冰相关反应。
    /// <summary>冻结状态ID（配表 StatusData_StatusData_Main.csv 待建；当前查不到时元素照常消耗、状态暂不挂载并打警告）。</summary>
    public const string FreezeStatusID2 = "ST_Freeze";

    /// <summary>冻结状态视作冰附着：元素量 = 冻结剩余回合数（文档：触发与冰相关反应时，冻结视作该量冰附着）。</summary>
    public static float GetFrozenAuraAsCryo(BattleEntity target)
    {
        if (target == null || !target.StatusDict.TryGetValue(FreezeStatusID2, out var inst)) return 0f;
        return Mathf.Max(0f, inst.RemainingPhaseCount);
    }

    /// <summary>消耗冻结状态视作冰附着：扣减剩余回合数，归 0 解除冻结。返回实际消耗量。</summary>
    public static float TryConsumeFrozen(BattleEntity target, float amount)
    {
        if (target == null || amount <= 0f || !target.StatusDict.TryGetValue(FreezeStatusID2, out var inst)) return 0f;
        float consumed = Mathf.Min(inst.RemainingPhaseCount, amount);
        inst.RemainingPhaseCount -= Mathf.CeilToInt(consumed);
        LogManager.Log(LogCategory.Reaction, $"冻结视作冰附着被消耗 {consumed:F1}（剩余 {inst.RemainingPhaseCount} 回合）");
        if (inst.RemainingPhaseCount <= 0)
        {
            target.StatusDict.Remove(FreezeStatusID2);
            LogManager.Log(LogCategory.Reaction, $"冻结解除（消耗殆尽）：{target.EntityID}");
        }
        return consumed;
    }

    /// <summary>施加冻结状态：持续回合数 = 本次反应消耗量向上取整；单次冻结结束前不重复冻结；
    /// Caster 暂记目标自身（冻结无可行动效果）；AddInPhase/TriggerPhase 待配表状态定义后由状态机接管。</summary>
    static void TryApplyFreeze(BattleEntity target, int duration)
    {
        if (target == null || duration <= 0) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusMainDict.TryGetValue(FreezeStatusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.Reaction, $"冻结状态 {FreezeStatusID2} 配表未建（StatusData_StatusData_Main.csv），元素已消耗但状态未挂载");
            return;
        }
        if (target.StatusDict.ContainsKey(FreezeStatusID2))
        {
            LogManager.Log(LogCategory.Reaction, $"目标 {target.EntityID} 已冻结，不重复冻结（元素已照常消耗）");
            return;
        }
        target.AddStatus(FreezeStatusID2, target, duration, 0, 0, statusMain);
        LogManager.Log(LogCategory.Reaction, $"冻结 -> {target.EntityID}（持续 {duration} 回合，无法行动）");
    }

    /// <summary>碎冰（接口预留，2026-08-15）：冻结目标受到高于自身韧性（Poise）的攻击时，解除冻结并受到物理碎冰伤害（与攻击角色精通有关）。
    /// 接入点：伤害结算（削韧判定处调用）。</summary>
    public static void TryShatter(BattleEntity target, float attackPoise, BattleEntity attacker)
    {
        if (target == null || attacker == null || !target.StatusDict.ContainsKey(FreezeStatusID2)) return;
        if (attackPoise < target.Poise) return; // 削韧不足不碎冰
        target.StatusDict.Remove(FreezeStatusID2);
        // TODO：物理碎冰伤害 = f(attacker 元素精通)（元素反应文档：与攻击角色的精通有关），需接入伤害结算
        LogManager.Log(LogCategory.Reaction, $"碎冰：{attacker.EntityID} 削韧 {attackPoise:F0} ≥ {target.EntityID} 韧性 {target.Poise:F0}，解除冻结");
    }

    /// <summary>
    /// 尝试触发元素反应（伤害结算前调用）。
    /// </summary>
    /// <param name="target">伤害目标</param>
    /// <param name="attackElement">本次攻击元素（Pyro/Cryo/...，空或 None = 无元素，不反应）</param>
    /// <param name="attackAmount">本次攻击元素量（附着量，无附着=0）</param>
    /// <param name="baseDamage">反应前伤害（触发增幅反应会乘倍率）</param>
    /// <param name="reactionName">触发的反应名（未触发为空字符串）</param>
    /// <param name="attackRemain">攻击元素反应后的残留量（调用方按此量上附着；未反应=attackAmount 原样）</param>
    /// <returns>反应后的伤害（未触发 = baseDamage 原样返回）</returns>
    public static float TryReaction(BattleEntity target, string attackElement, float attackAmount, float baseDamage, out string reactionName, out float attackRemain)
    {
        reactionName = "";
        attackRemain = attackAmount;
        if (target == null || string.IsNullOrEmpty(attackElement) || attackElement == "None")
            return baseDamage;

        float mult = 1f;
        string name = "";
        float A = Mathf.Max(0f, attackAmount);

        // 融化（增幅反应）：按元素反应文档消耗——火冰消耗比 1:2（火克冰），消耗到至少一方耗尽（2026-08-15）：
        // Δ火 = min(火量, 冰量/2)；Δ冰 = min(冰量, 火量×2)。
        // 例：冰2 攻 火4 → 火消耗 min(4, 2/2)=1，冰消耗2（耗尽）→ 火残留 3（4火不会被2冰清零）。
        // 火攻击 → 目标有冰附着（含冻结状态视作冰，2026-08-15）：伤害 ×2.0
        if (attackElement == "Pyro" && (target.GetAura("Cryo") != null || GetFrozenAuraAsCryo(target) > 0f))
        {
            float F = A;                          //攻击火量
            //冰附着 = 普通冰附着 + 冻结视作冰（冻结视作剩余回合数的冰附着）
            float I = (target.GetAura("Cryo") != null ? target.GetAura("Cryo").AuraAmount : 0f) + GetFrozenAuraAsCryo(target);
            float consumeI = Mathf.Min(I, 2f * F);
            float consumeF = Mathf.Min(F, I / 2f);
            //先消耗普通冰附着，剩余消耗冻结视作冰
            float cRemain = consumeI;
            if (target.GetAura("Cryo") != null)
            {
                float c = Mathf.Min(target.GetAura("Cryo").AuraAmount, cRemain);
                target.ConsumeAura("Cryo", c);
                cRemain -= c;
            }
            if (cRemain > 0f) TryConsumeFrozen(target, cRemain);
            attackRemain = Mathf.Max(0f, F - consumeF);
            mult = MeltFireOnCryo;
            name = "融化（火×冰）";
        }
        // 冰攻击 → 目标有火附着：伤害 ×1.5
        else if (attackElement == "Cryo" && target.GetAura("Pyro") != null)
        {
            float I = A;                          //攻击冰量
            float F = target.GetAura("Pyro").AuraAmount; //附着火量
            float consumeF = Mathf.Min(F, I / 2f);
            float consumeI = Mathf.Min(I, 2f * F);
            target.ConsumeAura("Pyro", consumeF);
            attackRemain = Mathf.Max(0f, I - consumeI);
            mult = MeltCryoOnFire;
            name = "融化（冰×火）";
        }
        // 冻结（剧变反应）：水×冰 1:1 消耗（2026-08-15，元素反应文档）——
        // 水攻冰附 / 冰攻水附 均可触发；消耗到至少一方耗尽；允许过量水/冰残留；
        // 冻结回合数 = 本次消耗量向上取整；冻结状态视作冰附着参与消耗；无直接伤害（mult=1）。
        else if ((attackElement == "Hydro" && (target.GetAura("Cryo") != null || GetFrozenAuraAsCryo(target) > 0f))
              || (attackElement == "Cryo" && target.GetAura("Hydro") != null))
        {
            float waterAmt, cryoAmt;
            if (attackElement == "Hydro")
            {
                waterAmt = A;
                cryoAmt = (target.GetAura("Cryo") != null ? target.GetAura("Cryo").AuraAmount : 0f) + GetFrozenAuraAsCryo(target);
            }
            else // Cryo 攻
            {
                cryoAmt = A;
                waterAmt = target.GetAura("Hydro") != null ? target.GetAura("Hydro").AuraAmount : 0f;
            }
            float consumeW = Mathf.Min(waterAmt, cryoAmt); //1:1，消耗到至少一方耗尽
            float consumeC = Mathf.Min(cryoAmt, waterAmt);
            //冰侧消耗：先普通冰附着，剩余消耗冻结视作冰
            float cRemain = consumeC;
            if (target.GetAura("Cryo") != null)
            {
                float c = Mathf.Min(target.GetAura("Cryo").AuraAmount, cRemain);
                target.ConsumeAura("Cryo", c);
                cRemain -= c;
            }
            if (cRemain > 0f) TryConsumeFrozen(target, cRemain);
            //水侧消耗（冰攻击时消耗目标的水附着；水攻击时消耗量即攻击方水量，残留见下）
            if (attackElement == "Cryo") target.ConsumeAura("Hydro", consumeW);
            attackRemain = attackElement == "Hydro"
                ? Mathf.Max(0f, waterAmt - consumeW)
                : Mathf.Max(0f, cryoAmt - consumeC);
            //冻结回合数 = 本次反应消耗量向上取整（2水攻3冰→2回合；4冰攻1.5水→2回合）
            TryApplyFreeze(target, Mathf.CeilToInt(consumeC));
            name = "冻结";
        }

        // 后续反应（蒸发 / 超载 / 感电 / 冻结 / 结晶 / 激化 / 绽放）在此扩展

        if (name.Length == 0) return baseDamage;

        reactionName = name;
        LogManager.Log(LogCategory.Reaction, $"{name}：{baseDamage:F1} ×{mult} → {baseDamage * mult:F1}（目标{target.EntityID}，攻击量{A:F1} 反应后残留{attackRemain:F1}）");
        return baseDamage * mult;
    }
}
