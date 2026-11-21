using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>UI-independent application entry point. One persisted snapshot owns character, item and equipment relationships.</summary>
public sealed class RoguelikeGameService : IRoguelikeGameService
{
    private readonly RoguelikeSaveService saves;
    private readonly RoguelikeFlowService flow;
    private readonly RoguelikeInventoryService inventory;
    private readonly IRoguelikeItemCatalog items;
    private readonly IRoguelikeCharacterAcquisitionCatalog characters;
    private RoguelikeBattlePreparation battlePreparation;
    private bool executing;
    public event Action Changed;
    public PlayerAppearanceService Appearance { get; }
    public RoguelikeWishService Wishes { get; private set; }
    public RoguelikeShopService Shop { get; }
    internal RoguelikeFlowService Flow => flow;
    internal IRoguelikeItemCatalog Items => items;

    public RoguelikeGameService(RoguelikeSaveService saves, IRoguelikeRouteRules routes,
        IRoguelikeEquipmentCatalog equipment, IRoguelikeItemCatalog items,
        IRoguelikeCharacterAcquisitionCatalog characters, CardBackCatalog cardBacks = null, RoguelikeShopCatalog shopCatalog = null,
        RoguelikeNodeContentCatalog nodeContents = null, RoguelikeNodeRewardCatalog nodeRewards = null)
    {
        this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
        flow = new RoguelikeFlowService(saves, routes, nodeContents, nodeRewards);
        inventory = new RoguelikeInventoryService(flow, equipment, items);
        Shop = new RoguelikeShopService(flow, inventory,
            () => shopCatalog ?? RoguelikeShopCatalog.LoadFromStreamingAssets(this.items), NotifyChanged);
        Appearance = new PlayerAppearanceService(saves.Repository, cardBacks);
        var appearanceRead = Appearance.Refresh();
        if (!appearanceRead.Succeeded) Debug.LogWarning("外观记录暂不可用：" + appearanceRead.Message);
    }

    /// <summary>Uses loaded DataManager templates and runtime Items/Wishlist tables. Optional phase selection is driven by chapter/node progress.</summary>
    public static RoguelikeGameService CreateFromStreamingAssets(DataManager data, IRoguelikeRouteRules routes,
        RoguelikeSavePaths paths = null, Func<RoguelikeProgressSnapshot, Version> wishPhase = null,
        RoguelikeWishRules wishRules = null, IBattleRandomSource wishRandom = null)
        => Create(data, routes, RoguelikeNodeContentCatalog.LoadFromStreamingAssets(), paths, wishPhase, wishRules, wishRandom);

    internal static RoguelikeGameService CreateCampaign(DataManager data, out RoguelikePlayableRoutes routes, RoguelikeSavePaths paths = null)
    {
        data.PreloadRosterCatalogs();
        CampaignUiTables.Preload();
        var contents = RoguelikeNodeContentCatalog.LoadFromStreamingAssets();
        routes = new RoguelikePlayableRoutes();
        routes.Bind(contents);
        return Create(data, routes, contents, paths, null, null, null);
    }

    private static RoguelikeGameService Create(DataManager data, IRoguelikeRouteRules routes, RoguelikeNodeContentCatalog nodeContents,
        RoguelikeSavePaths paths, Func<RoguelikeProgressSnapshot, Version> wishPhase, RoguelikeWishRules wishRules, IBattleRandomSource wishRandom)
    {
        if (data == null || !data.IsLoaded) throw new InvalidOperationException("创建肉鸽入口前必须先加载 DataManager。");
        wishRules ??= new RoguelikeWishRules();
        var saves = new RoguelikeSaveService(new RoguelikeSaveRepository(paths ?? new RoguelikeSavePaths()),
            wishRules: wishRules, wishRandom: wishRandom);
        var equipment = new DataManagerRoguelikeEquipmentCatalog(data);
        var items = data.GetItemCatalog();
        var characters = new DataManagerRoguelikeCharacterAcquisitionCatalog(data);
        var nodeRewards = RoguelikeNodeRewardCatalog.LoadFromStreamingAssets(items, nodeContents.StageIds);
        var catalog = data.GetWishCatalog(nodeContents.StageIds);
        var game = new RoguelikeGameService(saves, routes, equipment, items, characters,
            CardBackCatalog.FromStreamingAssets(), RoguelikeShopCatalog.LoadFromStreamingAssets(items), nodeContents: nodeContents, nodeRewards: nodeRewards);
        game.battlePreparation = new RoguelikeBattlePreparation(nodeContents, equipment, new DataManagerRoguelikeEnemyCatalog(data));
        game.Wishes = new RoguelikeWishService(game.flow, game.inventory, catalog, equipment, items, characters,
            data.WeaponAttributesDict.Values, wishPhase, rules: wishRules, random: wishRandom,
            characterOverview: data.GetCharacterOverview);
        game.Wishes.Changed += game.NotifyChanged;
        return game;
    }

