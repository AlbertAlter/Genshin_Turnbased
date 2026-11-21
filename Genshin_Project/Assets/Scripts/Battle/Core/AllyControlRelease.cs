public enum AllyControlClickResult { Ignored, Progressed, Released, InsufficientAP }

/// <summary>实际被选中的控制实例。移除后仍可读取本次点击进度，供表现收尾。</summary>
public readonly struct AllyControlState
{
    public readonly ReactionStateInstance Frozen;
    public readonly StatusInstance Airborne;
    public bool Exists => Frozen != null || Airborne != null;
    public int Clicks => Airborne != null ? Airborne.ControlReleaseClicks : Frozen?.ThawClicks ?? 0;
    public AllyControlState(ReactionStateInstance frozen, StatusInstance airborne)
    { Frozen = frozen; Airborne = airborne; }
}

/// <summary>我方冻结、击飞共用施加顺序。每份实例独立点击三次，最后一次支付10 AP。</summary>
public static class AllyControlRelease
{
    public const int RequiredClicks = FrozenThawInteraction.RequiredClicks;
    public const int APCost = FrozenReactionHandler.ThawAPCost;

    public static AllyControlState GetOldest(BattleEntity target)
    {
        if (target?.IsOnField != true || target.Side != BattleSide.Ally
            || target.Type != BattleEntity.EntityType.Character) return default;
        ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out var frozen);
        foreach (var status in target.GetStatusInstances(AirborneStatusHandler.CharacterStatusID))
        {
            if (!status.IsActive || status.DeathCleanupPending) continue;
            if (frozen == null || status.ApplyOrder < frozen.ApplyOrder) return new AllyControlState(null, status);
            break;
        }
        return new AllyControlState(frozen, null);
    }

    public static AllyControlClickResult Click(BattleEntity target, ActionPointManager ap, out AllyControlState handled)
    {
        handled = GetOldest(target);
        if (ap == null || !handled.Exists) return AllyControlClickResult.Ignored;
        if (handled.Frozen != null)
        {
            switch (FrozenThawInteraction.Click(target, ap))
            {
                case FrozenThawClickResult.Cracked: return AllyControlClickResult.Progressed;
                case FrozenThawClickResult.Thawed: return AllyControlClickResult.Released;
                case FrozenThawClickResult.InsufficientAP: return AllyControlClickResult.InsufficientAP;
                default: return AllyControlClickResult.Ignored;
            }
        }
        var airborne = handled.Airborne;
        if (airborne.ControlReleaseClicks < RequiredClicks - 1)
        {
            airborne.ControlReleaseClicks++;
            return AllyControlClickResult.Progressed;
        }
        if (!ap.ConsumeAP(APCost)) return AllyControlClickResult.InsufficientAP;
        airborne.ControlReleaseClicks = RequiredClicks;
        target.RemoveStatusInstance(airborne, "ManualControlRelease");
        return AllyControlClickResult.Released;
    }
}
