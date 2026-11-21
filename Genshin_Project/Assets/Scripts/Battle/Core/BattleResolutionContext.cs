using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Action仅由角色/敌人主动技能建立，整条后续链共享；独立状态不创建Action。
/// Execution按实际调用的连续执行段计数；状态被其他调用打断后再调用其效果时建立新额度。
/// Effect仍覆盖源效果的全部Hit与派生；Chain只保留因果分组，不提供第三种配表限次。
/// 上下文属于当前异步调用链；等待表现期间不会泄漏到 UI 或下一次外部调用。
/// </summary>
public static class BattleResolutionContext
{
    public sealed class CounterScope
    {
        /// <summary>此状态执行/独立状态链的实例；主动技能和Effect范围为null。</summary>
        public StatusInstance IndependentStatus { get; }
        public CounterScope() { }
        internal CounterScope(StatusInstance status) { IndependentStatus = status; }
        public long ScopeId { get; } = BattleEventHub.NextIdentity();
        private readonly Dictionary<(StatusInstance, string), int> _counts
            = new Dictionary<(StatusInstance, string), int>();
        // 由根效果范围持有，随该范围释放；不保存跨战斗或全场历史 hit。
        private readonly Dictionary<(long, StatusInstance, string), int> _hitCounts
            = new Dictionary<(long, StatusInstance, string), int>();
        internal int StatusDepth;
        internal bool StatusFailed;
        // 同一次状态调用可有多段Execution；深度与收尾属于整次调用，不随额度分段提前结束。
        internal CounterScope StatusCallRoot;
        internal CounterScope CurrentStatusSegment;
        internal ExecutionChain StatusCallChain;
        internal bool StatusCallStarted;
        internal readonly List<(Action complete, Action cancel)> StatusCompletions = new List<(Action, Action)>();
        internal List<Action> ExecutionCompletions;
        internal bool ExecutionEnded;
        internal bool NeedsStartFlush;

        internal int Count(StatusInstance status, string key)
            => _counts.TryGetValue((status, key), out int count) ? count : 0;

        internal void Collect(StatusInstance status, string key)
            => _counts[(status, key)] = Count(status, key) + 1;

        internal int CountHit(long hitID, StatusInstance status, string key)
            => _hitCounts.TryGetValue((hitID, status, key), out int count) ? count : 0;

        internal void CollectHit(long hitID, StatusInstance status, string key)
            => _hitCounts[(hitID, status, key)] = CountHit(hitID, status, key) + 1;
    }

    internal sealed class ExecutionChain
    {
        internal readonly CounterScope Scope;
        // 真实调用的线性游标。恢复历史声明上下文不能移动它；装备调用不参与此游标。
        internal CounterScope LastCaller;
        internal ExecutionChain(CounterScope scope) { Scope = scope; }
    }

