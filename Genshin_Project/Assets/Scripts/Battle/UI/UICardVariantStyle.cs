using System.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public sealed class UICardVariantStyle
{
    public const string FileName = "CardVariant.xlsx";
    public float Duration;
    public bool Smooth;
    public static UICardVariantStyle Load() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    public static UICardVariantStyle ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["Parameters"];
        if (sheet?.Dimension == null) throw new InvalidDataException("CardVariant 缺少 Parameters 子表。");
        var headers = ExcelTableHeaders.For(sheet); headers.Require("Key", "Value");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string key = headers.Cell(row, "Key").Text.Trim();
            if (key.Length == 0) continue;
            if (values.ContainsKey(key)) throw new InvalidDataException("差分渐变参数重复：" + key);
            values.Add(key, Convert.ToString(headers.Cell(row, "Value").Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
        }
        float Number(string key)
        {
            if (!values.TryGetValue(key, out var text) || !float.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("差分渐变参数无效：" + key);
            return value;
        }
        float duration = Number("DurationSeconds"), smooth = Number("SmoothStep");
        if (duration <= 0 || duration > 10 || (smooth != 0 && smooth != 1))
            throw new InvalidDataException("差分渐变时长应在 (0,10]，SmoothStep 为 0 或 1。");
        return new UICardVariantStyle { Duration = duration, Smooth = smooth == 1 };
    }
}
