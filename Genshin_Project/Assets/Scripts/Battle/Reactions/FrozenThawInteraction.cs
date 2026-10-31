public enum FrozenThawClickResult { Ignored, Cracked, Thawed, InsufficientAP }

/// <summary>Shared manual thaw interaction. Progress belongs to the actual freeze instance.</summary>
public static class FrozenThawInteraction
{
    public const int RequiredClicks = 3;

    public static FrozenThawClickResult Click(BattleEntity target, ActionPointManager ap)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Character || target.Side != BattleSide.Ally
            || target.CurrentHP <= 0 || ap == null
            || !ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out var frozen))
            return FrozenThawClickResult.Ignored;
        if (frozen.ThawClicks < RequiredClicks - 1)
        {
            frozen.ThawClicks++;
            return FrozenThawClickResult.Cracked;
        }
        // Reuse the atomic AP payment + real freeze removal. Failure keeps progress at two.
        if (!FrozenReactionHandler.TryThaw(target, ap)) return FrozenThawClickResult.InsufficientAP;
        frozen.ThawClicks = RequiredClicks;
        return FrozenThawClickResult.Thawed;
    }
}
