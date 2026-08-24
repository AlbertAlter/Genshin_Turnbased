/// <summary>Applies ordinary crystallize shields without owning any global runtime registry.</summary>
public static class CrystallizeShieldSystem
{
    public const int ShieldDuration = 2;

    public static void ApplyPartyShield(
        BattleManager battle,
        string element,
        float value,
        int applicationPhase)
    {
        if (battle == null || string.IsNullOrEmpty(element) || value <= 0f) return;

        foreach (CharacterBattleController ally in battle.Allies)
        {
            BattleEntity entity = ally != null ? ally.Entity : null;
            if (entity == null || !entity.IsAlive) continue;
            entity.AddOrReplaceCrystallizeShield(
                value,
                element,
                ShieldDuration,
                applicationPhase);
        }
    }
}