    public sealed class StatusExecutionScope : IDisposable
    {
        private CounterScope scope;
        private readonly CounterScope previous;
        private readonly ExecutionChain previousChain;
        private readonly bool previousEquipmentOrigin;
        private bool completed;
        internal StatusExecutionScope(StatusInstance status, bool joinCurrentExecution,
            bool joinDispatchedInvocation = false, bool startExecution = true)
        {
            previous = CurrentExecution; previousChain = ChainSlot.Value;
            previousEquipmentOrigin = EquipmentOriginSlot.Value;
            if (status == null) return;
            EquipmentOriginSlot.Value = EquipmentStatusLifetime.IsEquipmentStatus(status);
            var dispatched = joinDispatchedInvocation ? DispatchedStatusCall.Value : null;
            var root = previous?.StatusCallRoot;
            scope = dispatched?.IndependentStatus == status && dispatched.StatusDepth > 0 ? dispatched
                : joinCurrentExecution && root?.IndependentStatus == status && root.StatusDepth > 0
                    ? root : new CounterScope(status);
            if (scope.StatusDepth++ == 0)
            {
                if (ChainSlot.Value == null) ChainSlot.Value = new ExecutionChain(new CounterScope(status));
                scope.StatusFailed = false;
                scope.StatusCallRoot = scope;
                scope.CurrentStatusSegment = scope;
                scope.StatusCallChain = ChainSlot.Value;
            }
            else ChainSlot.Value = scope.StatusCallChain;
            CurrentExecution = scope.CurrentStatusSegment;
            if (startExecution) ContinueStatusExecution(status);
        }
        public void Complete() { completed = true; }
        public void Dispose()
        {
            var current = scope;
            if (current == null) return;
            scope = null;
            try
            {
                if (!completed) current.StatusFailed = true;
                if (--current.StatusDepth != 0) return;
                CompleteExecution(current.CurrentStatusSegment);
                var callbacks = current.StatusCompletions.ToArray();
                current.StatusCompletions.Clear();
                bool success = !current.StatusFailed && !BattleExecution.IsCancelled && !BattleExecution.GameplayEffectsStopped;
                foreach (var callback in callbacks)
                    if (success) callback.complete(); else callback.cancel?.Invoke();
            }
            finally
            {
                CurrentExecution = previous; ChainSlot.Value = previousChain;
                EquipmentOriginSlot.Value = previousEquipmentOrigin;
            }
        }
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
        internal StatusInstance ExecutingStatus;
        internal object Occurrence;
        internal Func<bool> StatusStillValid;
        public PreparedTrigger(Func<Task> execute, Action release)
        {
            _execute = execute;
            _release = release;
        }
        /// <summary>只标记待执行者与本次触发身份；不在确认候选时创建Execution。</summary>
        internal PreparedTrigger WithStatus(StatusInstance status, object occurrence, Func<bool> stillValid = null)
        {
            ExecutingStatus = status; Occurrence = occurrence; StatusStillValid = stillValid;
            return this;
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
                    Settlement state = previous ?? new Settlement { LastCaller = ChainSlot.Value?.LastCaller };
                    SettlementSlot.Value = state;
                    state.Depth++;
                    bool completed = false;
                    try
                    {
                        await ExecutePrepared(prepared);
                        if (AfterResponses.Count > 0) await CloseStatusCalls(state, true);
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
                        try { if (state.Depth == 0) await FinishSettlement(state, completed); }
                        finally { SettlementSlot.Value = previous; }
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
        // 同一实际响应列跨声明监听回调/子收集层共享；历史来源Snapshot不改写这个游标。
        public CounterScope LastCaller;
        public StatusExecutionScope StatusCall;
        public StatusInstance StatusOwner;
        public object StatusOccurrence;
        public CounterScope StatusRoot;
        public readonly List<Action> Callbacks = new List<Action>();
    }
    private static readonly AsyncLocal<CounterScope> ActionSlot = new AsyncLocal<CounterScope>();
    private static readonly AsyncLocal<CounterScope> ExecutionSlot = new AsyncLocal<CounterScope>();
    private static readonly AsyncLocal<ExecutionChain> ChainSlot = new AsyncLocal<ExecutionChain>();
    private static readonly AsyncLocal<CounterScope> EffectSlot = new AsyncLocal<CounterScope>();
    private static readonly AsyncLocal<Collection> CollectionSlot = new AsyncLocal<Collection>();
    private static readonly AsyncLocal<int> DepthSlot = new AsyncLocal<int>();
    private static readonly AsyncLocal<Settlement> SettlementSlot = new AsyncLocal<Settlement>();
    // 属于实际执行队列，不随历史声明Snapshot回放；事件行据此加入当前整组状态调用。
    private static readonly AsyncLocal<CounterScope> DispatchedStatusCall = new AsyncLocal<CounterScope>();
    // 仅限制Status_Action事件响应资格；真实状态、效果、命中身份与结算仍完整保留。
    private static readonly AsyncLocal<bool> EquipmentOriginSlot = new AsyncLocal<bool>();
    public static CounterScope CurrentAction { get => ActionSlot.Value; private set => ActionSlot.Value = value; }
    public static CounterScope CurrentExecution { get => ExecutionSlot.Value; private set => ExecutionSlot.Value = value; }
    /// <summary>保持整链临时记忆、独立根状态和表现分组，不作为Action额度。</summary>
    public static CounterScope CurrentChain => ChainSlot.Value?.Scope;
    public static CounterScope CurrentEffect { get => EffectSlot.Value; private set => EffectSlot.Value = value; }
    private static Collection _collection { get => CollectionSlot.Value; set => CollectionSlot.Value = value; }
    private static int _triggerDepth { get => DepthSlot.Value; set => DepthSlot.Value = value; }
    private const int MaxTriggerDepth = 128;

    public sealed class Snapshot
    {
        internal CounterScope Action, Effect, Execution;
        internal ExecutionChain Chain;
        internal bool EquipmentOrigin;
        public IDisposable Enter()
        {
            CounterScope previousAction = CurrentAction, previousEffect = CurrentEffect, previousExecution = CurrentExecution;
            ExecutionChain previousChain = ChainSlot.Value;
            bool previousEquipmentOrigin = EquipmentOriginSlot.Value;
            CurrentAction = Action;
            CurrentEffect = Effect;
            CurrentExecution = Execution;
            ChainSlot.Value = Chain;
            EquipmentOriginSlot.Value = EquipmentOrigin;
            return new RestoreScope(() => {
                CurrentAction = previousAction; CurrentEffect = previousEffect;
                CurrentExecution = previousExecution; ChainSlot.Value = previousChain;
                EquipmentOriginSlot.Value = previousEquipmentOrigin;
            });
        }
    }
    public static Snapshot Capture() => new Snapshot {
        Action = CurrentAction, Effect = CurrentEffect, Execution = CurrentExecution, Chain = ChainSlot.Value,
        EquipmentOrigin = EquipmentOriginSlot.Value
    };

    /// <summary>直接调用装备效果时也保留来源；await、声明和响应排队通过Snapshot携带该标记。</summary>
    public static IDisposable BeginStatusEffectSource(StatusInstance status)
        => BeginEquipmentEffectSource(EquipmentStatusLifetime.IsEquipmentStatus(status));

    internal static IDisposable BeginEquipmentEffectSource(bool isEquipment)
    {
        bool previous = EquipmentOriginSlot.Value;
        EquipmentOriginSlot.Value = previous || isEquipment;
        return new RestoreScope(() => EquipmentOriginSlot.Value = previous);
    }

    /// <summary>
    /// 武器/圣遗物的结果不是Status_Action事件触发的引子，任何限次（含无限制行）均不收集、不占额度。
    /// OnApply、OnItsTurn、OnEnd、OnDestroy和OnDeath仍走状态自己的生命周期。
    /// context.Status是响应监听者，绝不能据此排除装备对普通角色行为的正常响应。
    /// </summary>
    public static bool CanTriggerStatusAction(StatusActionData action, ScriptHookContext context = null)
    {
        if (action?.ActionType != "OnTrigger") return true;
        var source = context?.DamageEvent?.Source;
        if (source?.EquipmentSource != null || EquipmentStatusLifetime.IsEquipmentStatus(source?.SourceStatus)) return false;
        // 装备处理器可定向续处理原角色声明；那仍以原事实为引子，不是新装备结果。
        // 没有明确原声明时才回落当前执行来源，不能把当前装备的起因误当成新事实。
        var declaration = context?.Declaration ?? context?.DamageEvent?.Declaration;
        if (declaration == null)
        {
            if (EquipmentOriginSlot.Value) return false;
            declaration = BattleEventHub.Current;
        }
        if (declaration?.ResolutionContext.EquipmentOrigin == true) return false;
        if (declaration?.Data is DamageEventData damage)
        {
            source = damage.Source;
            if (source?.EquipmentSource != null || EquipmentStatusLifetime.IsEquipmentStatus(source?.SourceStatus)) return false;
        }
        return true;
    }

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
        if (action == null) return new RestoreScope(null);
        CounterScope previous = CurrentAction, previousExecution = CurrentExecution;
        ExecutionChain previousChain = ChainSlot.Value;
        if (ChainSlot.Value == null) ChainSlot.Value = new ExecutionChain(action);
        if (CurrentAction == null) CurrentAction = action;
        // 主动行为本身也各有Execution；两种额度不能共用同一个账本，允许Action;Execution并列。
        var execution = new CounterScope();
        CurrentExecution = execution;
        if (!EquipmentOriginSlot.Value)
        {
            ChainSlot.Value.LastCaller = CurrentExecution;
            if (SettlementSlot.Value != null) SettlementSlot.Value.LastCaller = CurrentExecution;
        }
        return new RestoreScope(() => {
            try { CompleteExecution(execution); }
            finally { CurrentAction = previous; CurrentExecution = previousExecution; ChainSlot.Value = previousChain; }
        });
    }

