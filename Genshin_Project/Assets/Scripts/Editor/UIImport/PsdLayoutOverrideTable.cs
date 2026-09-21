using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

internal sealed class PsdLayoutOverride
{
    public string Path;
    public string Name;
    public string NodeType;
    public string Anchor;
    public float? OffsetX;
    public float? OffsetY;
    public float? Width;
    public float? Height;
    public string SizeMode;
    public string AssetMode;
    public string ExistingAssetPath;
    public string TemplateID;
    public string TemplateRole;
    public string LayoutVariant;
    public string BindingKey;
    public bool? AllowCrop;
    public string SliceBorder;
}

internal sealed class PsdLayoutOverrideTable
{
    private readonly Dictionary<string, PsdLayoutOverride> rows =
        new Dictionary<string, PsdLayoutOverride>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> matched =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static PsdLayoutOverrideTable LoadAdjacentTo(string jsonPath)
    {
        var table = new PsdLayoutOverrideTable();
        string directory = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        string path = Path.Combine(directory, "layout_overrides.csv");
        if (!File.Exists(path)) return table;

        string[] lines = File.ReadAllLines(path, new UTF8Encoding(true));
        if (lines.Length == 0) return table;
        List<string> headers = ParseCsvLine(lines[0]);
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;
            List<string> values = ParseCsvLine(lines[lineIndex]);
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < headers.Count; index++)
                cells[headers[index]] = index < values.Count ? values[index].Trim() : string.Empty;

            string nodePath = Get(cells, "Path");
            if (string.IsNullOrWhiteSpace(nodePath))
            {
                Debug.LogWarning($"PSD UI 覆盖表第 {lineIndex + 1} 行没有 Path，已忽略。");
                continue;
            }

            table.rows[nodePath] = new PsdLayoutOverride
            {
                Path = nodePath,
                Name = Get(cells, "Name"),
                NodeType = Get(cells, "NodeType"),
                Anchor = Get(cells, "Anchor"),
                OffsetX = ParseFloat(Get(cells, "OffsetX")),
                OffsetY = ParseFloat(Get(cells, "OffsetY")),
                Width = ParseFloat(Get(cells, "Width")),
                Height = ParseFloat(Get(cells, "Height")),
                SizeMode = Get(cells, "SizeMode"),
                AssetMode = Get(cells, "AssetMode"),
                ExistingAssetPath = Get(cells, "ExistingAssetPath"),
                TemplateID = Get(cells, "TemplateID"),
                TemplateRole = Get(cells, "TemplateRole"),
                LayoutVariant = Get(cells, "LayoutVariant"),
                BindingKey = Get(cells, "BindingKey"),
                AllowCrop = ParseBool(Get(cells, "AllowCrop")),
                SliceBorder = Get(cells, "SliceBorder"),
            };
        }
        return table;
    }

    public PsdLayoutOverride Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!rows.TryGetValue(path, out PsdLayoutOverride value)) return null;
        matched.Add(path);
        return value;
    }

    public void WarnUnmatchedRows()
    {
        foreach (string path in rows.Keys)
        {
            if (!matched.Contains(path))
                Debug.LogWarning($"PSD UI 覆盖项没有匹配到图层，可能是 PSD 改名或层级变化：{path}");
        }
    }

    private static string Get(Dictionary<string, string> cells, string key)
    {
        return cells.TryGetValue(key, out string value) ? value : string.Empty;
    }

    private static float? ParseFloat(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : (float?)null;
    }

    private static bool? ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (bool.TryParse(value, out bool result)) return result;
        if (value == "1" || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value == "0" || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (current == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (current == ',' && !quoted)
            {
                values.Add(cell.ToString());
                cell.Length = 0;
            }
            else
            {
                cell.Append(current);
            }
        }
        values.Add(cell.ToString());
        return values;
    }
}
