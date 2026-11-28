using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 肉鸽主流程骨架：控制步骤、固定奖励待发记录和持久化提交，不加载场景、不配置队伍或执行战斗。
/// 战斗：EnterNode -> BeginBattle -> RecordBattleResult -> CompleteNode；战败结算后返回同一Node。
/// 遭遇事件：EnterNode -> BeginEventBattle -> 战斗结算，或SkipEvent无奖励完成。
/// CompleteEvent保留给其他事件结果提交。具体模块从Resume获取上下文并接管当前步骤。
/// 所有写入先落盘，失败不会在本服务留下已推进的内存状态。
/// </summary>
public sealed class RoguelikeFlowService
{
    internal IDisposable BeginSaveTransaction() => saves.BeginTransaction();
    internal RoguelikeSaveResult<RoguelikeWishPlanningData> GetWishPlanning(RoguelikeWishRules rules, IBattleRandomSource random)
        => saves.Repository.GetOrCreateWishPlanning(rules, random);
    private readonly RoguelikeSaveService saves;
    private readonly IRoguelikeRouteRules routes;
    private readonly RoguelikeNodeContentCatalog nodeContents;
    private readonly RoguelikeNodeRewardCatalog nodeRewards;

    public RoguelikeFlowService(RoguelikeSaveService saves, IRoguelikeRouteRules routes,
        RoguelikeNodeContentCatalog nodeContents = null, RoguelikeNodeRewardCatalog nodeRewards = null)
    {
        this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.nodeContents = nodeContents;
        this.nodeRewards = nodeRewards;
    }

