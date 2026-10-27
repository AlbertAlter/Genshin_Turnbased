using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景外养成入口。UI 先用草稿 Preview/GetAdditionalMaterialLimit 或 PreviewSkillUpgrade，
/// 确认后带当前 StepId 提交；天赋调用 draft.UpgradeSkill(characterId, skillIndex, targetBaseLevel)。
/// 节点系统注入突破门槛；通过门槛后的 ReconcileUnlockedProgress 可与节点结果一并提交。
/// </summary>
public sealed class RoguelikeUpgradeService
{
    private readonly RoguelikeFlowService flow;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly RoguelikeItemCatalog items;
    private readonly LevelUpRequirementsTable requirements;
    private readonly ArtifactGenerationConfig artifactConfig;
    private readonly IRoguelikeUpgradeGate gate;
    private readonly IBattleRandomSource randomSource;
    private bool committing;
    private string retryStep;
    private IBattleRandomSource retrySource;
    private RetryRandom retryRandom;

    public event Action<RoguelikeProgressSnapshot> Changed;

    /// <summary>仅从正式运行时目录读取，不同步或改写任何配表。</summary>
    public static RoguelikeUpgradeService CreateFromStreamingAssets(RoguelikeFlowService flow,
        IRoguelikeEquipmentCatalog equipment, IRoguelikeUpgradeGate gate, IBattleRandomSource randomSource = null)
        => new RoguelikeUpgradeService(flow, equipment, RoguelikeItemCatalog.LoadFromStreamingAssets(),
            LevelUpRequirementsLoader.LoadFromStreamingAssets(), ArtifactConfigurationLoader.LoadFromStreamingAssets(),
            gate, randomSource);

    public RoguelikeUpgradeService(RoguelikeFlowService flow, IRoguelikeEquipmentCatalog equipment,
        RoguelikeItemCatalog items, LevelUpRequirementsTable requirements, ArtifactGenerationConfig artifactConfig,
        IRoguelikeUpgradeGate gate, IBattleRandomSource randomSource = null)
    {
        this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        this.requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        this.artifactConfig = artifactConfig ?? throw new ArgumentNullException(nameof(artifactConfig));
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        this.randomSource = randomSource;
    }

    /// <summary>可与奖励/节点处理合成一个完整快照；草稿本身不保存、不通知界面。</summary>
    public RoguelikeUpgradeTransaction CreateDraft(RoguelikeProgressSnapshot source)
        => CreateDraft(source, randomSource ?? BattleRandom.Source);

    private RoguelikeUpgradeTransaction CreateDraft(RoguelikeProgressSnapshot source, IBattleRandomSource random)
    {
        int unlocked = gate.GetUnlockedAscensions(RoguelikeSaveJson.DeepClone(source));
        return new RoguelikeUpgradeTransaction(source, equipment, items, requirements, artifactConfig, unlocked, random);
    }

    /// <summary>升级、命座消耗和实例变化只保存一次；保存失败不发布 Changed，不提交部分结果。</summary>
    public RoguelikeSaveResult Commit(string stepId, Action<RoguelikeUpgradeTransaction> operation)
    {
        if (operation == null || committing) return Invalid("养成操作不能为空，也不能在提交时重入。");
        var current = flow.Resume();
        if (!current.Succeeded) return current;
        if (string.IsNullOrWhiteSpace(stepId) || current.Data.Flow.StepId != stepId ||
            current.Data.Flow.Step == RoguelikeFlowStep.Battle ||
            current.Data.Flow.Step == RoguelikeFlowStep.ChapterCompleted)
            return Invalid("当前步骤不能养成，或操作上下文已过期。");

        committing = true;
        RoguelikeUpgradeTransaction draft = null;
        try
        {
            var source = randomSource ?? BattleRandom.Source;
            if (retryStep != stepId || !ReferenceEquals(retrySource, source))
            {
                retryStep = stepId;
                retrySource = source;
                retryRandom = new RetryRandom(source);
            }
            retryRandom.Rewind();
            draft = CreateDraft(current.Data.Snapshot, retryRandom);
            operation(draft);
            var snapshot = draft.ExportSnapshot();
            var result = flow.CommitProgressAction(stepId, snapshot, RoguelikeSaveReason.CharacterUpgraded);
            if (!result.Succeeded) return result;
            draft.Close();
            retryRandom = null; retryStep = null; retrySource = null;
            if (Changed != null)
                foreach (Action<RoguelikeProgressSnapshot> listener in Changed.GetInvocationList())
                    try { listener(RoguelikeSaveJson.DeepClone(snapshot)); }
                    catch (Exception exception) { Debug.LogException(exception); }
            return result;
        }
        catch (Exception exception)
        {
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation,
                "养成操作未提交：" + exception.Message, exception);
        }
        finally
        {
            draft?.Close();
            committing = false;
        }
    }

    private static RoguelikeSaveResult Invalid(string message)
        => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);

    /// <summary>
    /// 只缓存统一随机入口的实际抽取，不创建新随机流。同一服务同一步骤保存失败后重试复用值，
    /// 避免 I/O 失败导致反复重抽词条；这不是跨进程的完整种子恢复协议。
    /// </summary>
    private sealed class RetryRandom : IBattleRandomSource
    {
        private readonly IBattleRandomSource source;
        private readonly List<float> values = new List<float>();
        private int cursor;
        public RetryRandom(IBattleRandomSource source) { this.source = source; }
        public void Rewind() { cursor = 0; }
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(minInclusive + Math.Floor(NextFloat01() * ((long)maxExclusive - minInclusive)));
        }
        public float NextFloat01()
        {
            if (cursor == values.Count)
            {
                float value = source.NextFloat01();
                if (float.IsNaN(value) || value < 0f || value >= 1f)
                    throw new InvalidOperationException("统一随机源必须返回 [0,1) 范围的数值。");
                values.Add(value);
            }
            return values[cursor++];
        }
    }
}
