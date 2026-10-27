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
    private bool executing;
    public event Action Changed;

    public RoguelikeGameService(RoguelikeSaveService saves, IRoguelikeRouteRules routes,
        IRoguelikeEquipmentCatalog equipment, IRoguelikeItemCatalog items,
        IRoguelikeCharacterAcquisitionCatalog characters)
    {
        this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
        flow = new RoguelikeFlowService(saves, routes);
        inventory = new RoguelikeInventoryService(flow, equipment, items);
    }

    /// <summary>Uses loaded DataManager templates and runtime Items.xlsx. Does not initialize data, create a run or touch scenes.</summary>
    public static RoguelikeGameService CreateFromStreamingAssets(DataManager data, IRoguelikeRouteRules routes,
        RoguelikeSavePaths paths = null)
    {
        if (data == null || !data.IsLoaded) throw new InvalidOperationException("创建肉鸽入口前必须先加载 DataManager。");
        var saves = new RoguelikeSaveService(new RoguelikeSaveRepository(paths ?? new RoguelikeSavePaths()));
        return new RoguelikeGameService(saves, routes, new DataManagerRoguelikeEquipmentCatalog(data),
            RoguelikeItemCatalog.LoadFromStreamingAssets(), new DataManagerRoguelikeCharacterAcquisitionCatalog(data));
    }

    public RoguelikeSaveResult<RoguelikeFlowContext> Continue() => flow.Resume();
    public RoguelikeSaveResult<RoguelikeInventoryView> ReadInventory() => inventory.Read();
    public RoguelikeSaveResult<RoguelikeSaveSummary> ReadActiveSummary() => saves.GetActiveRunSummary();
    public RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>> ListChapterSlots(int chapter) => saves.ListChapterSlots(chapter);
    public RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initial) => Execute(() => flow.StartNewGame(initial));
    public RoguelikeSaveResult StartFromChapterSlot(int chapter, int slot) => Execute(() => flow.StartFromChapterSlot(chapter, slot));
    public RoguelikeSaveResult SaveCurrentProgress() => Execute(saves.SaveCurrentProgress);
    public RoguelikeSaveResult RestoreActiveBackup(bool confirmed) => Execute(() => saves.RestoreActiveRunBackup(confirmed));
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
                for (int i = 0; i < quantity; i++) draft.AddWeapon(definition.ReferenceID);
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
    public RoguelikeSaveResult RecordBattleVictory(string stepId) => Execute(() => flow.RecordBattleVictory(stepId));
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
            var result = draft.ExportSnapshot();
            if (isEvent) return flow.CompleteEvent(stepId, result);
            result.PendingRewardData = null;
            return flow.CompleteNode(stepId, result);
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

    private RoguelikeSaveResult Execute(Func<RoguelikeSaveResult> action)
    {
        using var transaction = saves.BeginTransaction();
        if (executing) return Invalid("正在提交本次操作，请勿重入。");
        executing = true;
        try
        {
            var result = action();
            if (result.Succeeded && Changed != null)
                foreach (Action listener in Changed.GetInvocationList())
                    try { listener(); } catch (Exception error) { Debug.LogException(error); }
            return result;
        }
        catch (Exception error) { return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, error.Message, error); }
        finally { executing = false; }
    }
    private static RoguelikeSaveResult Invalid(string message) => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
}
