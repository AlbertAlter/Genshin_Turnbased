using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Skill/status effect numeric IDs select one-shot art; committed results still own targets and timing.</summary>
public sealed class BattleEffectIDMapping
{
    private readonly Dictionary<int, BattleEffectKind?> rules = new Dictionary<int, BattleEffectKind?>();
    public int Count => rules.Count;
    // A configured disabled row differs from an absent row: it also suppresses legacy UE hooks.
    public bool TryRule(int id, out BattleEffectKind? kind) => rules.TryGetValue(id, out kind);
    public static BattleEffectIDMapping Load()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UE", BattleReactionEffectMapping.FileName));
    public static BattleEffectIDMapping ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["ByEffectID"];
        if (sheet?.Dimension == null) throw new InvalidDataException("特效对应表缺少 ByEffectID 子表。");
        var headers = ExcelTableHeaders.For(sheet); headers.Require("EffectID", "EffectKind", "Enabled");
        string Text(int row, string key) => Convert.ToString(headers.Cell(row, key).Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        var result = new BattleEffectIDMapping();
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string text = Text(row, "EffectID"); if (text.Length == 0) continue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0)
                throw new InvalidDataException("效果特效 EffectID 须为正整数：" + text);
            string name = Text(row, "EffectKind"), enabled = Text(row, "Enabled");
            if(name.Length==0 && headers.Optional("ResourceKey")>0 && Text(row,"ResourceKey").Length>0) continue; // Validated by the string-key table catalog.
            if (!Enum.TryParse(name, out BattleEffectKind kind) || Enum.GetName(typeof(BattleEffectKind), kind) != name || BattleEffectParameters.IsSustained(kind))
                throw new InvalidDataException("效果特效须为已注册的单次类型：" + id + "/" + name);
            if (enabled != "0" && enabled != "1") throw new InvalidDataException("效果特效 Enabled 须为0或1：" + id);
            if (headers.Optional("ResolverHook") > 0 && Text(row, "ResolverHook").Length > 0)
                throw new InvalidDataException("效果特效 ResolverHook 尚未实现，请留空：" + id);
            if (result.rules.ContainsKey(id)) throw new InvalidDataException("重复效果特效 EffectID：" + id);
            result.rules.Add(id, enabled == "1" ? (BattleEffectKind?)kind : null);
        }
        return result;
    }
    public static int NumericID(string id2)
    {
        var data = DataManager.Instance;
        if (data == null || string.IsNullOrEmpty(id2)) return 0;
        // Only read the already loaded runtime data. No string-key matching against authoring labels.
        if (data.SkillEffectDict.TryGetValue(id2, out var skill)) return skill.SkillEffectID;
        if (data.StatusEffectDict.TryGetValue(id2, out var status)) return status.StatusEffectID;
        if (data.EnemySkillEffectDict.TryGetValue(id2, out var enemy)) return enemy.SkillEffectID;
        return 0;
    }
}