    /// <summary>非主动技能容器沿用调用者Execution，不能把蓄力起手当一次主动执行。</summary>
    public static IDisposable BeginSkillAction(bool isActiveAction)
        => isActiveAction ? EnterAction(new CounterScope()) : new RestoreScope(null);

    /// <summary>无角色/状态容器的独立反应tick各有一次Execution；即时派生沿用来源，不新建Action。</summary>
    public static IDisposable BeginIndependentExecution()
    {
        if (CurrentExecution != null) return new RestoreScope(null);
        var previousChain = ChainSlot.Value;
        var scope = new CounterScope();
        if (ChainSlot.Value == null) ChainSlot.Value = new ExecutionChain(scope);
        CurrentExecution = scope;
        return new RestoreScope(() => {
            try { CompleteExecution(scope); }
            finally { CurrentExecution = null; ChainSlot.Value = previousChain; }
        });
    }

    /// <summary>
    /// 本次状态调用的连续行动行/效果共用Execution；被其他状态或主动技能打断后，
    /// 下次实际调用本状态效果时通过ContinueStatusExecution换新额度，不在调用栈返回时自动计次。
    /// 单个效果内部的ExecuteCount、多hit和多目标不拆段。再次独立触发同一实例仍是新调用。
    /// 没有主动行为时Action保持null，不提交虚构的主动行为声明。
    /// </summary>
    public static StatusExecutionScope BeginStatusExecution(StatusInstance status, bool joinCurrentExecution = false,
        bool joinDispatchedInvocation = false, bool startExecution = true)
        => new StatusExecutionScope(status, joinCurrentExecution, joinDispatchedInvocation, startExecution);

