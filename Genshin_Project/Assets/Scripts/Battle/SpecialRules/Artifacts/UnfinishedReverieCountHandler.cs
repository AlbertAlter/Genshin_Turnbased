using System;

/// <summary>553遐思：初值0、上下限0/5、永久保留；只由阶段驱动改值，后台绝不减点。</summary>
public sealed class UnfinishedReverieCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_455302";
    private sealed class Timing { internal int StartTurn = -1, EndTurn = -1; }

    public override void OnCreated(StatusInstance status, object host)
    {
        RefreshingArtifactCount.RequireOwner(status, host);
        status.RuntimeValue = 0f;
        status.StackCount = 1;
        status.IsPermanent = true;
        status.RemainingPhaseCount = 0;
        status.CountRuntimeState = new Timing();
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var battle = BattleManager.Instance;
        var driver = ScriptHookContext.Current?.Status;
        if (battle == null || driver == null || driver.Caster != owner) return status;
        int phase = (int)battle.CurrentPhase;
        if ((phase != 1 || driver.StatusID2 != "ST_Artifact_455304")
            && (phase != 3 || driver.StatusID2 != "ST_Artifact_455305")) return status;
        var timing = (Timing)status.CountRuntimeState;
        if (phase == 1)
        {
            if (timing.StartTurn == battle.TurnCount) return status;
            timing.StartTurn = battle.TurnCount;
        }
        else
        {
            if (timing.EndTurn == battle.TurnCount) return status;
            timing.EndTurn = battle.TurnCount;
        }
        bool onField = owner.CharacterCtrl.IsActive;
        if (phase == 3 && !onField) status.RuntimeValue = 5f;
        else if (HasBurningEnemy(battle, owner)) status.RuntimeValue = Math.Min(5f, status.RuntimeValue + 1f);
        else if (onField) status.RuntimeValue = Math.Max(0f, status.RuntimeValue - 1f);
        // 阶段1后台无燃烧保持，阶段3后台直接补满；两个阶段各有自己的每回合额度。
        // 重施请求不重置永久性、不重新绑定两个驱动，也不把Count改成状态层数。
        return status;
    }

    private static bool HasBurningEnemy(BattleManager battle, BattleEntity owner)
    {
        if (battle.Field == null) return false;
        var slots = owner.Side == BattleSide.Ally ? battle.Field.EnemySlots : battle.Field.AllySlots;
        foreach (var slot in slots)
        {
            BattleEntity target = slot?.Occupant;
            if (target != null && target.IsAlive && target.Side != owner.Side
                && ReactionStateSystem.TryGetEntityState(target, ReactionType.Burning, out _)) return true;
        }
        return false; // 仅真实在场的存活燃烧目标；火附着不是燃烧，后台/离场敌人不算。
    }

    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}
