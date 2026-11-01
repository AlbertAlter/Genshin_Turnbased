using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

/// <summary>Enemy skills select shared animation templates by ID.</summary>
public sealed class EnemyActionTemplates
{
    public const string FileName = "EnemyActionPresentation.xlsx";
    private readonly Dictionary<string, BattleHitTemplate> skills = new Dictionary<string, BattleHitTemplate>(StringComparer.Ordinal);
    public BattleHitTemplate Find(string id) => id != null && skills.TryGetValue(id, out var value) ? value : null;

    public static EnemyActionTemplates ReadFile(string path, BattleSkillPresentationTemplates templates)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["EnemyActions"] ?? throw new InvalidDataException("缺少 EnemyActions");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("EnemySkillID2", "TemplateID");
        var result = new EnemyActionTemplates();
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = h.Cell(row, "EnemySkillID2").Text.Trim();
            if (id.Length == 0) continue;
            string templateId = h.Cell(row, "TemplateID").Text.Trim();
            var template = templates.Get(templateId) ?? throw new InvalidDataException($"敌人技能 {id} 引用了不存在的模板 {templateId}");
            if (result.skills.ContainsKey(id)) throw new InvalidDataException("敌人技能模板重复：" + id);
            result.skills.Add(id, template);
        }
        return result;
    }
}
