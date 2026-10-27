using System;
using UnityEngine;

/// <summary>
/// 背包查询与持久化入口，不依赖 UI；物品表通过 IRoguelikeItemCatalog 注入。
/// Commit 仅用于独立背包/装备操作；奖励和升级应通过 CreateDraft 合成该步骤的完整结果后统一提交。
/// 只在本服务保存成功后发布 Changed；外部业务步骤提交后应重新 Read，不把草稿视为正式持有数据。
/// </summary>
public sealed class RoguelikeInventoryService
{
    private readonly RoguelikeFlowService flow;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly IRoguelikeItemCatalog items;
    private bool committing;

    public event Action<RoguelikeInventoryView> Changed;

    /// <summary>正式物品表接入入口；不发布配表，不读取 Charts 策划源文件。</summary>
    public static RoguelikeInventoryService CreateWithItemTable(RoguelikeFlowService flow,
        IRoguelikeEquipmentCatalog equipment)
    {
        return new RoguelikeInventoryService(flow, equipment, RoguelikeItemCatalog.LoadFromStreamingAssets());
    }

    public RoguelikeInventoryService(RoguelikeFlowService flow, IRoguelikeEquipmentCatalog equipment,
        IRoguelikeItemCatalog items = null)
    {
        this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.items = items;
    }

    public RoguelikeSaveResult<RoguelikeInventoryView> Read()
    {
        var current = flow.Resume();
        if (!current.Succeeded) return RoguelikeSaveResult<RoguelikeInventoryView>.Failure(
            current.ErrorCode, current.Message, current.Exception);
        return RoguelikeSaveResult<RoguelikeInventoryView>.Success(
            new RoguelikeInventoryView(current.Data.Snapshot, equipment, items));
    }

    public RoguelikeInventoryTransaction CreateDraft(RoguelikeProgressSnapshot source)
    {
        return new RoguelikeInventoryTransaction(source, equipment, items);
    }

    /// <summary>当前 StepId 来自 flow.Resume；一次操作可同时增减材料、接收装备及调整装备关系。</summary>
    public RoguelikeSaveResult Commit(string stepId, Action<RoguelikeInventoryTransaction> operation)
        => Commit(stepId, operation, RoguelikeSaveReason.InventoryChanged);

    public RoguelikeSaveResult Commit(string stepId, Action<RoguelikeInventoryTransaction> operation, RoguelikeSaveReason reason)
    {
        using var transaction = flow.BeginSaveTransaction();
        if (reason != RoguelikeSaveReason.InventoryChanged && reason != RoguelikeSaveReason.EquipmentChanged && reason != RoguelikeSaveReason.GachaCompleted)
            return Invalid("背包提交原因必须为获得/消耗物品、装备变更或祈愿结果。");
        if (operation == null) return Invalid("背包操作不能为空。");
        if (committing) return Invalid("背包提交过程中不能重入。");
        var current = flow.Resume();
        if (!current.Succeeded) return current;
        if (string.IsNullOrWhiteSpace(stepId) || current.Data.Flow.StepId != stepId ||
            current.Data.Flow.Step == RoguelikeFlowStep.Battle ||
            current.Data.Flow.Step == RoguelikeFlowStep.ChapterCompleted)
            return Invalid("当前步骤不可修改背包，或操作上下文已经过期。");

        committing = true;
        RoguelikeInventoryTransaction draft = null;
        try
        {
            draft = CreateDraft(current.Data.Snapshot);
            operation(draft);
            var snapshot = draft.ExportSnapshot();
            var view = new RoguelikeInventoryView(snapshot, equipment, items);
            var result = flow.CommitProgressAction(stepId, snapshot, reason);
            if (!result.Succeeded) return result;
            draft.Close();
            // 单个界面订阅者异常不能将已成功落盘的操作伪装成失败，也不妨碍其他订阅者刷新。
            if (Changed != null)
                foreach (Action<RoguelikeInventoryView> listener in Changed.GetInvocationList())
                    try { listener(view); }
                    catch (Exception exception) { Debug.LogException(exception); }
            return result;
        }
        catch (Exception exception)
        {
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation,
                "背包操作未提交：" + exception.Message, exception);
        }
        finally
        {
            draft?.Close();
            committing = false;
        }
    }

    private static RoguelikeSaveResult Invalid(string message)
    {
        return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
    }
}
