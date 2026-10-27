using System;
using System.Collections.Generic;

/// <summary>
/// 肉鸽主流程骨架：只控制步骤与持久化提交，不加载场景、不配置队伍、不生成奖励或执行战斗。
/// 战斗：EnterNode -> BeginBattle -> RecordBattleVictory -> CompleteNode。
/// 事件：EnterNode -> CompleteEvent -> CompleteNode。具体模块从 Resume 获取上下文并接管当前步骤。
/// 所有写入先落盘，失败不会在本服务留下已推进的内存状态。
/// </summary>
public sealed class RoguelikeFlowService
{
    internal IDisposable BeginSaveTransaction() => saves.BeginTransaction();
    private readonly RoguelikeSaveService saves;
    private readonly IRoguelikeRouteRules routes;

    public RoguelikeFlowService(RoguelikeSaveService saves, IRoguelikeRouteRules routes)
    {
        this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initialSnapshot)
    {
        using var transaction = BeginSaveTransaction();
        if (initialSnapshot == null) return Invalid("开局快照不能为空。");
        var snapshot = RoguelikeSaveJson.DeepClone(initialSnapshot);
        if (snapshot.PendingRewardData != null) return Invalid("新开局不能携带待领取奖励。");
        snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
        return saves.StartNewGame(snapshot);
    }

    public RoguelikeSaveResult StartFromChapterSlot(int chapterId, int slotIndex)
    {
        return saves.StartFromChapterSlot(chapterId, slotIndex);
    }

    public RoguelikeSaveResult<RoguelikeFlowContext> Resume()
    {
        using var transaction = BeginSaveTransaction();
        var loaded = saves.LoadActiveRun();
        if (!loaded.Succeeded) return Failure<RoguelikeFlowContext>(loaded);
        var save = loaded.Data;
        if (save.Snapshot.Flow == null)
        {
            // 仅无待处理步骤的旧路线档可以无损进入新框架，不猜测旧战斗/事件的内容。
            if (save.ResumePoint != RoguelikeResumePoint.ChapterRoute || save.Snapshot.PendingRewardData != null)
                return Failure<RoguelikeFlowContext>(Invalid("该旧存档缺少流程上下文，需由原步骤模块恢复。"));
            save.Snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
            var migrated = saves.SaveActiveRun(save.Snapshot, save.ResumePoint, save.LastSaveReason);
            if (!migrated.Succeeded) return Failure<RoguelikeFlowContext>(migrated);
        }
        return RoguelikeSaveResult<RoguelikeFlowContext>.Success(new RoguelikeFlowContext
        {
            RunId = save.RunId,
            BattleId = save.PendingBattleId,
            ResumePoint = save.ResumePoint,
            Snapshot = RoguelikeSaveJson.DeepClone(save.Snapshot),
        });
    }

