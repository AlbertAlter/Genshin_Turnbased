using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal sealed class BattlefieldUiLayoutRow
{
    public string Path;
    public string Name;
    public string NodeType;
    public string Anchor;
    public float OffsetX;
    public float OffsetY;
    public float Width;
    public float Height;
    public string SizeMode;
    public string AssetMode;
    public string StreamingAssetPath;
    public string TemplateID;
    public string TemplateRole;
    public string LayoutVariant;
    public string BindingKey;
    public bool AllowCrop;
    public string Text;
}

internal static class BattlefieldUiLayoutTable
{
    public static List<BattlefieldUiLayoutRow> Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("UI layout table not found.", path);
        string[] lines = File.ReadAllLines(path, new UTF8Encoding(true));
        if (lines.Length == 0) throw new InvalidDataException("UI layout table is empty: " + path);

        List<string> headers = ParseCsvLine(lines[0]);
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++) columns[headers[index].Trim()] = index;
        string[] required = { "Path", "Name", "NodeType", "Anchor", "OffsetX", "OffsetY", "Width", "Height",
            "SizeMode", "AssetMode", "StreamingAssetPath", "AllowCrop" };
        foreach (string header in required)
            if (!columns.ContainsKey(header)) throw new InvalidDataException("UI layout table is missing column: " + header);

        var result = new List<BattlefieldUiLayoutRow>();
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;
            List<string> cells = ParseCsvLine(lines[lineIndex]);
            string Get(string key) => columns.TryGetValue(key, out int column) && column < cells.Count
                ? cells[column].Trim() : string.Empty;
            float Number(string key) => float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture,
                out float value) ? value : 0f;
            result.Add(new BattlefieldUiLayoutRow
            {
                Path = Get("Path"), Name = Get("Name"), NodeType = Get("NodeType"), Anchor = Get("Anchor"),
                OffsetX = Number("OffsetX"), OffsetY = Number("OffsetY"), Width = Number("Width"), Height = Number("Height"),
                SizeMode = Get("SizeMode"), AssetMode = Get("AssetMode"),
                StreamingAssetPath = Get("StreamingAssetPath"), TemplateID = Get("TemplateID"),
                TemplateRole = Get("TemplateRole"), LayoutVariant = Get("LayoutVariant"),
                BindingKey = Get("BindingKey"), AllowCrop = bool.TryParse(Get("AllowCrop"), out bool crop) && crop,
                Text = Get("Text"),
            });
        }
        return result;
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
                else quoted = !quoted;
            }
            else if (current == ',' && !quoted)
            {
                values.Add(cell.ToString());
                cell.Length = 0;
            }
            else cell.Append(current);
        }
        values.Add(cell.ToString());
        return values;
    }
}
