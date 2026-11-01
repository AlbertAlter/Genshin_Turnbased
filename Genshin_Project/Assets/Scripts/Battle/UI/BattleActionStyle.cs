using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

public sealed class BattleActionStyle
{
    public const string FileName = "ActionPrompt.xlsx";
    public int MinTextCharacters { get; private set; } = 8;
    public float Reveal { get; private set; }
    public float Hold { get; private set; }
    public float Fade { get; private set; }
    public float ParticleSeconds { get; private set; }
    public static BattleActionStyle ReadFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(file);
        var style = new BattleActionStyle();
        var parameters = book.Workbook.Worksheets["Parameters"] ?? throw new InvalidDataException("缺少 Parameters");
        var columns = ExcelTableHeaders.For(parameters); columns.Require("Key", "Value");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = columns.DataStartRow; row <= parameters.Dimension.End.Row; row++)
        {
            string key = columns.Cell(row, "Key").Text.Trim();
            if (key.Length != 0) values.Add(key, columns.Cell(row, "Value").Text.Trim());
        }
        style.Reveal = Seconds(values["RevealSeconds"], "RevealSeconds");
        style.Hold = Seconds(values["HoldSeconds"], "HoldSeconds");
        style.Fade = Seconds(values["FadeSeconds"], "FadeSeconds");
        if (!values.TryGetValue("MinTextCharacters", out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1)
            throw new InvalidDataException("MinTextCharacters 须为正整数");
        style.MinTextCharacters = count;
        style.ParticleSeconds = Seconds(values["StatusParticleSeconds"], "StatusParticleSeconds");
        return style;
    }
    private static float Seconds(string value, string key)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ||
            !(seconds >= 0) || float.IsInfinity(seconds)) throw new InvalidDataException("行动表现时间无效：" + key);
        return seconds;
    }
}
