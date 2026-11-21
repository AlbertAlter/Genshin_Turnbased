using System;
using System.Threading;

/// <summary>
/// 非自然移除的事件行动。先完成移除事实，再复用事件队列收集/限次/执行；
/// 只保留这份已移除实例的执行资格，不重新激活状态，也不放行死亡的来源。
/// </summary>
public static class StatusRemovalActions
{
    private static readonly AsyncLocal<int> CleanupDepth = new AsyncLocal<int>();

    internal sealed class Request
    {
        internal readonly StatusInstance Status;
        internal readonly object Host;
        internal readonly ScriptHookContext Context;
        private readonly long sourceDeathVersion;
        private readonly int sessionVersion;
        private readonly bool sourceAvailableAtRemoval;
        internal Request(StatusInstance status, object host, BattleDeclaration cause)
        {
            Status = status; Host = host;
            sourceDeathVersion = status.Caster?.EquipmentDeathVersion ?? 0;
            sessionVersion = BattleDeathResolution.SessionVersion;
            sourceAvailableAtRemoval = status.Caster?.IsAlive == true && EquipmentStatusLifetime.CanExecute(status);
            Context = new ScriptHookContext { Caster = status.Caster, Status = status,
                StatusHost = host, Target = host as BattleEntity, Declaration = cause };
        }
        internal bool CanExecute => sourceAvailableAtRemoval && !Status.IsActive && Status.Caster?.IsAlive == true
            && BattleManager.Instance?.IsBattleOver != true
            && Status.Caster.EquipmentDeathVersion == sourceDeathVersion
            && BattleDeathResolution.SessionVersion == sessionVersion
            && EquipmentStatusLifetime.CanExecute(Status) && CleanupDepth.Value == 0;
    }

    private sealed class Scope : IDisposable
    {
        private Action release;
        internal Scope(Action release) { this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }

    internal static IDisposable BeginNaturalEnd(StatusInstance status, string actionType)
    {
        if (status == null || actionType != "OnEnd") return null;
        status.NaturalEndDepth++;
        return new Scope(() => status.NaturalEndDepth--);
    }

    /// <summary>容器释放及阶段退出仅清理实例和绑定，跳过游戏内的销毁行动。</summary>
    internal static IDisposable SuppressForCleanup()
    {
        CleanupDepth.Value++;
        return new Scope(() => CleanupDepth.Value--);
    }

    internal static void OnRemoved(StatusInstance status, object host, string reason)
    {
        if (status == null || status.IsActive || status.RemovalActionCollected) return;
        status.RemovalActionCollected = true;
        if (reason == "Expired" || status.NaturalEndDepth > 0 || CleanupDepth.Value > 0) return;
        var cause = BattleEventHub.Current;
        if (cause?.Kind != BattleEventKind.StatusRemoved
            || cause.Get<StatusEventData>()?.InstanceId != status.ApplyOrder) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        var request = new Request(status, host, cause);
        using var collection = BattleResolutionContext.BeginCollection();
        foreach (var action in actions)
        {
            if (action.ActionType != "OnDestroy") continue;
            if (status.Caster?.CharacterCtrl != null)
                status.Caster.CharacterCtrl.TryExecuteRemovedStatusAction(request, action);
            else if (status.Caster?.EnemyCtrl != null)
                status.Caster.EnemyCtrl.TryExecuteRemovedStatusAction(request, action);
        }
    }
}
