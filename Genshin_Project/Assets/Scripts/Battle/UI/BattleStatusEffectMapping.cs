using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Numeric status IDs select visuals; the actual status instance and its host own their lifetime.</summary>
public sealed class BattleStatusEffectMapping
{
    private readonly Dictionary<int, BattleEffectKind?> rules = new Dictionary<int, BattleEffectKind?>();
    public int Count => rules.Count;
    public bool TryRule(int statusID, out BattleEffectKind? kind) => rules.TryGetValue(statusID, out kind);
    public bool TryGet(int statusID, out BattleEffectKind kind)
    {
        kind = default;
        if (!rules.TryGetValue(statusID, out var value) || !value.HasValue) return false;
        kind = value.Value; return true;
    }
    public static BattleStatusEffectMapping Load()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UE", BattleReactionEffectMapping.FileName));
    public static BattleStatusEffectMapping ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["ByStatusID"];
        if (sheet?.Dimension == null) throw new InvalidDataException("特效对应表缺少 ByStatusID 子表。");
        var headers = ExcelTableHeaders.For(sheet); headers.Require("StatusID", "EffectKind", "Enabled");
        string Text(int row, string key) => Convert.ToString(headers.Cell(row, key).Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        var result = new BattleStatusEffectMapping();
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string text = Text(row, "StatusID"); if (text.Length == 0) continue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0)
                throw new InvalidDataException("状态特效 StatusID 须为正整数：" + text);
            string name = Text(row, "EffectKind"), enabled = Text(row, "Enabled");
            if(name.Length==0 && headers.Optional("ResourceKey")>0 && Text(row,"ResourceKey").Length>0) continue; // Native sequence resources do not require enum entries.
            if (!Enum.TryParse(name, out BattleEffectKind kind) || Enum.GetName(typeof(BattleEffectKind), kind) != name || !BattleEffectParameters.IsSustained(kind))
                throw new InvalidDataException("状态特效须为已注册的持续类型：" + id + "/" + name);
            if (enabled != "0" && enabled != "1") throw new InvalidDataException("状态特效 Enabled 须为0或1：" + id);
            if (headers.Optional("ResolverHook") > 0 && Text(row, "ResolverHook").Length > 0)
                throw new InvalidDataException("状态特效 ResolverHook 尚未实现，请留空：" + id);
            if (result.rules.ContainsKey(id)) throw new InvalidDataException("重复状态特效 StatusID：" + id);
            result.rules.Add(id, enabled == "1" ? (BattleEffectKind?)kind : null);
        }
        return result;
    }
}
