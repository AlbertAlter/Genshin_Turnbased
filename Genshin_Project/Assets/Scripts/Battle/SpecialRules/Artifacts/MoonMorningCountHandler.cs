/// <summary>560晓歌：永久隐藏0/1资格；切出获1，上场不清，到我方阶段3在场才清0。</summary>
public sealed class MoonMorningCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_456002";
    private sealed class Timing { internal int EndTurn = -1; }

    internal static void RegisterHooks()
    {
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var ally in battle.Allies)
            {
                if (ally?.Entity == null) continue;
                foreach (var status in ally.Entity.GetStatusInstances(StatusID))
                    if (status.IsActive) status.RuntimeValue = ally.IsActive ? 0f : 1f;
            }
        });
        BattleSpecialRules.RegisterNumberedCondition("456004", (argument, context) =>
        {
            var team = BattleManager.Instance?.TeamBonuses;
            BattleEntity owner = context?.Status?.Caster ?? context?.Caster;
            return team != null && !team.IsDisposed && team.Contains(owner) && team.Moonsign.Level >= 2;
        });
    }

    public override void OnCreated(StatusInstance status, object host)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var battle = BattleManager.Instance;
        // Init逐个构筑时还未决定布局：先0，完整编队建立后的BattleReady才按真实位置初始化。
        status.RuntimeValue = battle != null && battle.IsBattleRunning && battle.TeamBonuses != null
            && !battle.TeamBonuses.IsDisposed && battle.TeamBonuses.Contains(owner)
            && !owner.CharacterCtrl.IsActive ? 1f : 0f;
        status.StackCount = 1;
        status.IsPermanent = true;
        status.RemainingPhaseCount = 0;
        status.CountRuntimeState = new Timing();
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        if (application.AddInPhase == 1)
        {
            // 使用原SwitchOut声明，不读响应时可能已经再次切回的前后台位置。
            var change = ScriptHookContext.Current?.Declaration?.Get<SwitchEventData>();
            if (change?.Outgoing == owner) status.RuntimeValue = 1f;
        }
        else if (application.AddInPhase == 3)
        {
            var battle = BattleManager.Instance;
            var driver = ScriptHookContext.Current?.Status;
            var timing = (Timing)status.CountRuntimeState;
            if (battle != null && (int)battle.CurrentPhase == 3
                && driver?.StatusID2 == "ST_Artifact_456005" && driver.Caster == owner
                && timing.EndTurn != battle.TurnCount)
            {
                timing.EndTurn = battle.TurnCount;
                status.RuntimeValue = owner.CharacterCtrl.IsActive ? 0f : 1f;
            }
        }
        return status; // 0仍保留；不续时、不叠层、不重建绑定驱动。
    }

    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}
