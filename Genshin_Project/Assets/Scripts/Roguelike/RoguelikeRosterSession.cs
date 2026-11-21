using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>角色和背包共用的养成会话，预览与正式提交共用养成规则。</summary>
public sealed class RoguelikeRosterSession
{
    private readonly RoguelikeFlowService flow;
    private readonly RoguelikeInventoryService inventory;
    private readonly RoguelikeUpgradeService upgrades;
    private readonly IRoguelikeUpgradeGate gate;
    private readonly Lazy<RoguelikeArtifactReliquaryService> reliquaries;
    private readonly RoguelikeProgressSnapshot archive;
    public DataManager Data { get; }
    public RoguelikeItemCatalog Items { get; }
    public ArtifactGenerationConfig Artifacts { get; }
    internal RosterTextCatalog Texts { get; }
    public RoguelikeArtifactReliquaryService Reliquaries => reliquaries.Value;
    public RoguelikeFlowContext Context { get; private set; }
    public RoguelikeProgressSnapshot Snapshot => Context?.Snapshot;
    public bool IsArchive => archive != null;
    public bool CanEdit => !IsArchive && Context?.Flow != null && Context.Flow.Step != RoguelikeFlowStep.Battle &&
                           Context.Flow.Step != RoguelikeFlowStep.ChapterCompleted;

    public RoguelikeRosterSession(DataManager data, RoguelikeFlowService flow, IRoguelikeUpgradeGate gate,
        RoguelikeProgressSnapshot readOnlySnapshot = null)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        archive = readOnlySnapshot == null ? null : RoguelikeSaveJson.DeepClone(readOnlySnapshot);
        var equipment = new DataManagerRoguelikeEquipmentCatalog(data);
        Items = data.GetItemCatalog();
        Artifacts = data.GetArtifactConfig();
        inventory = new RoguelikeInventoryService(flow, equipment, Items);
        Texts = data.GetRosterText();
        reliquaries = new Lazy<RoguelikeArtifactReliquaryService>(() =>
        {
            var wishes = data.GetWishCatalog();
            return new RoguelikeArtifactReliquaryService(inventory, Items, Artifacts, Texts.SetVersions.Select(entry =>
            {
                var set = Texts.Sets[entry.Key];
                return (entry.Key, set.MinRarity, set.MaxRarity, wishes.OpeningStageForVersion(entry.Value));
            }));
        });
        upgrades = new RoguelikeUpgradeService(flow, equipment, Items,
            data.GetLevelRequirements(), Artifacts, gate);
    }

    public RoguelikeSaveResult Reload()
    {
        if (archive != null)
        {
            Context = new RoguelikeFlowContext { Snapshot = RoguelikeSaveJson.DeepClone(archive) };
            return RoguelikeSaveResult.Success();
        }
        var result = flow.Resume();
        Context = result.Succeeded ? result.Data : null;
        return result;
    }

    public RoguelikeUpgradeLimits Limits => new RoguelikeUpgradeLimits(gate.GetUnlockedAscensions(Snapshot));
    public int Count(string id) => Snapshot.Inventory.Items.Find(item => item.ItemId == id)?.Quantity ?? 0;
    public RoguelikeCharacterProgressData Character(int id) => Snapshot.Characters.Find(c => c.CharacterID == id);
    public RoguelikeWeaponInstanceData Weapon(string id) => Snapshot.Inventory.Weapons.Find(w => w.InstanceId == id);
    public RoguelikeArtifactInstanceData Artifact(string id) => Snapshot.Inventory.Artifacts.Find(a => a.InstanceId == id);
    public RoguelikeCharacterProgressData Owner(string id) => Snapshot.Characters.Find(c =>
        c.EquippedWeaponInstanceId == id || c.EquippedArtifacts.Exists(a => a.ArtifactInstanceId == id));

    public T Preview<T>(Func<RoguelikeUpgradeTransaction, T> query)
    {
        var transaction = upgrades.CreateDraft(Snapshot);
        try { return query(transaction); }
        finally { transaction.Close(); }
    }

    internal RoguelikeUpgradePreview PreviewLevel(RoguelikeUpgradeTarget target, IReadOnlyList<RoguelikeItemStackData> selected)
        => upgrades.PreviewLevel(Snapshot, Limits, target, selected);

    internal RoguelikeSkillUpgradePreview PreviewSkillUpgrade(int characterId, int skillIndex, int targetBaseLevel)
        => upgrades.PreviewSkillUpgrade(Snapshot, Limits, characterId, skillIndex, targetBaseLevel);

    public RoguelikeSaveResult Upgrade(Action<RoguelikeUpgradeTransaction> command)
        => !CanEdit ? ReadOnly() : AcceptCommit(upgrades.CommitWithContext(Context.Flow.StepId, command));

    public RoguelikeSaveResult Equipment(Action<RoguelikeInventoryTransaction> command)
        => !CanEdit ? ReadOnly() : AcceptCommit(inventory.CommitWithContext(Context.Flow.StepId, command, RoguelikeSaveReason.EquipmentChanged));

    public RoguelikeSaveResult OpenReliquaries(string stepId, string itemId, int setId, int quantity)
        => !CanEdit ? ReadOnly() : AcceptCommit(Reliquaries.OpenWithContext(stepId, itemId, setId, quantity));

    private static RoguelikeSaveResult ReadOnly() => RoguelikeSaveResult.Failure(
        RoguelikeSaveErrorCode.InvalidOperation, "当前档案只读。");

    public RoguelikeCharacterProgressData PreviewEquipment(int characterId, Action<RoguelikeInventoryTransaction> command)
    {
        var draft = inventory.CreateDraft(Snapshot);
        try
        {
            command(draft);
            return draft.PreviewCharacter(characterId);
        }
        finally { draft.Close(); }
    }

    private RoguelikeSaveResult AcceptCommit(RoguelikeSaveResult<RoguelikeFlowContext> result)
    {
        if (result.Succeeded) Context = result.Data;
        return result;
    }
}

/// <summary>独立场景使用档案明确保存的解锁进度。旧档保留已实际获得的突破阶段。</summary>
public sealed class RoguelikeSavedUpgradeGate : IRoguelikeUpgradeGate
{
    public int GetUnlockedAscensions(RoguelikeProgressSnapshot snapshot)
    {
        if (snapshot.UnlockedAscensions.HasValue) return snapshot.UnlockedAscensions.Value;
        return snapshot.Characters.Select(c => c.Ascension)
            .Concat(snapshot.Inventory.Weapons.Select(w => w.Weapon.Ascension)).DefaultIfEmpty(0).Max();
    }
}
