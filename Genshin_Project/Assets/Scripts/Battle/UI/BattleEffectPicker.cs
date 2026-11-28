using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Skill-VFX metadata adapter for the shared authoring picker.</summary>
public sealed class BattleEffectPicker
{
    private readonly UIEffectPicker picker = new UIEffectPicker();
    public bool CloseRequested => picker.CloseRequested;
    public BattleEffectPicker()
    {
        var rows = new List<UIEffectPicker.Entry>();
        foreach (var e in BattleEffectPreviewCatalog.Entries)
            rows.Add(new UIEffectPicker.Entry { Key = e.Kind.ToString(), Label = e.Label, Detail = e.Detail,
                Category = BattleEffectPreviewCatalog.CategoryLabel(e.Category), Type = e.Sustained ? "持续状态" : "单次行动",
                OwnerKey = e.OwnerKey, OwnerLabel = e.Category == BattleEffectCategory.Common ? "公共" : e.OwnerName + " " + e.OwnerID,
                SearchText = e.SearchText });
        picker.SetEntries(rows, new[] { "全部", "角色", "敌人", "公共" }, new[] { "全部类型", "持续状态", "单次行动" });
    }
    public void Open(BattleEffectKind current) => picker.Open(current.ToString());
    public bool Draw(Rect bounds, Font font, BattleEffectKind current, out BattleEffectKind selection)
    {
        bool accepted = picker.Draw(bounds, font, current.ToString(), out string key);
        selection = accepted ? (BattleEffectKind)Enum.Parse(typeof(BattleEffectKind), key) : current; return accepted;
    }
}
