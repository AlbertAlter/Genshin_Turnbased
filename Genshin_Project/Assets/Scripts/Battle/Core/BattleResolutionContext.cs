using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 源效果内所有 Hit、反应与状态派生共享 Effect，同一 ActionType 行动共享 Action。
/// 上下文属于当前异步调用链；等待表现期间不会泄漏到 UI 或下一次外部调用。
/// </summary>
public static class BattleResolutionContext
{
    public sealed class CounterScope
    {
        private readonly Dictionary<(StatusInstance, string), int> _counts
            = new Dictionary<(StatusInstance, string), int>();

        internal int Count(StatusInstance status, string key)
            => _counts.TryGetValue((status, key), out int count) ? count : 0;

        internal void Collect(StatusInstance status, string key)
            => _counts[(status, key)] = Count(status, key) + 1;
    }

    private sealed class RestoreScope : IDisposable
    {
        private Action _restore;
        public RestoreScope(Action restore) { _restore = restore; }
        public void Dispose()
        {
            Action restore = _restore;
            _restore = null;
            restore?.Invoke();
        }
    }

    public sealed class PreparedTrigger : IDisposable
    {
        private Func<Task> _execute;
        private Action _release;
        public PreparedTrigger(Func<Task> execute, Action release)
        {
            _execute = execute;
            _release = release;
        }
        public async Task Execute()
        {
            Func<Task> execute = _execute;
            _execute = null;
            try { if (execute != null) await execute(); }
            finally { Dispose(); }
        }
        public void Dispose()
        {
            _execute = null;
            Action release = _release;
            _release = null;
            release?.Invoke();
        }
    }

    private sealed class Candidate
    {
        public long ApplyOrder;
        public int Sequence;
        public Func<PreparedTrigger> Prepare;
    }

    /// <summary>先按施加顺序确认整层请求并预留额度，再依次执行；子层在当前请求内结清。</summary>
    private sealed class Collection : IDisposable
    {
        internal readonly List<Candidate> Candidates = new List<Candidate>();
        internal readonly List<Action> AfterResponses = new List<Action>();
        private readonly Collection _previous;
        private readonly IDisposable _effect;
        private bool _disposed;

        internal Collection()
        {
            _effect = BeginEffect();
            _previous = _collection;
            _collection = this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _collection = _previous;
            var prepared = new List<PreparedTrigger>();
            try
            {
                Candidates.Sort((a, b) => a.ApplyOrder != b.ApplyOrder
                    ? a.ApplyOrder.CompareTo(b.ApplyOrder) : a.Sequence.CompareTo(b.Sequence));
                foreach (Candidate candidate in Candidates)
                {
                    PreparedTrigger trigger = candidate.Prepare();
                    if (trigger != null) prepared.Add(trigger);
                }
                if (prepared.Count == 0 && AfterResponses.Count == 0) return;
                // 先确认整层额度，再挂起等待表现。候选不会在恢复时重新求值。
                BattleExecution.Enqueue(async () =>
                {
                    Settlement previous = SettlementSlot.Value;
                    Settlement state = previous ?? new Settlement();
                    SettlementSlot.Value = state;
                    state.Depth++;
                    bool completed = false;
                    try
                    {
                        foreach (PreparedTrigger trigger in prepared)
                        {
                            BattleExecution.ThrowIfCancelled();
                            await BattleExecution.RunInline(trigger.Execute);
                        }
                        foreach (Action completion in AfterResponses)
                        {
                            BattleExecution.ThrowIfCancelled();
                            completion();
                            await BattleExecution.Flush();
                        }
                        completed = true;
                    }
                    finally
                    {
                        foreach (PreparedTrigger trigger in prepared) trigger.Dispose();
                        state.Depth--;
                        SettlementSlot.Value = previous;
                        if (state.Depth == 0)
                        {
                            Action[] callbacks = state.Callbacks.ToArray();
                            state.Callbacks.Clear();
                            if (completed && !BattleExecution.IsCancelled)
                                foreach (Action callback in callbacks) callback();
                        }
                    }
                }, () => { foreach (PreparedTrigger trigger in prepared) trigger.Dispose(); });
            }
            catch
            {
                foreach (PreparedTrigger trigger in prepared) trigger.Dispose();
                throw;
            }
            finally { _effect.Dispose(); }
        }
    }

