using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>按宝匣星级和套装配置兑换圣遗物，消耗与产出同次提交。</summary>
public sealed class RoguelikeArtifactReliquaryService
{
    public sealed class Options
    {
        public int Owned { get; }
        public int FreeSlots { get; }
        public int MaxQuantity => Math.Min(Owned, FreeSlots);
        public IReadOnlyList<int> SetIds { get; }

        internal Options(int owned, int freeSlots, IReadOnlyList<int> setIds)
        {
            Owned = owned;
            FreeSlots = freeSlots;
            SetIds = setIds;
        }
    }

    private readonly RoguelikeInventoryService inventory;
    private readonly IRoguelikeItemCatalog items;
    private readonly ArtifactGenerationConfig artifacts;
    private readonly Dictionary<int, (int Id, int Stage)[]> setsByRarity = new();

    public RoguelikeArtifactReliquaryService(RoguelikeInventoryService inventory, IRoguelikeItemCatalog items,
        ArtifactGenerationConfig artifacts, IEnumerable<(int Id, int MinRarity, int MaxRarity, int? OpeningStage)> sets)
    {
        this.inventory = inventory;
        this.items = items;
        this.artifacts = artifacts;
        var slots = (ArtifactSlot[])Enum.GetValues(typeof(ArtifactSlot));
        var completeSets = sets.Where(set => set.OpeningStage.HasValue && slots.All(slot =>
            items.TryGetItem(RoguelikeItemIds.Artifact(set.Id, slot), out var item) && item?.ItemType == RoguelikeItemType.Artifact))
            .ToArray();
        for (int rarity = 3; rarity <= 5; rarity++)
            setsByRarity.Add(rarity, completeSets.Where(set => set.MinRarity <= rarity && rarity <= set.MaxRarity)
                .Select(set => (set.Id, set.OpeningStage.Value)).ToArray());
    }

    public Options Read(RoguelikeProgressSnapshot snapshot, string itemId)
    {
        if (snapshot == null || !items.TryGetItem(itemId, out var item) ||
            item?.Category != RoguelikeItemCatalog.ArtifactReliquaryCategory ||
            !setsByRarity.TryGetValue(item.Rarity, out var sets)) return null;
        int owned = snapshot.Inventory.Items.Find(stack => stack.ItemId == itemId)?.Quantity ?? 0;
        int freeSlots = Math.Max(0, RoguelikeInventoryData.MaxArtifacts - snapshot.Inventory.Artifacts.Count);
        var setIds = sets.Where(set => RoguelikeStageId.IsReached(set.Stage, snapshot)).Select(set => set.Id).ToArray();
        return new Options(owned, freeSlots, Array.AsReadOnly(setIds));
    }

    public RoguelikeSaveResult Open(string stepId, string itemId, int setId, int quantity, IBattleRandomSource random = null)
        => OpenWithContext(stepId, itemId, setId, quantity, random);

    internal RoguelikeSaveResult<RoguelikeFlowContext> OpenWithContext(string stepId, string itemId,
        int setId, int quantity, IBattleRandomSource random = null)
    {
        if (!items.TryGetItem(itemId, out var item) || item?.Category != RoguelikeItemCatalog.ArtifactReliquaryCategory || quantity <= 0)
            return Invalid("请选择宝匣并填写有效数量。");
        return inventory.CommitWithContext(stepId, draft =>
        {
            draft.ConsumeItems(new[] { new RoguelikeItemStackData { ItemId = itemId, Quantity = quantity } });
            var generator = new ArtifactGenerator(artifacts, random);
            var rewards = new GeneratedArtifact[quantity];
            for (int index = 0; index < quantity; index++) rewards[index] = generator.Generate(item.Rarity);
            draft.AddArtifacts(setId, rewards);
        }, RoguelikeSaveReason.InventoryChanged, snapshot =>
        {
            var options = Read(snapshot, itemId);
            if (options == null || !options.SetIds.Contains(setId)) return Invalid("该宝匣无法兑换此套装。");
            if (quantity > options.Owned) return Invalid("持有的宝匣数量不足。");
            if (quantity > options.FreeSlots)
                return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InventoryFull, "圣遗物背包余位不足。");
            return RoguelikeSaveResult.Success();
        });
    }

    private static RoguelikeSaveResult<RoguelikeFlowContext> Invalid(string message)
        => RoguelikeSaveResult<RoguelikeFlowContext>.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
}