    /// <summary>
    /// 仅在实际调用状态的下一行/下一效果时推进。不能在命中、声明回放或额度查询中调用，
    /// 否则同一Damage内部的多hit及被动观察响应会错误拆分来源执行。
    /// </summary>
    public static void ContinueStatusExecution(StatusInstance status)
    {
        var root = CurrentExecution?.StatusCallRoot;
        if (status == null || root?.IndependentStatus != status || root.StatusDepth <= 0) return;
        if (StartsStatusSegment(root))
        {
            if (root.StatusCallStarted)
            {
                bool startedSetup = CompleteExecution(root.CurrentStatusSegment);
                root.CurrentStatusSegment = new CounterScope(status) { StatusCallRoot = root, NeedsStartFlush = startedSetup };
            }
            root.StatusCallStarted = true;
            ChainSlot.Value.LastCaller = root;
            var settlement = SettlementSlot.Value;
            if (settlement != null) settlement.LastCaller = root;
        }
        CurrentExecution = root.CurrentStatusSegment;
    }

    private static bool StartsStatusSegment(CounterScope root)
    {
        var chain = ChainSlot.Value;
        var settlement = SettlementSlot.Value;
        var lastCaller = settlement != null ? settlement.LastCaller : chain?.LastCaller;
        return chain != null && !EquipmentStatusLifetime.IsEquipmentStatus(root.IndependentStatus) && lastCaller != root;
    }