    public RoguelikeSaveResult EnterNode(string stepId, RoguelikeNodeSelection selection)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Route);
        if (!current.Succeeded) return current;
        if (selection == null || string.IsNullOrWhiteSpace(selection.NodeId) ||
            string.IsNullOrWhiteSpace(selection.ContentId) || selection.StageIndex < 0 ||
            !Enum.IsDefined(typeof(RoguelikeNodeKind), selection.Kind))
            return Invalid("节点必须包含有效 ID、层索引、类型和内容 ID。");
        var snapshot = current.Data.Snapshot;
        var node = RoguelikeSaveJson.DeepClone(selection);
        if (snapshot.ClearedNodeIds.Contains(node.NodeId)) return Invalid("节点已经完成，不能重复进入。");
        var allowed = routes.ValidateNodeEntry(RoguelikeSaveJson.DeepClone(snapshot),
            RoguelikeSaveJson.DeepClone(node));
        if (!allowed.Succeeded) return allowed;
        snapshot.CurrentNodeId = node.NodeId;
        snapshot.CurrentStageIndex = node.StageIndex;
        if (!snapshot.SelectedRouteNodeIds.Contains(node.NodeId)) snapshot.SelectedRouteNodeIds.Add(node.NodeId);
        snapshot.Flow = NewStep(RoguelikeFlowStep.Node, node);
        return saves.SaveActiveRun(snapshot, RoguelikeResumePoint.ChapterRoute, RoguelikeSaveReason.RouteChosen);
    }

    /// <summary>先保存，再由调用者通过 IRoguelikeBattlePort 启动；队伍配置不由流程层生成或修改。</summary>
    public RoguelikeSaveResult BeginBattle(string stepId, string battleId)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Node);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        if (snapshot.Flow.Node.Kind == RoguelikeNodeKind.Event) return Invalid("事件节点不能直接进入战斗步骤。");
        if (string.IsNullOrWhiteSpace(battleId)) return Invalid("battleId 不能为空。");
        snapshot.Flow = NewStep(RoguelikeFlowStep.Battle, snapshot.Flow.Node);
        return saves.SaveBeforeBattle(snapshot, battleId);
    }

    public RoguelikeSaveResult RecordBattleVictory(string stepId)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Battle);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        snapshot.Flow = NewStep(RoguelikeFlowStep.Settlement, snapshot.Flow.Node);
        // 不把战斗中的生命、能量或临时状态写回章节档案，也不在这里发奖。
        return saves.SaveAfterBattleVictory(snapshot, current.Data.BattleId);
    }

    public RoguelikeSaveResult CompleteEvent(string stepId, RoguelikeProgressSnapshot eventResult)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Node);
        if (!current.Succeeded) return current;
        if (current.Data.Flow.Node.Kind != RoguelikeNodeKind.Event) return Invalid("当前不是事件节点。");
        var result = PrepareResult(current.Data.Snapshot, eventResult);
        if (!result.Succeeded) return result;
        result.Data.Flow = NewStep(RoguelikeFlowStep.Settlement, current.Data.Flow.Node);
        return saves.SaveActiveRun(result.Data, RoguelikeResumePoint.Event, RoguelikeSaveReason.EventCompleted);
    }

    /// <summary>奖励内容及抽取逻辑由奖励模块负责，本接口只保留已有待选奖励的存档接入。</summary>
    public RoguelikeSaveResult SavePendingReward(string stepId, PendingRewardData reward)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Settlement);
        if (!current.Succeeded) return current;
        if (current.Data.Snapshot.PendingRewardData != null) return Invalid("已有待领取奖励，不能覆盖重新生成。");
        current.Data.Flow.StepId = Guid.NewGuid().ToString("N");
        return saves.SavePendingReward(current.Data.Snapshot, reward);
    }

    /// <summary>
    /// 为背包、养成、装备/本次出战配置、祈愿模块保留提交口；具体合法性与消耗由对应模块处理。
    /// 不允许借此改变路线、跳过结算、覆盖待选奖励或写入战斗中间态。
    /// </summary>
    public RoguelikeSaveResult CommitProgressAction(string stepId, RoguelikeProgressSnapshot proposed,
        RoguelikeSaveReason reason)
    {
        using var transaction = BeginSaveTransaction();
        var current = Resume();
        if (!current.Succeeded) return current;
        if (string.IsNullOrWhiteSpace(stepId) || current.Data.Flow.StepId != stepId ||
            current.Data.Flow.Step == RoguelikeFlowStep.Battle ||
            current.Data.Flow.Step == RoguelikeFlowStep.ChapterCompleted)
            return Invalid("当前步骤不允许提交背包/养成/配置变更，或回调已经过期。");
        if (reason != RoguelikeSaveReason.InventoryChanged &&
            reason != RoguelikeSaveReason.CharacterUpgraded && reason != RoguelikeSaveReason.EquipmentChanged &&
            reason != RoguelikeSaveReason.GachaCompleted)
            return Invalid("此接口只接收背包、养成、装备配置和祈愿结果。");
        var result = PrepareResult(current.Data.Snapshot, proposed);
        if (!result.Succeeded) return result;
        result.Data.PendingRewardData = RoguelikeSaveJson.DeepClone(current.Data.Snapshot.PendingRewardData);
        result.Data.Flow.StepId = Guid.NewGuid().ToString("N");
        return saves.SaveActiveRun(result.Data, current.Data.ResumePoint, reason);
    }

    /// <summary>结算模块提交最终结果（包括清除已领取奖励）；成功后才标记节点完成。</summary>
    public RoguelikeSaveResult CompleteNode(string stepId, RoguelikeProgressSnapshot settledSnapshot)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Settlement);
        if (!current.Succeeded) return current;
        var result = PrepareResult(current.Data.Snapshot, settledSnapshot);
        if (!result.Succeeded) return result;
        var snapshot = result.Data;
        if (snapshot.PendingRewardData != null) return Invalid("奖励尚未领取，不能完成节点。");
        snapshot.ClearedNodeIds.Add(current.Data.Flow.Node.NodeId);
        snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
        return saves.SaveActiveRun(snapshot, RoguelikeResumePoint.ChapterRoute, RoguelikeSaveReason.RewardChosen);
    }

    public RoguelikeSaveResult CompleteChapter(string stepId)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Route);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        if (snapshot.PendingRewardData != null) return Invalid("奖励尚未领取，不能结束章节。");
        var allowed = routes.ValidateChapterCompletion(RoguelikeSaveJson.DeepClone(snapshot));
        if (!allowed.Succeeded) return allowed;
        snapshot.Flow = NewStep(RoguelikeFlowStep.ChapterCompleted);
        return saves.SaveActiveRun(snapshot, RoguelikeResumePoint.ChapterCompleted, RoguelikeSaveReason.ManualExit);
    }

    public RoguelikeSaveResult SaveCompletedChapterToSlot(string stepId, int slotIndex, bool overwriteConfirmed = false)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.ChapterCompleted);
        if (!current.Succeeded) return current;
        return saves.CompleteChapterAndSaveToNextSlot(current.Data.Snapshot.CurrentChapterId,
            slotIndex, current.Data.Snapshot, overwriteConfirmed);
    }

    private RoguelikeSaveResult<RoguelikeFlowContext> RequireStep(string stepId, RoguelikeFlowStep step)
    {
        var current = Resume();
        if (!current.Succeeded) return current;
        if (string.IsNullOrWhiteSpace(stepId) || current.Data.Flow.StepId != stepId || current.Data.Flow.Step != step)
            return Failure<RoguelikeFlowContext>(Invalid("流程步骤已变化或回调已提交，请重新获取当前上下文。"));
        return current;
    }

    private static RoguelikeSaveResult<RoguelikeProgressSnapshot> PrepareResult(
        RoguelikeProgressSnapshot current, RoguelikeProgressSnapshot result)
    {
        if (result == null || result.CurrentChapterId != current.CurrentChapterId ||
            result.Flow == null || result.Flow.StepId != current.Flow.StepId)
            return Failure<RoguelikeProgressSnapshot>(Invalid("提交结果必须来自当前章节、当前步骤的上下文。"));
        var snapshot = RoguelikeSaveJson.DeepClone(result);
        // 业务模块只提交业务数据；路线推进与流程标识归本服务管理。
        snapshot.Flow = RoguelikeSaveJson.DeepClone(current.Flow);
        snapshot.CurrentNodeId = current.CurrentNodeId;
        snapshot.CurrentStageIndex = current.CurrentStageIndex;
        snapshot.SelectedRouteNodeIds = new List<string>(current.SelectedRouteNodeIds);
        snapshot.ClearedNodeIds = new List<string>(current.ClearedNodeIds);
        return RoguelikeSaveResult<RoguelikeProgressSnapshot>.Success(snapshot);
    }

    private static RoguelikeFlowData NewStep(RoguelikeFlowStep step, RoguelikeNodeSelection node = null)
    {
        return new RoguelikeFlowData { Step = step, StepId = Guid.NewGuid().ToString("N"), Node = node };
    }

    private static RoguelikeSaveResult Invalid(string message)
    {
        return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
    }

    private static RoguelikeSaveResult<T> Failure<T>(RoguelikeSaveResult result)
    {
        return RoguelikeSaveResult<T>.Failure(result.ErrorCode, result.Message, result.Exception);
    }
}
