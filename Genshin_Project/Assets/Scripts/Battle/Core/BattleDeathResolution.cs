using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 死亡仍由HitBatch即时提交；这里只保存当前行为/独立状态效果链的死亡候选。
/// 不替代原响应队列：复活请求仍通过BattleExecution及正式状态行动执行，最后才检查胜负。
/// </summary>
public static class BattleDeathResolution
{
    public sealed class Death
    {
        public BattleEntity Target { get; internal set; }
        public long BatchOrder { get; internal set; }
        public long DamageEventID { get; internal set; }
        public BattleDeclaration Declaration { get; internal set; }
        internal BattleResolutionContext.Snapshot Rules;
        internal long TargetDeathVersion;
        internal List<StatusDeathActions.Candidate> Actions;
        internal bool ActionsNotified;
    }

    public sealed class Revival
    {
        internal readonly List<Death> Items = new List<Death>();
        internal Func<Revival, Task> Execute;
        public StatusInstance Source { get; internal set; }
        public IReadOnlyList<Death> Candidates => Items.AsReadOnly();
        public BattleEntity SelectedTarget { get; set; }
        public bool Succeeded { get; internal set; }
        internal long SourceDeathVersion;
    }

    private sealed class Window
    {
        internal int Generation;
        internal bool Standalone, Queued, Closing, Closed, NeedsEndCheck;
        internal long NextBatch;
        internal readonly Dictionary<long, long> Batches = new Dictionary<long, long>();
        internal readonly Dictionary<long, Death> Deaths = new Dictionary<long, Death>();
        internal readonly Dictionary<StatusInstance, Revival> Revivals = new Dictionary<StatusInstance, Revival>();
        internal readonly List<Action> Cleanup = new List<Action>();
    }

    private sealed class Restore : IDisposable
    {
        private Action action;
        internal Restore(Action restore) { action = restore; }
        public void Dispose() { var restore = action; action = null; restore?.Invoke(); }
    }

    private static readonly AsyncLocal<Window> Current = new AsyncLocal<Window>();
    private static readonly AsyncLocal<Revival> CurrentRevival = new AsyncLocal<Revival>();
    private static int generation;
    internal static int SessionVersion => generation;
    private static bool Valid(Window window) => window != null && window.Generation == generation && !window.Closed;

    public sealed class Snapshot
    {
        private readonly Window window;
        private readonly Revival revival;
        internal Snapshot() { window = Current.Value; revival = CurrentRevival.Value; }
        public IDisposable Enter()
        {
            var previous = Current.Value;
            var previousRevival = CurrentRevival.Value;
            Current.Value = Valid(window) ? window : null;
            CurrentRevival.Value = Valid(window) ? revival : null;
            return new Restore(() => { Current.Value = previous; CurrentRevival.Value = previousRevival; });
        }
    }

    public static Snapshot Capture() => new Snapshot();

    /// <summary>嵌套状态/技能归入外层行为；独立调用则拥有自己的完整效果链边界。</summary>
    public static async Task RunAsync(Func<Task> execute)
    {
        if (Valid(Current.Value)) { await execute(); return; }
        var previous = Current.Value;
        var window = new Window { Generation = generation };
        Current.Value = window;
        try
        {
            await execute();
            await BattleExecution.Flush();
            if (!BattleExecution.IsCancelled && Valid(window)) await CompleteAsync(window);
        }
        finally
        {
            window.Closed = true;
            window.Revivals.Clear(); window.Deaths.Clear(); window.Cleanup.Clear();
            Current.Value = previous;
        }
    }

    /// <summary>无技能/状态容器的直接伤害，退回本次伤害及其全部子响应的边界。</summary>
    internal static IDisposable EnterDamageBatch(bool hasDeaths)
    {
        if (!hasDeaths || Valid(Current.Value)) return new Restore(null);
        var previous = Current.Value;
        Current.Value = new Window { Generation = generation, Standalone = true };
        return new Restore(() => Current.Value = previous);
    }

    internal static void RecordDeaths(IReadOnlyList<DamageResolvedEvent> values)
    {
        var window = Current.Value;
        if (!Valid(window)) return;
        // 此时普通响应尚未执行；顺序来自真实HP提交批次，不按较晚的Death回调遍历顺序猜。
        long fallback = 0;
        foreach (var value in values)
        {
            long id = value.Declaration?.Header.EventId ?? 0;
            if (!value.CausedDeath || id == 0 || window.Deaths.ContainsKey(id)) continue;
            long batch = value.RuntimeHitID != 0 ? value.RuntimeHitID
                : value.HitBatchID != 0 ? -value.HitBatchID : fallback != 0 ? fallback : (fallback = BattleEventHub.NextIdentity());
            if (!window.Batches.TryGetValue(batch, out long order)) window.Batches.Add(batch, order = ++window.NextBatch);
            window.Deaths.Add(id, new Death { Target = value.Target, BatchOrder = order, DamageEventID = id,
                Rules = BattleResolutionContext.Capture(), TargetDeathVersion = value.Target.EquipmentDeathVersion,
                Actions = StatusDeathActions.Capture(value.Target) });
        }
    }