    /// <summary>当前实际Execution结束时处理已提交的结果，状态重新分段时也结束旧Execution。</summary>
    internal static bool AfterCurrentExecution(Action complete)
    {
        var execution = CurrentExecution;
        if (execution == null || execution.ExecutionEnded) return false;
        var source = Capture();
        (execution.ExecutionCompletions ??= new List<Action>()).Add(() => { using (source.Enter()) complete(); });
        return true;
    }

    private static bool CompleteExecution(CounterScope execution)
    {
        if (execution == null || execution.ExecutionEnded) return false;
        execution.ExecutionEnded = true;
        var callbacks = execution.ExecutionCompletions;
        execution.ExecutionCompletions = null;
        if (callbacks == null || BattleExecution.IsCancelled || BattleExecution.GameplayEffectsStopped) return false;
        foreach (Action callback in callbacks) callback();
        return true;
    }

    /// <summary>同一状态继续下一段前，先结清旧段收尾产生的初始化和移除响应。</summary>
    internal static Task FlushExecutionStart()
    {
        var execution = CurrentExecution;
        if (execution?.NeedsStartFlush != true) return Task.CompletedTask;
        execution.NeedsStartFlush = false;
        return BattleExecution.Flush();
    }

    /// <summary>状态本次完整执行及延伸结清后收尾；异常/取消丢弃。不发布假的主动行动完成事件。</summary>
    public static bool AfterStatusExecution(Action complete, Action cancel = null)
    {
        var root = CurrentExecution?.StatusCallRoot;
        if (root == null || root.StatusDepth == 0) return false;
        // 各段的结清回调保留各自来源额度；整次状态调用成功后才执行，不提前改变死亡/阶段窗口。
        var source = Capture();
        root.StatusCompletions.Add((() => { using (source.Enter()) complete(); },
            cancel == null ? (Action)null : () => { using (source.Enter()) cancel(); }));
        return true;
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
            var source = Capture();
            _collection.Candidates.Add(new Candidate
            {
                ApplyOrder = applyOrder,
                Sequence = _collection.Candidates.Count,
                Prepare = () => PrepareInSource(source, prepare)
            });
            return true; // 已提交候选；是否成功收集在本层统一判定。
        }
        using (BeginEffect())
        {
            PreparedTrigger trigger = prepare();
            if (trigger == null) return false;
            BattleExecution.Enqueue(() => ExecutePrepared(new List<PreparedTrigger> { trigger }), trigger.Dispose);
            return true;
        }
    }

    // 一层候选可能来自不同执行者。确认与执行均保留提交者额度，不能借用前一个响应的状态。
    private static PreparedTrigger PrepareInSource(Snapshot source, Func<PreparedTrigger> prepare)
    {
        using (source.Enter())
        {
            PreparedTrigger trigger = prepare();
            return trigger == null ? null : new PreparedTrigger(async () => {
                using (source.Enter()) await trigger.Execute();
            }, trigger.Dispose).WithStatus(trigger.ExecutingStatus, trigger.Occurrence, () => {
                using (source.Enter()) return trigger.StatusStillValid == null || trigger.StatusStillValid();
            });
        }
    }

