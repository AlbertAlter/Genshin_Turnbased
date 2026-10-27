using System;
using UnityEngine;

/// <summary>
/// 战斗实例运行接口，未来由场景/战斗构建模块实现，不由肉鸽数据适配层决定 UI 或加载哪一个场景。
/// 必须完整应用角色突破、技能、武器和圣遗物；未支持的构筑应返回失败，不能忽略后开战。
/// 角色输入用build.CreateCharacterProfile()，先InitializePanel，再注册完整编队，最后ApplyBuild；突破阶段不得转为bool。
/// 完成面板/装备初始化后调用 RoguelikeBattleEntryRules.Apply，按最大值恢复生命和 50% 能量。
/// 随机沿用 BattleRandom；停止/初始化失败不能回报自然胜负。
/// 战斗结果应从 BattleManager.OnBattleEnded 接出，保持原有响应全部结束后的时点。
/// </summary>
public interface IRoguelikeBattleRuntime
{
    RoguelikeSaveResult StartBattle(RoguelikeBattleSetup setup, Action<bool> onEnded);
    void StopBattle();
}

public sealed class RoguelikeBattleOutcome
{
    public string RunId { get; private set; }
    public string BattleId { get; private set; }
    public string StepId { get; private set; }
    public bool Victory { get; private set; }
    public RoguelikeSaveResult ProgressCommit { get; private set; }

    internal RoguelikeBattleOutcome(RoguelikeBattleSetup setup, bool victory, RoguelikeSaveResult progressCommit)
    {
        RunId = setup.RunId; BattleId = setup.BattleId; StepId = setup.StepId;
        Victory = victory; ProgressCommit = progressCommit;
    }
}

/// <summary>
/// UI 无关的战斗数据/生命周期适配器。先由 flow.BeginBattle 保存恢复点，再 TryStart(flow.Resume().Data)。
/// 胜利只推进到结算步骤，不自动领奖或返回某界面；失败保留战前档。
/// 重试/离开时先 StopBattle，旧运行器的迟到回调会被丢弃。
/// </summary>
public sealed class RoguelikeBattleAdapter : IRoguelikeBattlePort, IDisposable
{
    private readonly RoguelikeFlowService flow;
    private readonly RoguelikeBattlePreparation preparation;
    private readonly IRoguelikeBattleRuntime runtime;
    private Session session;
    private bool disposed;
    private bool cleanupRequired;

    private sealed class Session
    {
        public RoguelikeBattleSetup Setup;
        public bool Starting = true;
        public bool? Victory;
        public bool Committed;
    }

    public event Action<RoguelikeBattleOutcome> Ended;
    public RoguelikeBattleOutcome LastOutcome { get; private set; }
    public bool IsRunning => session != null && !session.Victory.HasValue;

    public RoguelikeBattleAdapter(RoguelikeFlowService flow, RoguelikeBattlePreparation preparation,
        IRoguelikeBattleRuntime runtime)
    {
        this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        this.preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>保留原 void 接口；需要处理可恢复错误的调用者使用 TryStart。</summary>
    public void StartBattle(RoguelikeFlowContext context)
    {
        var result = TryStart(context);
        if (!result.Succeeded) throw new InvalidOperationException(result.Message, result.Exception);
    }

    public RoguelikeSaveResult TryStart(RoguelikeFlowContext context)
    {
        if (disposed) return Invalid("战斗适配器已经释放。");
        if (session != null || cleanupRequired) return Invalid("请先释放上一场战斗，再开始或重试。");
        if (context?.Snapshot?.Flow == null) return Invalid("战斗上下文不能为空。");
        var current = flow.Resume();
        if (!current.Succeeded) return current;
        if (context.RunId != current.Data.RunId || context.BattleId != current.Data.BattleId ||
            context.Flow.StepId != current.Data.Flow.StepId)
            return Invalid("战斗上下文已经过期，请重新读取恢复点。");

        Session attempt = null;
        try
        {
            // 只信任最新已保存快照，不能通过修改界面持有的 context 偷换等级或装备。
            var prepared = preparation.Prepare(current.Data);
            if (!prepared.Succeeded) return prepared;
            attempt = new Session { Setup = prepared.Data };
            session = attempt;
            cleanupRequired = true;
            LastOutcome = null;
            // 运行器拿到副本，后续修改它不能篡改回调用于确认身份的上下文。
            var result = runtime.StartBattle(RoguelikeSaveJson.DeepClone(attempt.Setup),
                victory => OnRuntimeEnded(attempt, victory));
            if (result == null || !result.Succeeded)
            {
                CancelAttempt(attempt);
                return result ?? Invalid("战斗运行器未返回启动结果。");
            }
            attempt.Starting = false;
            if (attempt.Victory.HasValue) PublishOutcome(attempt);
            return result;
        }
        catch (Exception exception)
        {
            if (attempt != null) CancelAttempt(attempt);
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation,
                "战斗未启动：" + exception.Message, exception);
        }
    }

    private void OnRuntimeEnded(Session attempt, bool victory)
    {
        if (disposed || !ReferenceEquals(session, attempt) || attempt.Victory.HasValue) return;
        attempt.Victory = victory;
        // 同步回调必须等 StartBattle 明确成功后才允许推进存档。
        if (!attempt.Starting) PublishOutcome(attempt);
    }

    private void PublishOutcome(Session attempt)
    {
        if (!ReferenceEquals(session, attempt)) return;
        var result = attempt.Victory.Value ? CommitVictory(attempt) : RoguelikeSaveResult.Success("战败，保留战前恢复点。");
        LastOutcome = new RoguelikeBattleOutcome(attempt.Setup, attempt.Victory.Value, result);
        var outcome = LastOutcome;
        if (Ended == null) return;
        foreach (Action<RoguelikeBattleOutcome> listener in Ended.GetInvocationList())
            try { listener(outcome); }
            catch (Exception exception) { Debug.LogException(exception); }
    }

    /// <summary>胜利已发生但存档失败时，只重试提交，不重开战斗，也不再广播一次自然结束。</summary>
    public RoguelikeSaveResult RetryVictoryCommit()
    {
        if (disposed || session == null || session.Starting || session.Victory != true || session.Committed)
            return Invalid("当前没有需要重新提交的胜利结果。");
        var result = CommitVictory(session);
        LastOutcome = new RoguelikeBattleOutcome(session.Setup, true, result);
        return result;
    }

    private RoguelikeSaveResult CommitVictory(Session attempt)
    {
        var result = flow.RecordBattleVictory(attempt.Setup.StepId);
        if (result.Succeeded) attempt.Committed = true;
        return result;
    }

    /// <summary>只清理实例，不伪造战败，不改变已有恢复点；运行器负责取消执行并解绑战斗事件。</summary>
    public RoguelikeSaveResult StopBattle()
    {
        if (session == null && !cleanupRequired) return RoguelikeSaveResult.Success();
        session = null;
        try { runtime.StopBattle(); cleanupRequired = false; return RoguelikeSaveResult.Success(); }
        catch (Exception exception)
        {
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation,
                "战斗实例清理失败：" + exception.Message, exception);
        }
    }

    private void CancelAttempt(Session attempt)
    {
        if (!ReferenceEquals(session, attempt)) return;
        session = null;
        try { runtime.StopBattle(); cleanupRequired = false; }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    public void Dispose()
    {
        if (disposed) return;
        var result = StopBattle();
        if (!result.Succeeded && result.Exception != null) Debug.LogException(result.Exception);
        disposed = true;
        Ended = null;
    }

    private static RoguelikeSaveResult Invalid(string message)
    {
        return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
    }
}
