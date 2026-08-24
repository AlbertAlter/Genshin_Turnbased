using UnityEngine;

/// <summary>
/// 韧性与倒地的统一入口。
/// 所有普通伤害、状态伤害和反应派生伤害都必须在护盾结算后把实际扣血量传入本类。
/// </summary>
public static class PoiseSystem
{
    public const string CharacterKnockdownStatusID = "ST_Fall_Character";
    public const string EnemyKnockdownStatusID = "ST_Fall_Enemy";
    public const int StandUpAPCost = 10;

    public static string GetKnockdownStatusID(BattleEntity target)
    {
        return target != null && target.Type == BattleEntity.EntityType.Enemy
            ? EnemyKnockdownStatusID
            : CharacterKnockdownStatusID;
    }

    public static bool IsKnockedDown(BattleEntity target)
    {
        return target != null && target.HasStatus(GetKnockdownStatusID(target));
    }

    /// <summary>
    /// 对一次已经完成护盾结算的命中应用削韧。
    /// 返回 true 表示本次新触发了倒地。
    /// </summary>
    public static bool ApplyPoiseDamage(
        BattleEntity target,
        float poiseDamage,
        float actualHPDamage,
        BattleEntity source = null)
    {
        if (target == null || !target.IsAlive) return false;
        if (poiseDamage <= 0f || actualHPDamage <= 0f) return false;
        if (target.MaxPoise < 0f || IsKnockedDown(target)) return false;

        target.Poise = Mathf.Max(0f, target.Poise - poiseDamage);
        if (target.Poise > 0f) return false;

        return ApplyKnockdown(target, source);
    }

    /// <summary>强制将目标韧性归零并施加对应阵营的倒地状态，供碎冰等机制调用。</summary>
    public static bool BreakPoise(BattleEntity target, BattleEntity source = null)
    {
        if (target == null || !target.IsAlive || target.MaxPoise < 0f) return false;
        target.Poise = 0f;
        return ApplyKnockdown(target, source);
    }

    public static bool ApplyKnockdown(BattleEntity target, BattleEntity source = null)
    {
        if (target == null || !target.IsAlive || target.MaxPoise < 0f) return false;

        string statusID = GetKnockdownStatusID(target);
        if (target.HasStatus(statusID)) return false;

        StatusMainData mainData = null;
        DataManager dataManager = DataManager.Instance;
        if (dataManager != null)
            dataManager.StatusMainDict.TryGetValue(statusID, out mainData);

        // EditMode 独立测试或配表尚未同步到 StreamingAssets 时仍保留真实 StatusInstance。
        // 正常运行时会使用 StatusData_Overall.xlsx 中的完整显示数据。
        if (mainData == null)
        {
            mainData = new StatusMainData
            {
                StatusID = target.Type == BattleEntity.EntityType.Enemy ? 40002 : 40001,
                StatusID2 = statusID,
                StatusName = "倒地",
                Display = 1,
                Description = target.Type == BattleEntity.EntityType.Enemy
                    ? "失去首次行动"
                    : "本回合无法行动"
            };
        }

        // 普通状态以施加阶段作为计时原点：在哪个阶段破韧，就挂进哪个阶段的计时桶。
        // 下一次进入同一阶段时自然到期；主动起身/敌人跳过行动则提前 RemoveStatus。
        int currentPhase = BattleManager.Instance != null
            ? (int)BattleManager.Instance.CurrentPhase
            : 0;
        int timerPhase = currentPhase >= (int)TurnPhase.AllyPreTurn && currentPhase <= (int)TurnPhase.EnemyPostTurn
            ? currentPhase
            : (target.Type == BattleEntity.EntityType.Enemy
                ? (int)TurnPhase.AllyAction
                : (int)TurnPhase.EnemyAction);
        target.AddStatus(statusID, source ?? target, 1, timerPhase, 0, mainData);
        LogManager.Log(LogCategory.Status, $"{target.EntityID} 韧性归零，施加 {statusID}");
        return true;
    }

    /// <summary>自身阵营回合开始时调用；倒地者不恢复韧性。</summary>
    public static void RestoreAtTurnStart(BattleEntity target)
    {
        if (target == null || !target.IsAlive || target.MaxPoise < 0f) return;
        if (!IsKnockedDown(target))
            target.Poise = target.MaxPoise;
    }

    /// <summary>角色起身。仅成功扣除 10 AP 后才主动删除倒地状态。</summary>
    public static bool TryStandUp(BattleEntity target, ActionPointManager actionPoints)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Character) return false;
        if (!target.HasStatus(CharacterKnockdownStatusID)) return false;
        if (actionPoints == null || !actionPoints.ConsumeAP(StandUpAPCost)) return false;

        target.RemoveStatus(CharacterKnockdownStatusID);
        LogManager.Log(LogCategory.Action, $"{target.EntityID} 花费 {StandUpAPCost} AP 起身");
        return true;
    }

    /// <summary>敌人行动入口调用；返回 true 表示本次行动应被跳过。</summary>
    public static bool ConsumeEnemyFirstAction(BattleEntity target)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Enemy) return false;
        if (!target.RemoveStatus(EnemyKnockdownStatusID)) return false;

        LogManager.Log(LogCategory.Enemy, $"{target.EntityID} 因倒地跳过首次行动并起身");
        return true;
    }
}
