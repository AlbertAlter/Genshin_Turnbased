using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>两处养成入口共用的选料与预览状态。清除或关闭时不修改档案。</summary>
public sealed class RoguelikeRosterUpgradeControls
{
    private readonly RoguelikeRosterSession session;
    private readonly Dictionary<string, int> quantities = new();
    private readonly List<string> refinementCopies = new();
    private RoguelikeProgressSnapshot previewSource;
    private RoguelikeUpgradePreview preview;
    public RoguelikeUpgradeTarget Target { get; }
    public bool IsRefinement { get; }
    public bool HasSelection => IsRefinement ? refinementCopies.Count != 0 : quantities.Count != 0;
    public bool IncludesCopy(string id) => refinementCopies.Contains(id);
    public int Selected(string id) => quantities.TryGetValue(id, out int count) ? count : 0;

    public RoguelikeRosterUpgradeControls(RoguelikeRosterSession session, RoguelikeUpgradeTarget target, bool refinement = false)
    {
        this.session = session;
        Target = target;
        IsRefinement = refinement;
    }

    private RoguelikeItemStackData[] Costs() => quantities.Select(entry => new RoguelikeItemStackData
        { ItemId = entry.Key, Quantity = entry.Value }).ToArray();

    public RoguelikeUpgradePreview Preview()
    {
        if (preview == null || !ReferenceEquals(previewSource, session.Snapshot))
        {
            preview = session.PreviewLevel(Target, Costs());
            previewSource = session.Snapshot;
        }
        return preview;
    }

    public int PreviewRefinement() => RoguelikeUpgradeTransaction.PreviewWeaponRefinement(session.Snapshot, Target.InstanceId, refinementCopies);

    public int AdditionalLimit(string id)
    {
        var selection = session.PreviewLevel(Target, Costs());
        return RoguelikeUpgradeTransaction.GetAdditionalMaterialLimit(session.Snapshot, session.Items, Target.Kind, selection, id);
    }

    public bool Adjust(string id, int delta)
    {
        int old = Selected(id);
        if (delta > 0)
            delta = Math.Min(delta, AdditionalLimit(id));
        int next = Math.Max(0, old + delta);
        if (next == old) return false;
        if (next > 0) quantities[id] = next;
        else quantities.Remove(id);
        preview = null;
        return true;
    }

    public void QuickAdd(IEnumerable<RoguelikeItemDefinition> materials)
    {
        foreach (var material in materials.OrderByDescending(item => item.ExperienceValue))
            Adjust(material.ItemId, int.MaxValue);
    }

    public void ToggleCopy(string id)
    {
        if (refinementCopies.Remove(id)) return;
        var candidate = refinementCopies.Append(id).ToArray();
        RoguelikeUpgradeTransaction.PreviewWeaponRefinement(session.Snapshot, Target.InstanceId, candidate);
        refinementCopies.Add(id);
    }

    public RoguelikeSaveResult Commit()
    {
        if (IsRefinement) return session.Upgrade(tx => tx.RefineWeapon(Target.InstanceId, refinementCopies));
        var costs = Costs();
        return session.Upgrade(tx => tx.Upgrade(Target, costs));
    }
}
