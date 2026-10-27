public partial class BattleEntity
{
    public float GetTeamDamageBonus(string element)
    {
        TeamBonusSystem team = BattleManager.Instance?.TeamBonuses;
        if (team == null || !team.Contains(this)) return 0f;
        return team.Resonance.DamageBonus(this)
            + ((element == "Cryo" || element == "Electro") ? team.Stellar.ReadElementDamageBonus() : 0f);
    }
    public float GetTeamResistanceBonus(string element)
    {
        TeamBonusSystem team = BattleManager.Instance?.TeamBonuses;
        if (team == null) return 0f;
        return team.Resonance.ResistanceBonus(this, element)
            + (Side == BattleSide.Enemy && element == "Physical"
                ? team.Stellar.ReadPhysicalResistanceBonus() : 0f);
    }
    public float GetTeamShieldStrength() => BattleManager.Instance?.TeamBonuses?.Resonance.ShieldBonus(this) ?? 0f;
    public float GetTeamLunarDamageBonus()
    {
        TeamBonusSystem team = BattleManager.Instance?.TeamBonuses;
        return team != null && team.Contains(this) ? team.Moonsign.DamageBonus : 0f;
    }
}
