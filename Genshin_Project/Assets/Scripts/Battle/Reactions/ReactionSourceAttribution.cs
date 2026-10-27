/// <summary>元素来源按真实实例识别；旧数字ID仅在本队唯一匹配时兼容，不将同ID敌人当作队友。</summary>
public static class ReactionSourceAttribution
{
    public static BattleEntity ResolvePartySource(TeamBonusSystem team, BattleEntity source, int sourceID)
    {
        if (team == null || team.IsDisposed) return null;
        if (source != null) return team.Contains(source) ? source : null;
        if (sourceID == 0) return null;
        BattleEntity found = null;
        foreach (BattleEntity member in team.Members)
            if (member.EntityID == sourceID)
            {
                if (found != null) return null;
                found = member;
            }
        return found;
    }

    public static BattleEntity ResolvePartySource(TeamBonusSystem team, ElementalAura aura)
        => aura == null ? null : ResolvePartySource(team, aura.SourceEntity, aura.SourceEntityID);

    public static BattleEntity ResolvePartySource(TeamBonusSystem team, ElementPoolReading pool)
        => pool == null ? null : ResolvePartySource(team, pool.SourceEntity, pool.SourceEntityID);

    public static BattleEntity ResolvePartySource(TeamBonusSystem team, ReactionSourceSnapshot snapshot)
        => snapshot == null ? null : ResolvePartySource(team, snapshot.SourceEntity, snapshot.SourceEntityID);
}
