public sealed class NightSoulBurstEventData : BattleEventData
{
    public int ReadyTurn { get; }
    public NightSoulBurstEventData(BattleEntity actor, int readyTurn) : base(BattleEventKind.NightSoulBurst, actor)
    { ReadyTurn = readyTurn; }
}

public sealed class NightSoulBurstHandler
{
    private readonly TeamBonusSystem team;
    private readonly TeamBonusParameters data;
    private readonly int count;
    public int ReadyTurn { get; private set; } = 1;
    public NightSoulBurstHandler(TeamBonusSystem team, TeamBonusParameters data)
    { this.team = team; this.data = data; count = team.CountMembers(data.CharacterIDs()); }
    public void OnDamage(BattleEventHeader header, DamageEventData hit)
    {
        BattleEntity actor = hit.Source.SourceEntity;
        if (!data.Enabled || count <= 0 || team.Turn < ReadyTurn || !team.Contains(actor)
            || hit.Subject == null || hit.Subject.Side != BattleSide.Enemy || hit.DamageBeforeShield <= 0f
            // 伤害性质由Damage入口确定并随本次结果传递，不从角色/效果ID反推。
            || !hit.IsNightSoulDamage) return;
        int cooldown = data.Int(count >= 3 ? "Cooldown3" : count == 2 ? "Cooldown2" : "Cooldown1");
        ReadyTurn = team.Turn + cooldown;
        // 先写共用冷却，再发布声明；各角色通过NightSoulBurst(All)自行响应。
        BattleEventHub.Publish(new NightSoulBurstEventData(actor, ReadyTurn), causes: new[] { header.EventId });
        LogManager.Log(LogCategory.TeamBonus, "夜魂迸发：source=" + actor.EntityID + " readyTurn=" + ReadyTurn);
    }
}