    private sealed class Settlement
    {
        public int Depth;
        public readonly List<Action> Callbacks = new List<Action>();
    }
    private static readonly AsyncLocal<CounterScope> ActionSlot = new AsyncLocal<CounterScope>();
    private static readonly AsyncLocal<CounterScope> EffectSlot = new AsyncLocal<CounterScope>();
    private static readonly AsyncLocal<Collection> CollectionSlot = new AsyncLocal<Collection>();
    private static readonly AsyncLocal<int> DepthSlot = new AsyncLocal<int>();
    private static readonly AsyncLocal<Settlement> SettlementSlot = new AsyncLocal<Settlement>();
    public static CounterScope CurrentAction { get => ActionSlot.Value; private set => ActionSlot.Value = value; }
    public static CounterScope CurrentEffect { get => EffectSlot.Value; private set => EffectSlot.Value = value; }
    private static Collection _collection { get => CollectionSlot.Value; set => CollectionSlot.Value = value; }
    private static int _triggerDepth { get => DepthSlot.Value; set => DepthSlot.Value = value; }
    private const int MaxTriggerDepth = 128;

    public sealed class Snapshot
    {
        internal CounterScope Action, Effect;
        public IDisposable Enter()
        {
            CounterScope previousAction = CurrentAction, previousEffect = CurrentEffect;
            CurrentAction = Action;
            CurrentEffect = Effect;
            return new RestoreScope(() => { CurrentAction = previousAction; CurrentEffect = previousEffect; });
        }
    }
    public static Snapshot Capture() => new Snapshot { Action = CurrentAction, Effect = CurrentEffect };

    public static string ActionKey(StatusActionData action)
        => !string.IsNullOrEmpty(action.ActionKey) ? action.ActionKey
            : $"legacy:{action.StatusID2}:{action.ActionType}:{action.Param1}:{action.ScriptHook}";

    public static IDisposable BeginEffect()
    {
        CounterScope previous = CurrentEffect;
        if (CurrentEffect == null) CurrentEffect = new CounterScope();
        return new RestoreScope(() => CurrentEffect = previous);
    }

    // 跨玩家目标选择仅保存 CounterScope；每次同步推进时 Enter，退出后立即恢复。
    public static IDisposable EnterAction(CounterScope action)
    {
        CounterScope previous = CurrentAction;
        if (CurrentAction == null) CurrentAction = action;
        return new RestoreScope(() => CurrentAction = previous);
    }

    /// <summary>
    /// 正在记录同一批次时合并收集入口（伤害、反应、治疗等），不提前执行内层通知。
    /// 批次开始执行前会脱离收集器，因此响应产生的子层仍独立收集并深度优先结清。
    /// </summary>
    public static IDisposable BeginCollection()
        => _collection == null ? (IDisposable)new Collection() : new RestoreScope(null);

    /// <summary>保留现有死亡声明晚于本层普通响应的边界。</summary>
    public static void AfterResponses(Action action)
    {
        if (_collection != null) _collection.AfterResponses.Add(action);
        else action();
    }

    /// <summary>嵌套响应和本层收尾全部完成后再检查胜负。</summary>
    public static void AfterSettlement(Action action)
    {
        if (SettlementSlot.Value != null && SettlementSlot.Value.Depth > 0) SettlementSlot.Value.Callbacks.Add(action);
        else action();
    }

    public static bool Submit(long applyOrder, Func<PreparedTrigger> prepare)
    {
        if (_collection != null)
        {
            _collection.Candidates.Add(new Candidate
            {
                ApplyOrder = applyOrder,
                Sequence = _collection.Candidates.Count,
                Prepare = prepare
            });
            return true; // 已提交候选；是否成功收集在本层统一判定。
        }
        using (BeginEffect())
        {
            PreparedTrigger trigger = prepare();
            if (trigger == null) return false;
            BattleExecution.Enqueue(trigger.Execute, trigger.Dispose);
            return true;
        }
    }

    public static bool LimitReached(StatusInstance status, StatusActionData action)
    {
        string key = ActionKey(action);
        return (action.MaxTimePerEffect > 0 && CurrentEffect != null
                && CurrentEffect.Count(status, key) >= action.MaxTimePerEffect)
            || (action.MaxTimePerAction > 0 && CurrentAction != null
                && CurrentAction.Count(status, key) >= action.MaxTimePerAction);
    }

    public static bool TryCollect(StatusInstance status, StatusActionData action)
    {
        if (LimitReached(status, action)) return false;
        string key = ActionKey(action);
        if (action.MaxTimePerEffect > 0) CurrentEffect?.Collect(status, key);
        if (action.MaxTimePerAction > 0) CurrentAction?.Collect(status, key);
        return true;
    }

    public static bool HasApplicableLimit(StatusActionData action)
        => (action.MaxTimePerEffect > 0 && CurrentEffect != null)
            || (action.MaxTimePerAction > 0 && CurrentAction != null);

    public static IDisposable EnterTrigger()
    {
        if (_triggerDepth >= MaxTriggerDepth)
            throw new InvalidOperationException("状态连锁超过安全深度，请检查未限制的循环触发。");
        _triggerDepth++;
        return new RestoreScope(() => _triggerDepth--);
    }
}