    /// <summary>
    /// 在真实队列中归并同一状态、同次触发的连续行动行；不同事件或不同实例保持独立。
    /// 普通观察回调不拆组，若其真的调用其他状态，线性游标会在原状态继续时分段。
    /// 历史Snapshot只恢复触发事实；不应把响应者切回原攻击者，因而执行组单独保存。
    /// </summary>
    private static async Task ExecutePrepared(List<PreparedTrigger> prepared)
    {
        var previousSettlement = SettlementSlot.Value;
        var state = previousSettlement ?? new Settlement { LastCaller = ChainSlot.Value?.LastCaller };
        SettlementSlot.Value = state;
        state.Depth++;
        bool completed = false;
        try
        {
            foreach (var trigger in prepared)
            {
                BattleExecution.ThrowIfCancelled();
                StatusExecutionScope equipmentCall = null;
                CounterScope requestRoot = null;
                if (trigger.ExecutingStatus != null)
                {
                    if (trigger.StatusStillValid != null && !trigger.StatusStillValid())
                    { trigger.Dispose(); continue; }
                    object key = trigger.Occurrence ?? prepared;
                    if (EquipmentStatusLifetime.IsEquipmentStatus(trigger.ExecutingStatus))
                    {
                        // 装备有自己的真实调用身份，但不能结束或重开前后角色的Execution。
                        using (Capture().Enter())
                        {
                            equipmentCall = new StatusExecutionScope(trigger.ExecutingStatus, false, startExecution: false);
                            requestRoot = CurrentExecution.StatusCallRoot;
                        }
                    }
                    else if (trigger.ExecutingStatus != state.StatusOwner || !ReferenceEquals(key, state.StatusOccurrence))
                    {
                        await CloseStatusCalls(state, true);
                        state.StatusOwner = trigger.ExecutingStatus; state.StatusOccurrence = key;
                        // 到真正调用效果时再移动线性游标；取消/无效行不会制造一次执行。
                        // 缓存跨回调继续使用；打开/关闭时不能把响应者上下文泄漏给源事实。
                        using (Capture().Enter())
                        {
                            state.StatusCall = new StatusExecutionScope(state.StatusOwner, false, startExecution: false);
                            state.StatusRoot = CurrentExecution.StatusCallRoot;
                        }
                    }
                    if (requestRoot == null) requestRoot = state.StatusRoot;
                }
                var previous = DispatchedStatusCall.Value;
                DispatchedStatusCall.Value = requestRoot;
                bool rowCompleted = false;
                try { await BattleExecution.RunInline(trigger.Execute); rowCompleted = true; }
                finally
                {
                    DispatchedStatusCall.Value = previous;
                    if (equipmentCall != null)
                        using (Capture().Enter())
                        {
                            if (rowCompleted) equipmentCall.Complete();
                            equipmentCall.Dispose();
                        }
                }
            }
            completed = true;
        }
        finally
        {
            foreach (var trigger in prepared) trigger.Dispose();
            state.Depth--;
            try { if (state.Depth == 0) await FinishSettlement(state, completed); }
            finally { SettlementSlot.Value = previousSettlement; }
        }
        await BattleExecution.Flush();
    }

    private static async Task CloseStatusCalls(Settlement state, bool success)
    {
        while (state.StatusCall != null)
        {
            var call = state.StatusCall;
            state.StatusCall = null; state.StatusRoot = null;
            state.StatusOwner = null; state.StatusOccurrence = null;
            using (Capture().Enter())
            {
                if (success) call.Complete();
                call.Dispose();
            }
            // 收尾产生的真实状态响应仍按原队列先结清，再执行下一来源或死亡响应。
            await BattleExecution.Flush();
        }
    }

    private static async Task FinishSettlement(Settlement state, bool completed)
    {
        // 调用收尾也可能产生真实响应；仍属于正在结算，不能提前执行胜负等AfterSettlement。
        state.Depth++;
        try { await CloseStatusCalls(state, completed); }
        finally { state.Depth--; }
        Action[] callbacks = state.Callbacks.ToArray();
        state.Callbacks.Clear();
        if (completed && !BattleExecution.IsCancelled)
            foreach (var callback in callbacks) callback();
    }

    internal static object StatusActionOccurrence(ScriptHookContext context)
        => context?.Declaration ?? (object)context?.DamageEvent ?? context?.HealEvent
            ?? (object)context?.ReactionEvent ?? BattleEventHub.Current;