    public RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initialSnapshot, string replaceRunId = null)
    {
        using var transaction = BeginSaveTransaction();
        if (initialSnapshot == null) return Invalid("开局快照不能为空。");
        var snapshot = RoguelikeSaveJson.DeepClone(initialSnapshot);
        if (snapshot.PendingRewardData != null) return Invalid("新开局不能携带待领取奖励。");
        if (snapshot.PendingNodeReward != null) return Invalid("新开局不能携带待发节点奖励。");
        snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
        snapshot.AppearanceAwards = new List<AppearanceBossAward>();
        if (nodeContents != null && snapshot.RunSeed == 0) snapshot.RunSeed = BattleRandom.NextInt(1, int.MaxValue);
        return saves.StartNewGame(snapshot, replaceRunId);
    }

    public RoguelikeSaveResult StartFromChapterSlot(int chapterId, int slotIndex, string replaceRunId = null)
    {
        return saves.StartFromChapterSlot(chapterId, slotIndex, replaceRunId);
    }

    public RoguelikeSaveResult<RoguelikeFlowContext> Resume() => Resume(out _);

    private RoguelikeSaveResult<RoguelikeFlowContext> Resume(out ActiveRunSaveData activeRun)
    {
        using var transaction = BeginSaveTransaction();
        activeRun = null;
        var loaded = saves.LoadActiveRun();
        if (!loaded.Succeeded) return Failure<RoguelikeFlowContext>(loaded);
        var save = loaded.Data;
        if (save.Snapshot.Flow == null)
        {
            // 仅无待处理步骤的旧路线档可以无损进入新框架，不猜测旧战斗/事件的内容。
            if (save.ResumePoint != RoguelikeResumePoint.ChapterRoute || save.Snapshot.PendingRewardData != null)
                return Failure<RoguelikeFlowContext>(Invalid("该旧存档缺少流程上下文，需由原步骤模块恢复。"));
            save.Snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
            var migrated = saves.SaveActiveRun(save, save.Snapshot, save.ResumePoint, save.LastSaveReason);
            if (!migrated.Succeeded) return Failure<RoguelikeFlowContext>(migrated);
        }
        // 兼容接入固定奖励前已停在胜利/事件结算的档；先固化清单，再允许返回路线。
        if (save.Snapshot.Flow.Step == RoguelikeFlowStep.Settlement &&
            save.Snapshot.PendingNodeReward == null && save.Snapshot.Flow.Node?.FixedRewardClaimed != true && nodeRewards != null)
        {
            save.Snapshot.PendingNodeReward = nodeRewards.Capture(save.Snapshot);
            var captured = saves.SaveActiveRun(save, save.Snapshot, save.ResumePoint, RoguelikeSaveReason.PendingRewardGenerated);
            if (!captured.Succeeded) return Failure<RoguelikeFlowContext>(captured);
        }
        activeRun = save;
        return RoguelikeSaveResult<RoguelikeFlowContext>.Success(CreateContext(save, RoguelikeSaveJson.DeepClone(save.Snapshot)));
    }

    private static RoguelikeFlowContext CreateContext(ActiveRunSaveData save, RoguelikeProgressSnapshot snapshot)
        => new RoguelikeFlowContext
        {
            RunId = save.RunId,
            BattleId = save.PendingBattleId,
            ResumePoint = save.ResumePoint,
            Snapshot = snapshot,
        };

    public RoguelikeSaveResult EnterNode(string stepId, RoguelikeNodeSelection selection)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Route);
        if (!current.Succeeded) return current;
        if (selection == null || string.IsNullOrWhiteSpace(selection.NodeId) || selection.StageIndex < 0 ||
            (nodeContents == null && string.IsNullOrWhiteSpace(selection.ContentId)) ||
            !Enum.IsDefined(typeof(RoguelikeNodeKind), selection.Kind))
            return Invalid("节点必须包含有效 ID、层索引、类型和内容 ID。");
        if (selection.IsChapterFinalBoss && selection.Kind != RoguelikeNodeKind.Boss)
            return Invalid("章节末尾Boss标记只能用于Boss节点。");
        var snapshot = current.Data.Snapshot;
        var node = RoguelikeSaveJson.DeepClone(selection);
        node.EventBattleContentId = null;
        node.FixedRewardClaimed = false;
        node.BattlePartyCharacterIds = node.TrialCharacterIds = null;
        node.TrialConfigurations = null;
        if (snapshot.ClearedNodeIds.Contains(node.NodeId)) return Invalid("节点已经完成，不能重复进入。");
        var previous = RoguelikeSaveJson.DeepClone(snapshot);
        if (nodeContents != null)
        {
            var selected = nodeContents.Select(snapshot, node);
            if (!selected.Succeeded) return selected;
        }
        var allowed = routes.ValidateNodeEntry(previous,
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
        => BeginBattle(stepId, battleId, false);

    /// <summary>事件页迎战、编队保存之后显式调用；保留Event节点身份，不伪装成普通战斗。</summary>
    public RoguelikeSaveResult BeginEventBattle(string stepId, string battleId)
        => BeginBattle(stepId, battleId, true);

    /// <summary>战前专用事务：试用选择只存当前节点；纯自有队伍继续保存到原编队预设。</summary>
    internal RoguelikeSaveResult<RoguelikeFlowContext> SaveBattleParty(string stepId,
        IEnumerable<int> characterIds, IEnumerable<int> trialIds, int preset)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Node);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        var party = characterIds?.ToList(); var trials = trialIds?.ToList() ?? new List<int>();
        if (party == null || party.Count < 1 || party.Count > 4 || party.Distinct().Count() != party.Count ||
            party.Any(id => id <= 0) || trials.Distinct().Count() != trials.Count || trials.Any(id => !party.Contains(id)))
            return Failure<RoguelikeFlowContext>(Invalid("本场队伍或试用选择无效。"));
        var stage = nodeContents?.Resolve(snapshot.CurrentChapterId, snapshot.Flow.Node);
        if (stage == null || !stage.Succeeded) return Failure<RoguelikeFlowContext>(stage ?? Invalid("缺少本场战斗配置。"));
        var allowed = stage.Data.Trials.ToDictionary(t => t.CharacterID);
        if (trials.Any(id => !allowed.ContainsKey(id)) || party.Any(id => !trials.Contains(id) && !snapshot.Characters.Any(c => c.CharacterID == id)))
            return Failure<RoguelikeFlowContext>(Invalid("选择了本场不可用的角色。"));
        if (trials.Count == 0)
        {
            var roster = new RoguelikeChapterRuntime(snapshot);
            roster.SetParty(party, preset);
            snapshot = roster.ExportSnapshot();
        }
        snapshot.Flow.Node.BattlePartyCharacterIds = party;
        snapshot.Flow.Node.TrialCharacterIds = trials;
        snapshot.Flow.Node.TrialConfigurations = RoguelikeSaveJson.DeepClone(trials.Select(id => allowed[id]).ToList());
        snapshot.Flow.StepId = Guid.NewGuid().ToString("N");
        var save = saves.SaveActiveRun(snapshot, current.Data.ResumePoint, RoguelikeSaveReason.EquipmentChanged);
        return save.Succeeded ? Resume() : Failure<RoguelikeFlowContext>(save);
    }

    private RoguelikeSaveResult BeginBattle(string stepId, string battleId, bool eventBattle)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Node);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        bool isEvent = snapshot.Flow.Node.Kind == RoguelikeNodeKind.Event;
        if (isEvent != eventBattle) return Invalid("请通过当前节点对应的战斗入口进入。");
        if (eventBattle)
        {
            var encounter = nodeContents?.ResolveEvent(snapshot.CurrentChapterId, snapshot.Flow.Node);
            if (encounter == null) return Invalid("尚未配置事件战斗模块。");
            if (!encounter.Succeeded) return encounter;
            if (string.IsNullOrEmpty(encounter.Data.BattleContentId)) return Invalid("事件战斗尚未开放。");
            snapshot.Flow.Node.EventBattleContentId = encounter.Data.BattleContentId;
        }
        if (string.IsNullOrWhiteSpace(battleId)) return Invalid("battleId 不能为空。");
        snapshot.Flow = NewStep(RoguelikeFlowStep.Battle, snapshot.Flow.Node);
        // Freeze actual party equipment before entering combat, including allies who may die during this battle.
        if (snapshot.Flow.Node.IsChapterFinalBoss)
        {
            snapshot.Flow.AppearanceParticipants = new List<AppearanceBattleParticipant>();
            foreach (int id in snapshot.PartyCharacterIds)
            {
                var character = snapshot.Characters.Find(x => x.CharacterID == id);
                if (character == null) return Invalid("参战角色不存在：" + id);
                var weapon = snapshot.Inventory.Weapons.Find(x => x.InstanceId == character.EquippedWeaponInstanceId);
                if (!string.IsNullOrEmpty(character.EquippedWeaponInstanceId) && weapon == null) return Invalid("参战角色装备的武器实例不存在。");
                snapshot.Flow.AppearanceParticipants.Add(new AppearanceBattleParticipant { CharacterId = id, WeaponId = weapon?.Weapon.WeaponID ?? 0 });
            }
        }
        return saves.SaveBeforeBattle(snapshot, battleId);
    }

    /// <summary>离开遭遇：无奖励地完成节点，一次提交；不经过会补发固定奖励的结算步骤。</summary>
    public RoguelikeSaveResult SkipEvent(string stepId)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Node);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        var encounter = nodeContents?.ResolveEvent(snapshot.CurrentChapterId, snapshot.Flow.Node);
        if (encounter == null) return Invalid("尚未配置事件模块。");
        if (!encounter.Succeeded) return encounter;
        if (snapshot.PendingNodeReward != null || snapshot.PendingRewardData != null)
            return Invalid("已有待结算奖励，不能跳过事件。");
        snapshot.ClearedNodeIds.Add(snapshot.Flow.Node.NodeId);
        snapshot.Flow = NewStep(RoguelikeFlowStep.Route);
        return saves.SaveActiveRun(snapshot, RoguelikeResumePoint.ChapterRoute, RoguelikeSaveReason.EventCompleted);
    }

    public RoguelikeSaveResult RecordBattleVictory(string stepId, int eventEnemyKills = 0)
        => RecordBattleResult(stepId, true, eventEnemyKills);

    public RoguelikeSaveResult RecordBattleResult(string stepId, bool victory, int eventEnemyKills = 0)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Battle);
        if (!current.Succeeded) return current;
        var snapshot = current.Data.Snapshot;
        if (eventEnemyKills < 0) return Invalid("事件击杀数不能为负数。");
        if (victory && snapshot.Flow.Node.IsChapterFinalBoss)
        {
            if (snapshot.Flow.AppearanceParticipants == null) return Invalid("章底Boss战缺少战前参战记录，不能猜测外观奖励对象。");
            if (snapshot.AppearanceAwards == null) snapshot.AppearanceAwards = new List<AppearanceBossAward>();
            snapshot.AppearanceAwards.Add(new AppearanceBossAward
            {
                // RunId changes after deletion/restart or loading an entry slot; repeated real clears count again.
                CompletionId = current.Data.RunId + ":" + snapshot.CurrentChapterId + ":" + snapshot.Flow.StepId,
                Participants = RoguelikeSaveJson.DeepClone(snapshot.Flow).AppearanceParticipants,
            });
        }
        snapshot.Flow = NewStep(RoguelikeFlowStep.Settlement, snapshot.Flow.Node);
        snapshot.Flow.EventEnemyKills = eventEnemyKills;
        snapshot.Flow.BattleDefeated = !victory;
        snapshot.PendingNodeReward = nodeRewards?.Capture(snapshot);
        // 不回写战斗中间态；胜负与待发清单一起落盘，恢复后也只入账一次。
        return saves.SaveAfterBattleResult(snapshot, current.Data.BattleId, victory);
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
        result.Data.PendingNodeReward = nodeRewards?.Capture(result.Data);
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
        => CommitProgressAction(stepId, proposed, reason, out _);

    internal RoguelikeSaveResult CommitProgressAction(string stepId, RoguelikeProgressSnapshot proposed,
        RoguelikeSaveReason reason, out string committedStepId)
    {
        var result = CommitProgressActionWithContext(stepId, proposed, reason);
        committedStepId = result.Data?.Flow.StepId;
        return result;
    }

    internal RoguelikeSaveResult<RoguelikeFlowContext> CommitProgressActionWithContext(
        string stepId, RoguelikeProgressSnapshot proposed, RoguelikeSaveReason reason)
    {
        using var transaction = BeginSaveTransaction();
        var current = Resume(out var activeRun);
        if (!current.Succeeded) return current;
        if (string.IsNullOrWhiteSpace(stepId) || current.Data.Flow.StepId != stepId ||
            current.Data.Flow.Step == RoguelikeFlowStep.Battle ||
            current.Data.Flow.Step == RoguelikeFlowStep.ChapterCompleted)
            return Failure<RoguelikeFlowContext>(Invalid("当前步骤不允许提交背包/养成/配置变更，或回调已经过期。"));
        if (reason != RoguelikeSaveReason.InventoryChanged &&
            reason != RoguelikeSaveReason.CharacterUpgraded && reason != RoguelikeSaveReason.EquipmentChanged &&
            reason != RoguelikeSaveReason.GachaCompleted && reason != RoguelikeSaveReason.ShopPurchased)
            return Failure<RoguelikeFlowContext>(Invalid("此接口只接收背包、养成、装备配置、祈愿和商店结果。"));
        var result = PrepareResult(current.Data.Snapshot, proposed, reason);
        if (!result.Succeeded) return Failure<RoguelikeFlowContext>(result);
        result.Data.PendingRewardData = RoguelikeSaveJson.DeepClone(current.Data.Snapshot.PendingRewardData);
        result.Data.Flow.StepId = Guid.NewGuid().ToString("N");
        var save = saves.SaveActiveRun(activeRun, result.Data, current.Data.ResumePoint, reason);
        return save.Succeeded
            ? RoguelikeSaveResult<RoguelikeFlowContext>.Success(CreateContext(activeRun, activeRun.Snapshot), save.Message)
            : Failure<RoguelikeFlowContext>(save);
    }

    /// <summary>结算模块提交最终结果（包括清除已领取奖励）；成功后才标记节点完成。</summary>
    public RoguelikeSaveResult CompleteNode(string stepId, RoguelikeProgressSnapshot settledSnapshot)
        => CompleteNode(stepId, settledSnapshot, false);

    internal RoguelikeSaveResult CompleteNode(string stepId, RoguelikeProgressSnapshot settledSnapshot, bool fixedRewardGranted)
    {
        using var transaction = BeginSaveTransaction();
        var current = RequireStep(stepId, RoguelikeFlowStep.Settlement);
        if (!current.Succeeded) return current;
        var result = PrepareResult(current.Data.Snapshot, settledSnapshot);
        if (!result.Succeeded) return result;
        var snapshot = result.Data;
        if (snapshot.PendingRewardData != null) return Invalid("奖励尚未领取，不能完成节点。");
        if (snapshot.PendingNodeReward != null && !fixedRewardGranted)
            return Invalid("固定节点奖励尚未入账，不能完成节点。");
        if (fixedRewardGranted) current.Data.Flow.Node.FixedRewardClaimed = true;
        snapshot.PendingNodeReward = null;
        if (current.Data.Flow.BattleDefeated)
        {
            snapshot.Flow = NewStep(RoguelikeFlowStep.Node, current.Data.Flow.Node);
            return saves.SaveActiveRun(snapshot, RoguelikeResumePoint.ChapterRoute, RoguelikeSaveReason.RewardChosen);
        }
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
        if (snapshot.PendingNodeReward != null) return Invalid("固定节点奖励尚未入账，不能结束章节。");
        var allowed = routes.ValidateChapterCompletion(RoguelikeSaveJson.DeepClone(snapshot));
        if (!allowed.Succeeded) return allowed;
        snapshot.CompletedChapterNodeCounts ??= new Dictionary<int, int>();
        snapshot.CompletedChapterNodeCounts[snapshot.CurrentChapterId] = checked(snapshot.CurrentStageIndex + 1);
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
        RoguelikeProgressSnapshot current, RoguelikeProgressSnapshot result, RoguelikeSaveReason? reason = null)
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
        snapshot.PendingNodeReward = RoguelikeSaveJson.DeepClone(current.PendingNodeReward);
        snapshot.UsedNodeContentIds = current.UsedNodeContentIds == null ? null : new List<string>(current.UsedNodeContentIds);
        if (reason != RoguelikeSaveReason.GachaCompleted) snapshot.WishProgress = RoguelikeSaveJson.DeepClone(current.WishProgress);
        if (reason != RoguelikeSaveReason.ShopPurchased)
            snapshot.ShopPurchaseCounts = current.ShopPurchaseCounts == null ? null
                : new Dictionary<string, int>(current.ShopPurchaseCounts, current.ShopPurchaseCounts.Comparer);
        snapshot.CompletedChapterNodeCounts = current.CompletedChapterNodeCounts == null ? null
            : new Dictionary<int, int>(current.CompletedChapterNodeCounts);
        snapshot.AppearanceAwards = current.AppearanceAwards == null ? new List<AppearanceBossAward>()
            : RoguelikeSaveJson.DeepClone(current).AppearanceAwards;
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
