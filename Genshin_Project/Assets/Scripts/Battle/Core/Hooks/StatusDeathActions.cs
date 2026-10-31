using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>在真实死亡时固定宿主上的候选；死亡行动完成后才允许死亡清理。</summary>
public static class StatusDeathActions
{
    internal sealed class Candidate
    {
        internal StatusInstance Status;
        internal object Host;
        internal StatusActionData Action;
    }

    internal sealed class Request
    {
        internal readonly StatusInstance Status;
        internal readonly object Host;
        internal readonly ScriptHookContext Context;
        private readonly BattleEntity dead;
        private readonly long deathVersion;
        private readonly int sessionVersion;

        internal Request(Candidate candidate, DamageResolvedEvent damage, BattleDeclaration declaration, long targetDeathVersion)
        {
            Status = candidate.Status; Host = candidate.Host; dead = damage.Target;
            deathVersion = targetDeathVersion;
            sessionVersion = BattleDeathResolution.SessionVersion;
            Context = new ScriptHookContext { Caster = Status.Caster, Status = Status, StatusHost = Host,
                Target = dead, DamageEvent = damage, Declaration = declaration, EventHookName = "OnDeath",
                HitEffectID = damage.Source?.SourceEffectID };
        }

        internal bool CanExecute => sessionVersion == BattleDeathResolution.SessionVersion
            && !BattleExecution.IsCancelled && !dead.IsAlive && dead.EquipmentDeathVersion == deathVersion
            && Status.IsActive && StatusHostAccess.Contains(Host, Status)
            && (Status.EquipmentSource == null || Status.EquipmentSource.IsAvailable
                || Status.EquipmentSource.Owner == dead);

        internal IDisposable Enter()
        {
            var previous = Current.Value;
            Current.Value = this;
            return new Scope(() => Current.Value = previous);
        }
    }

    private sealed class Scope : IDisposable
    {
        private Action release;
        internal Scope(Action release) { this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
    private static readonly AsyncLocal<Request> Current = new AsyncLocal<Request>();

    // 只放行当前死亡行动自己的来源，不让死亡装备的其他候选恢复执行资格。
    internal static bool Allows(StatusInstance status)
        => status != null && Current.Value?.Status == status && Current.Value.CanExecute;

    internal static List<Candidate> Capture(BattleEntity dead)
    {
        var result = new List<Candidate>();
        var dm = DataManager.Instance;
        if (dead == null || dm == null) return result;
        void Collect(object host, IEnumerable<StatusInstance> statuses)
        {
            foreach (var status in statuses)
                if (status != null && status.IsActive && dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions))
                    foreach (var action in actions)
                        if (action.ActionType == "OnDeath") result.Add(new Candidate { Status = status, Host = host, Action = action });
        }
        Collect(dead, dead.GetStatusList());
        if (dead.Position?.Occupant == dead) Collect(dead.Position, dead.Position.StatusList);
        var team = BattleManager.Instance?.GetTeamFor(dead);
        if (team != null) Collect(team, team.GetStatusList());
        return result;
    }

    internal static void Notify(IReadOnlyList<Candidate> candidates, DamageResolvedEvent damage, BattleDeclaration declaration,
        long targetDeathVersion)
    {
        foreach (var candidate in candidates)
        {
            var request = new Request(candidate, damage, declaration, targetDeathVersion);
            if (request.Status.Caster?.CharacterCtrl != null)
                request.Status.Caster.CharacterCtrl.TryExecuteDeathStatusAction(request, candidate.Action);
            else if (request.Status.Caster?.EnemyCtrl != null)
                request.Status.Caster.EnemyCtrl.TryExecuteDeathStatusAction(request, candidate.Action);
        }
    }
}
