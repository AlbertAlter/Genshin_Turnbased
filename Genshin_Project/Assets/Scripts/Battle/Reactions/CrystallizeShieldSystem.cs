/// <summary>Applies ordinary crystallize shields without owning any global runtime registry.</summary>
public static class CrystallizeShieldSystem
{
    public const int ShieldDuration = 2;

    public static void ApplyPartyShield(
        BattleManager battle,
        string element,
        float value,
        int applicationPhase, BattleEntity creator = null)
    {
        if (battle == null || string.IsNullOrEmpty(element) || value <= 0f) return;
        using var resolution = BattleEventHub.BeginResolution();
        using var responses = BattleResolutionContext.BeginCollection();
        long batch = BattleEventHub.NextIdentity();
        var memory = StatusFormulaSnapshot.For(creator);

        foreach (CharacterBattleController ally in battle.Allies)
        {
            BattleEntity entity = ally != null ? ally.Entity : null;
            if (entity == null || !entity.IsOnField) continue;
            // 结晶基础盾量已含等级与精通；造盾加成读取实际反应来源，受盾者强效留在承伤时。
            float amount = BattleEntity.CalculateShieldAmount(value,
                memory?.Bonus("ShieldBonus", "", element, entity, ReactionType.Crystallize)
                    ?? creator?.GetStatusShieldBonus("ShieldBonus", "", "", element, entity, ReactionType.Crystallize) ?? 0f,
                memory?.Bonus("ShieldBonusFlat", "", element, entity, ReactionType.Crystallize)
                    ?? creator?.GetStatusShieldBonus("ShieldBonusFlat", "", "", element, entity, ReactionType.Crystallize) ?? 0f);
            entity.AddOrReplaceCrystallizeShield(
                amount,
                element,
                ShieldDuration,
                applicationPhase, batch, creator);
        }
    }
}