    private static long TriggerHitID(ScriptHookContext context)
    {
        // 优先使用当前响应保留的真实 hit 身份，不能使用后续子响应的“最后命中”。
        context = context ?? ScriptHookContext.Current;
        if (context?.DamageEvent != null && context.DamageEvent.RuntimeHitID > 0)
            return context.DamageEvent.RuntimeHitID;
        return context?.Declaration?.Header.HitId ?? BattleEventHub.Current?.Header.HitId ?? 0;
    }

    public static bool LimitReached(StatusInstance status, StatusActionData action, ScriptHookContext context = null,
        bool continueStatusCall = false)
    {
        if (!CanTriggerStatusAction(action, context)) return true;
        string key = ActionKey(action);
        long hitID = action.MaxTimePerHit > 0 ? TriggerHitID(context) : 0;
        return (hitID > 0 && CurrentEffect != null
                && CurrentEffect.CountHit(hitID, status, key) >= action.MaxTimePerHit)
            || (action.MaxTimePerEffect > 0 && CurrentEffect != null
                && CurrentEffect.Count(status, key) >= action.MaxTimePerEffect)
            || (action.MaxTimePerAction > 0 && (CurrentAction == null
                || CurrentAction.Count(status, key) >= action.MaxTimePerAction))
            || (action.MaxTimePerExecution > 0 && ExecutionLimitReached(status, action, key, continueStatusCall));
    }

    private static bool ExecutionLimitReached(StatusInstance status, StatusActionData action, string key, bool continueStatusCall)
    {
        var execution = CurrentExecution;
        var root = execution?.StatusCallRoot;
        if (continueStatusCall && root != null && root.IndependentStatus == status && root.StatusDepth > 0)
        {
            // 阶段行预查下一实际执行段的额度，候选被拒绝时不推进执行游标。
            if (root.StatusCallStarted && StartsStatusSegment(root)) return false;
            execution = root.CurrentStatusSegment;
        }
        return execution == null || execution.Count(status, key) >= action.MaxTimePerExecution;
    }

    public static bool TryCollect(StatusInstance status, StatusActionData action, ScriptHookContext context = null,
        bool continueStatusCall = false)
    {
        if (LimitReached(status, action, context, continueStatusCall)) return false;
        string key = ActionKey(action);
        long hitID = action.MaxTimePerHit > 0 ? TriggerHitID(context) : 0;
        if (hitID > 0) CurrentEffect?.CollectHit(hitID, status, key);
        if (action.MaxTimePerEffect > 0) CurrentEffect?.Collect(status, key);
        if (action.MaxTimePerAction > 0) CurrentAction?.Collect(status, key);
        if (!continueStatusCall && action.MaxTimePerExecution > 0) CurrentExecution?.Collect(status, key);
        return true;
    }

    internal static bool CommitContinuedStatusAction(StatusInstance status, StatusActionData action)
    {
        string key = ActionKey(action);
        if (action.MaxTimePerExecution > 0 && ExecutionLimitReached(status, action, key, continueStatusCall: true))
            return false;
        ContinueStatusExecution(status);
        if (action.MaxTimePerExecution > 0) CurrentExecution?.Collect(status, key);
        return true;
    }

    public static bool HasApplicableLimit(StatusActionData action)
        // Hit 额度不能替代旧重入保护：递归追加的新 hit 会有新 ID，不能据此无限自触发。
        => (action.MaxTimePerEffect > 0 && CurrentEffect != null)
            || (action.MaxTimePerAction > 0 && CurrentAction != null);
        // Execution在每次状态触发时重开，不能单独替代重入保护，否则A→B→A可不断获得新额度。

    public static IDisposable EnterTrigger()
    {
        if (_triggerDepth >= MaxTriggerDepth)
            throw new InvalidOperationException("状态连锁超过安全深度，请检查未限制的循环触发。");
        _triggerDepth++;
        return new RestoreScope(() => _triggerDepth--);
    }
}