    internal static void NotifyDeathActions(DamageResolvedEvent damage, BattleDeclaration declaration)
    {
        var window = Current.Value;
        long id = damage.Declaration?.Header.EventId ?? 0;
        if (!Valid(window) || !window.Deaths.TryGetValue(id, out var death) || death.ActionsNotified) return;
        death.ActionsNotified = true;
        death.Declaration = declaration;
        StatusDeathActions.Notify(death.Actions, damage, declaration, death.TargetDeathVersion);
    }

    /// <summary>固定待清理实例；只延后死亡清理，不保护被主动移除或替换的状态。</summary>
    internal static void RemoveAfterDeathActions(object host, StatusInstance status, string reason)
    {
        if (status == null || !status.IsActive || status.DeathCleanupPending) return;
        var window = Current.Value;
        if (!Valid(window)) { StatusHostAccess.Remove(host, status, reason); return; }
        MarkPendingCleanup(status, new HashSet<StatusInstance>());
        var cause = BattleEventHub.Current;
        var rules = BattleResolutionContext.Capture();
        window.Cleanup.Add(() => {
            using (rules.Enter())
            using (BattleEventHub.Enter(cause)) StatusHostAccess.Remove(host, status, reason);
        });
    }

    private static void MarkPendingCleanup(StatusInstance status, HashSet<StatusInstance> seen)
    {
        if (status == null || !status.IsActive || !seen.Add(status)) return;
        status.DeathCleanupPending = true;
        // 子状态仍由原绑定移除路径清理；这里只阻止它在父死亡清理前继续执行普通候选。
        if (status.BoundStatuses != null)
            foreach (var child in status.BoundStatuses) MarkPendingCleanup(child, seen);
    }

    /// <summary>只接受本窗口已提交的死亡声明；同一来源合并候选，不按队友分别消耗次数。</summary>
    public static void RequestRevival(StatusInstance source, BattleDeclaration declaration, Func<Revival, Task> execute)
    {
        var window = Current.Value;
        if (!Valid(window) || source == null || declaration?.Kind != BattleEventKind.Death) return;
        Death candidate = null;
        foreach (long cause in declaration.Header.CauseEventIds)
            if (window.Deaths.TryGetValue(cause, out var death) && death.Target == declaration.Data.Subject)
            { candidate = death; break; }
        if (candidate == null) return;
        candidate.Declaration = declaration;
        long sourceVersion = source.Caster?.EquipmentDeathVersion ?? 0;
        if (!window.Revivals.TryGetValue(source, out var request) || request.SourceDeathVersion != sourceVersion)
            window.Revivals[source] = request = new Revival { Source = source, Execute = execute, SourceDeathVersion = sourceVersion };
        if (!request.Items.Contains(candidate)) request.Items.Add(candidate);
    }

    internal static void ReportRevival(StatusInstance source, BattleEntity target)
    {
        var request = CurrentRevival.Value;
        if (request != null && request.Source == source && request.SelectedTarget == target) request.Succeeded = true;
    }

    public static bool DeferBattleEndCheck()
    {
        var window = Current.Value;
        if (!Valid(window)) return false;
        window.NeedsEndCheck = true;
        return true;
    }

    internal static void AfterDamageSettlement()
    {
        var window = Current.Value;
        if (!Valid(window)) { BattleManager.Instance?.CheckBattleEnd(); return; }
        window.NeedsEndCheck = true;
        if (!window.Standalone || window.Queued || window.Closing) return;
        window.Queued = true;
        BattleExecution.Enqueue(() => CompleteAsync(window));
    }

    private static async Task CompleteAsync(Window window)
    {
        if (!Valid(window) || window.Closing) return;
        window.Closing = true;
        try
        {
            while ((window.Cleanup.Count > 0 || window.Revivals.Count > 0) && Valid(window) && !BattleExecution.IsCancelled)
            {
                // 所有死亡行动及其子响应已经Flush；先清旧实例，再接受正式复活。
                // 清理响应如产生新的死亡，继续处理它的行动与清理，不能提前判胜负。
                if (window.Cleanup.Count > 0)
                {
                    var cleanup = window.Cleanup.ToArray();
                    window.Cleanup.Clear();
                    await BattleExecution.RunInline(() => {
                        using (BattleResolutionContext.BeginCollection())
                            foreach (var remove in cleanup) remove();
                        return Task.CompletedTask;
                    });
                    continue;
                }
                var requests = new List<Revival>(window.Revivals.Values);
                window.Revivals.Clear();
                requests.Sort((left, right) => left.Source.ApplyOrder.CompareTo(right.Source.ApplyOrder));
                foreach (var request in requests)
                {
                    if (!Valid(window) || BattleExecution.IsCancelled) return;
                    var previous = CurrentRevival.Value;
                    CurrentRevival.Value = request;
                    try { await BattleExecution.RunInline(() => request.Execute(request)); }
                    finally { CurrentRevival.Value = previous; }
                }
            }
            if (!Valid(window) || BattleExecution.IsCancelled) return;
            window.Closed = true;
            if (window.NeedsEndCheck) BattleManager.Instance?.CheckBattleEnd();
        }
        finally
        {
            window.Closed = true;
            window.Revivals.Clear(); window.Deaths.Clear(); window.Cleanup.Clear();
        }
    }

    internal static void ResetSession()
    {
        generation++;
        Current.Value = null; CurrentRevival.Value = null;
    }
}
