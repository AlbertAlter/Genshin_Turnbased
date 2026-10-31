using System.Collections.Generic;

public interface IBattleUiHitEffects
{
    bool Ready { get; }
    bool PlayHit(HitEffectWeapon weapon, bool heavy, string element, BattleUiCardBinding source,
        IReadOnlyList<BattleUiCardBinding> targets, int hit, int hitCount, uint seed,
        HitEffectTextureStyle.PlaybackMode? modeOverride = null);
    bool PlayScheduledHit(HitEffectWeapon weapon, bool heavy, string element, BattleUiCardBinding source,
        IReadOnlyList<BattleUiCardBinding> targets, int hit, int hitCount, uint seed, bool launchOnly, float flightSeconds,
        long sessionID, long eventID);
    void Clear();
}

public interface IBattleUiShields
{
    bool Ready { get; }
    bool SetShield(BattleSide side, int slot, bool visible, string element = "None");
    bool PlaySweep(BattleSide side, int slot, bool loop = false);
}
