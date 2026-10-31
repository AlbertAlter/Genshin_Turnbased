using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public sealed class BattleSkillPresentationTemplates
{
    public const string FileName = "SkillPresentationTemplates.xlsx";
    private readonly Dictionary<(int, string), BattleHitTemplate> templates = new Dictionary<(int, string), BattleHitTemplate>();
    public BattleHitTemplate Get(int weapon, string damageType) => templates.TryGetValue((weapon, damageType ?? ""), out var value) ? value : null;
    public static BattleSkillPresentationTemplates Load() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    public static BattleSkillPresentationTemplates ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["Templates"];
        if (sheet?.Dimension == null) throw new InvalidDataException("缺少 Templates 子表。");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("TemplateID", "WeaponType", "DamageType", "HitTimesSeconds", "RepeatIntervalSeconds", "ProjectileSeconds");
        var result = new BattleSkillPresentationTemplates(); var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string Text(string key) => Convert.ToString(h.Cell(row, key).Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            string id = Text("TemplateID"); if (id.Length == 0) continue;
            double Number(string raw)
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n) || n < 0)
                    throw new InvalidDataException($"模板{ id }存在非法时间：{raw}");
                return n;
            }
            string type = Text("DamageType");
            if (!int.TryParse(Text("WeaponType"), out int weapon) || weapon < 0 || weapon > 4 || (type != "Normal" && type != "Heavy"))
                throw new InvalidDataException("模板武器须为0..4，DamageType须为Normal/Heavy：" + id);
            if (!ids.Add(id) || result.templates.ContainsKey((weapon, type))) throw new InvalidDataException("模板编号或武器/攻击类型重复：" + id);
            var times = new List<double>(); foreach (string raw in Text("HitTimesSeconds").Split(';')) times.Add(Number(raw));
            result.templates.Add((weapon, type), new BattleHitTemplate(id, times, Number(Text("RepeatIntervalSeconds")), Number(Text("ProjectileSeconds"))));
        }
        if (result.templates.Count != 10) throw new InvalidDataException("通用模板须包含五种武器的Normal/Heavy，共10项。");
        return result;
    }
}
