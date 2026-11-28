using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Reaction-only native VFX mapping. Runtime reads the published table; combat rules remain independent.</summary>
public sealed class BattleReactionEffectMapping
{
    public const string FileName = "BattleEffectMapping.xlsx";
    private readonly Dictionary<ReactionType, BattleEffectKind?> rules = new Dictionary<ReactionType, BattleEffectKind?>();
    public int Count => rules.Count;
    public bool TryGet(ReactionType reaction, out BattleEffectKind kind)
    {
        kind = default;
        if (!rules.TryGetValue(reaction, out var mapped) || !mapped.HasValue) return false;
        kind = mapped.Value; return true;
    }
    public static BattleReactionEffectMapping Load()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UE", FileName));
    public static BattleReactionEffectMapping ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["ByReactionType"];
        if (sheet?.Dimension == null) throw new InvalidDataException(FileName + " 缺少 ByReactionType 子表。");
        var headers = ExcelTableHeaders.For(sheet); headers.Require("ReactionType", "EffectKind", "Enabled");
        string Text(int row, string field) => Convert.ToString(headers.Cell(row, field).Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        var result = new BattleReactionEffectMapping();
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string key = Text(row, "ReactionType"); if (key.Length == 0) continue;
            if (!Enum.TryParse(key, out ReactionType reaction) || reaction == ReactionType.None || Enum.GetName(typeof(ReactionType), reaction) != key)
                throw new InvalidDataException("未知反应特效分类：" + key);
            string name = Text(row, "EffectKind"), enabled = Text(row, "Enabled");
            if (!Enum.TryParse(name, out BattleEffectKind kind) || Enum.GetName(typeof(BattleEffectKind), kind) != name || !BattleEffectParameters.IsExplosion(kind))
                throw new InvalidDataException("反应特效当前须选择公共瞬时爆炸：" + key + "/" + name);
            if (enabled != "0" && enabled != "1") throw new InvalidDataException("反应特效 Enabled 须为0或1：" + key);
            if (headers.Optional("ResolverHook") > 0 && Text(row, "ResolverHook").Length > 0)
                throw new InvalidDataException("反应特效 ResolverHook 尚未实现，请留空：" + key);
            if (result.rules.ContainsKey(reaction)) throw new InvalidDataException("重复反应特效：" + key);
            result.rules.Add(reaction, enabled == "1" ? (BattleEffectKind?)kind : null);
        }
        return result;
    }
}

/// <summary>Use committed reaction damage, including shielded/immune hits, and real hit IDs rather than nearby slots or visual lanes.</summary>
public static class BattleReactionEffectPlan
{
    public static bool TryGetReaction(BattlePresentationResult result, out ReactionType reaction)
    {
        reaction = ReactionType.None;
        if (!(result?.Data is DamageEventData damage) || damage.DamageType != "Reaction") return false;
        var source = damage.Source;
        if (source.CountsAsHit || source.ReactionType == ReactionType.None) return false;
        reaction = source.ReactionType; return true;
    }
    public static (long Session, long Hit, ReactionType Reaction, string Causes) Key(BattlePresentationResult result)
    {
        if (!TryGetReaction(result, out var reaction) || result.Header == null)
            throw new ArgumentException("反应特效需要真实反应伤害结果。", nameof(result));
        long hit = result.Header.HitId != 0 ? result.Header.HitId : result.RequestID != 0 ? result.RequestID : -result.Header.EventId;
        var causes = new List<long>(result.Header.CauseEventIds ?? Array.Empty<long>()); causes.Sort();
        return (result.Header.SessionId, hit, reaction, string.Join(",", causes));
    }
    public static Dictionary<(long Session, long Hit, ReactionType Reaction, string Causes), List<BattlePresentationResult>> Group(IEnumerable<BattlePresentationResult> results)
    {
        var groups = new Dictionary<(long, long, ReactionType, string), List<BattlePresentationResult>>();
        var events = new HashSet<(long, long)>();
        foreach (var result in results)
        {
            if (!TryGetReaction(result, out _) || !((DamageEventData)result.Data).HitLanded ||
                !events.Add((result.Header.SessionId, result.Header.EventId))) continue;
            var key = Key(result);
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<BattlePresentationResult>());
            group.Add(result);
        }
        return groups;
    }
}