    public RoguelikeBattleAdapter CreateBattleAdapter(IRoguelikeBattleRuntime runtime)
        => new RoguelikeBattleAdapter(flow, battlePreparation ?? throw new InvalidOperationException("尚未加载节点战斗配置。"), runtime);

    public RoguelikeSaveResult<RoguelikeFlowContext> Continue() => flow.Resume();
    public RoguelikeSaveResult<RoguelikeInventoryView> ReadInventory() => inventory.Read();
    public RoguelikeSaveResult<RoguelikeSaveSummary> ReadActiveSummary() => saves.GetActiveRunSummary();
    public RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>> ListChapterSlots(int chapter) => saves.ListChapterSlots(chapter);
    public RoguelikeSaveResult<ChapterEntrySaveData> ReadChapterSlot(int chapter, int slot) => saves.LoadChapterSlot(chapter, slot);
    public RoguelikeSaveResult ReplaceWithNewGame(RoguelikeProgressSnapshot initial, string confirmedRunId)
        => Execute(() => flow.StartNewGame(initial, confirmedRunId));
    public RoguelikeSaveResult ReplaceWithChapterSlot(int chapter, int slot, string confirmedRunId)
        => Execute(() => flow.StartFromChapterSlot(chapter, slot, confirmedRunId));
    public RoguelikeSaveResult RestartChapter(string expectedRunId, RoguelikeProgressSnapshot chapterOne)
        => Execute(() =>
        {
            using var transaction = flow.BeginSaveTransaction();
            var active = saves.LoadActiveRun();
            if (!active.Succeeded) return active;
            var run = active.Data;
            if (string.IsNullOrEmpty(expectedRunId) || run.RunId != expectedRunId)
                return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "当前进度已变化，请重新确认。");
            if (run.StartType == RoguelikeRunStartType.NewGame && run.CurrentChapterId == 1)
                return flow.StartNewGame(chapterOne, expectedRunId);
            if (run.StartType == RoguelikeRunStartType.ChapterSlot && run.SourceChapterId == run.CurrentChapterId)
                return flow.StartFromChapterSlot(run.SourceChapterId, run.SourceSlotIndex, expectedRunId);
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "当前进度缺少本章入口档，无法重新开始。");
        });
    public RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initial) => Execute(() => flow.StartNewGame(initial));
    public RoguelikeSaveResult StartFromChapterSlot(int chapter, int slot) => Execute(() => flow.StartFromChapterSlot(chapter, slot));
    public RoguelikeSaveResult SaveCurrentProgress() => Execute(saves.SaveCurrentProgress);
    public RoguelikeSaveResult RestoreActiveBackup(bool confirmed) => Execute(() => saves.RestoreActiveRunBackup(confirmed), refreshAppearance: true);
    public RoguelikeSaveResult RestoreChapterBackup(int chapter, int slot, bool confirmed) => Execute(() => saves.RestoreChapterSlotBackup(chapter, slot, confirmed));
    public RoguelikeSaveResult<RoguelikeFlowContext> RetryCurrentBattle()
    {
        var result = saves.RetryCurrentBattle();
        return result.Succeeded ? flow.Resume() : RoguelikeSaveResult<RoguelikeFlowContext>.Failure(result.ErrorCode, result.Message, result.Exception);
    }

    public RoguelikeSaveResult Abandon(string expectedRunId, bool confirmed)
        => Execute(() =>
        {
            if (!confirmed || string.IsNullOrWhiteSpace(expectedRunId)) return Invalid("放弃本局需要明确确认，并提供当前 RunId。");
            var current = saves.LoadActiveRun();
            if (!current.Succeeded) return current;
            if (current.Data.RunId != expectedRunId) return Invalid("存档已变化，请重新读取并确认要放弃的流程。");
            return saves.AbandonActiveRun();
        });

    public RoguelikeSaveResult Acquire(string stepId, RoguelikeAcquisition acquisition)
        => Execute(() => inventory.Commit(stepId, draft => ApplyAcquisition(draft, acquisition)));
    public RoguelikeSaveResult AcquireCharacter(string stepId, int characterId)
        => Acquire(stepId, new RoguelikeAcquisition { Characters = new List<int> { characterId } });
    public RoguelikeSaveResult AcquireWeapon(string stepId, int weaponId)
        => Execute(() => inventory.Commit(stepId, draft => draft.AddWeapon(weaponId)));
    public RoguelikeSaveResult AcquireArtifact(string stepId, int setId, GeneratedArtifact artifact)
        => Execute(() => inventory.Commit(stepId, draft => draft.AddArtifact(setId, artifact)));

    public RoguelikeSaveResult AcquireItem(string stepId, string itemId, int quantity)
        => Execute(() => inventory.Commit(stepId, draft =>
        {
            if (quantity <= 0 || string.IsNullOrWhiteSpace(itemId) || !items.TryGetItem(itemId, out var definition) || definition == null)
                throw new InvalidOperationException("获得道具必须包含有效 ID 和正数量。");
            if (definition.ItemType == RoguelikeItemType.Stack)
                draft.AddItems(new[] { new RoguelikeItemStackData { ItemId = itemId, Quantity = quantity } });
            else if (definition.ItemType == RoguelikeItemType.Weapon)
                draft.AddWeapons(definition.ReferenceID, quantity);
            else throw new InvalidOperationException("圣遗物须先生成真实词条，再调用 AcquireArtifact 或批量 Acquire，不能仅凭物品 ID 重新随机。");
        }));

    public RoguelikeSaveResult ConsumeItems(string stepId, IReadOnlyList<RoguelikeItemStackData> costs)
        => Execute(() => inventory.Commit(stepId, draft => draft.ConsumeItems(costs)));
    public RoguelikeSaveResult EquipWeapon(string stepId, int characterId, string instanceId, bool transferConfirmed = false)
        => Execute(() => inventory.Commit(stepId, draft =>
        {
            if (transferConfirmed) draft.TransferWeapon(characterId, instanceId);
            else draft.EquipWeapon(characterId, instanceId);
        }, RoguelikeSaveReason.EquipmentChanged));
    public RoguelikeSaveResult EquipArtifact(string stepId, int characterId, string instanceId, bool transferConfirmed = false)
        => Execute(() => inventory.Commit(stepId, draft =>
        {
            if (transferConfirmed) draft.TransferArtifact(characterId, instanceId);
            else draft.EquipArtifact(characterId, instanceId);
        }, RoguelikeSaveReason.EquipmentChanged));
    public RoguelikeSaveResult UnequipWeapon(string stepId, int characterId)
        => Execute(() => inventory.Commit(stepId, draft => draft.UnequipWeapon(characterId), RoguelikeSaveReason.EquipmentChanged));
    public RoguelikeSaveResult UnequipArtifact(string stepId, int characterId, ArtifactSlot slot)
        => Execute(() => inventory.Commit(stepId, draft => draft.UnequipArtifact(characterId, slot), RoguelikeSaveReason.EquipmentChanged));
    public RoguelikeSaveResult SetParty(string stepId, IReadOnlyList<int> characterIds)
        => Execute(() => inventory.Commit(stepId, draft => draft.SetParty(characterIds), RoguelikeSaveReason.EquipmentChanged));

    // Node/battle/reward modules keep ownership of content and actual outcomes; UI only calls after success.
    public RoguelikeSaveResult EnterNode(string stepId, RoguelikeNodeSelection node) => Execute(() => flow.EnterNode(stepId, node));
    public RoguelikeSaveResult BeginBattle(string stepId, string battleId) => Execute(() => flow.BeginBattle(stepId, battleId));
    public RoguelikeSaveResult RecordBattleVictory(string stepId) => Execute(() => flow.RecordBattleVictory(stepId), refreshAppearance: true);
    public RoguelikeSaveResult SavePendingReward(string stepId, PendingRewardData pending) => Execute(() => flow.SavePendingReward(stepId, pending));
    public RoguelikeSaveResult CompleteChapter(string stepId) => Execute(() => flow.CompleteChapter(stepId));
    public RoguelikeSaveResult SaveCompletedChapterToSlot(string stepId, int slot, bool overwriteConfirmed = false)
        => Execute(() => flow.SaveCompletedChapterToSlot(stepId, slot, overwriteConfirmed));

    /// <summary>One atomic transaction for costs + outcomes, e.g. a wish or shop purchase. Never deduct and award in separate saves.</summary>
    public RoguelikeSaveResult CommitAcquisition(string stepId, IReadOnlyList<RoguelikeItemStackData> costs,
        RoguelikeAcquisition outcome, RoguelikeSaveReason reason = RoguelikeSaveReason.InventoryChanged)
        => Execute(() => inventory.Commit(stepId, draft => { draft.ConsumeItems(costs); ApplyAcquisition(draft, outcome); }, reason));

    /// <summary>Reward module supplies already resolved rewards. Award and node completion commit together exactly once.</summary>
    public RoguelikeSaveResult CompleteNode(string stepId, RoguelikeAcquisition outcome)
        => Execute(() => CompleteWithAcquisition(stepId, outcome, false));
    /// <summary>返回路线图：固定奖励入账、节点完成和待发记录清除只写一次存档。</summary>
    public RoguelikeSaveResult ReturnToRoute(string stepId) => Execute(() =>
    {
        var current = flow.Resume();
        if (!current.Succeeded) return current;
        if (current.Data.Snapshot.PendingRewardData != null) return Invalid("还有待选择的奖励，不能直接返回路线。");
        return CompleteWithAcquisition(stepId, new RoguelikeAcquisition(), false);
    });
    public RoguelikeSaveResult CompleteEvent(string stepId, RoguelikeAcquisition outcome)
        => Execute(() => CompleteWithAcquisition(stepId, outcome, true));
    private RoguelikeSaveResult CompleteWithAcquisition(string stepId, RoguelikeAcquisition outcome, bool isEvent)
    {
        var current = flow.Resume();
        if (!current.Succeeded) return current;
        if (current.Data.Flow.StepId != stepId || current.Data.Flow.Step != (isEvent ? RoguelikeFlowStep.Node : RoguelikeFlowStep.Settlement))
            return Invalid("奖励或事件上下文已变化，不能重复提交。");
        var draft = inventory.CreateDraft(current.Data.Snapshot);
        try
        {
            ApplyAcquisition(draft, outcome);
            if (!isEvent && current.Data.Snapshot.PendingNodeReward != null)
                draft.AddItems(current.Data.Snapshot.PendingNodeReward.Items);
            var result = draft.ExportSnapshot();
            if (isEvent) return flow.CompleteEvent(stepId, result);
            result.PendingRewardData = null;
            return flow.CompleteNode(stepId, result, current.Data.Snapshot.PendingNodeReward != null);
        }
        finally { draft.Close(); }
    }

    private void ApplyAcquisition(RoguelikeInventoryTransaction draft, RoguelikeAcquisition outcome)
    {
        if (outcome == null || outcome.Characters == null || outcome.Items == null || outcome.Weapons == null || outcome.Artifacts == null)
            throw new ArgumentException("获得结果及各列表不能为空。");
        foreach (int characterId in outcome.Characters)
        {
            if (!characters.TryGetInitialLimits(characterId, out float health, out float energy))
                throw new InvalidOperationException("找不到角色初始模板：" + characterId);
            draft.AddCharacter(characterId, health, energy);
        }
        draft.AddItems(outcome.Items);
        foreach (var weapon in outcome.Weapons)
        {
            if (weapon == null) throw new ArgumentException("武器获得结果不能为空。");
            draft.AddWeapon(string.IsNullOrWhiteSpace(weapon.InstanceId) ? Guid.NewGuid().ToString("N") : weapon.InstanceId, weapon.Weapon, weapon.Experience);
        }
        foreach (var artifact in outcome.Artifacts)
        {
            if (artifact == null) throw new ArgumentException("圣遗物获得结果不能为空。");
            draft.AddArtifact(string.IsNullOrWhiteSpace(artifact.InstanceId) ? Guid.NewGuid().ToString("N") : artifact.InstanceId,
                artifact.ArtifactSetID, artifact.Artifact, artifact.Experience);
        }
    }

    private RoguelikeSaveResult Execute(Func<RoguelikeSaveResult> action, bool refreshAppearance = false)
    {
        using var transaction = saves.BeginTransaction();
        if (executing) return Invalid("正在提交本次操作，请勿重入。");
        executing = true;
        try
        {
            var result = action();
            if (result.Succeeded && refreshAppearance)
            {
                var refreshed = Appearance.Refresh();
                if (!refreshed.Succeeded) Debug.LogWarning("操作已保存，外观刷新待重试：" + refreshed.Message);
            }
            if (result.Succeeded) NotifyChanged();
            return result;
        }
        catch (Exception error) { return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, error.Message, error); }
        finally { executing = false; }
    }
    private void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action listener in Changed.GetInvocationList())
            try { listener(); } catch (Exception error) { Debug.LogException(error); }
    }
    private static RoguelikeSaveResult Invalid(string message) => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
}
