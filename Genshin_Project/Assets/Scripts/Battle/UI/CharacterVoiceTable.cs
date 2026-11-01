using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

public enum CharacterVoiceAction { Idle, SwitchIn, TeammateLowHP, LowHP, LightHit, HardHit, Win, Die }

public sealed class CharacterVoiceTable
{
    public const string FileName = "CharacterVoice.xlsx";
    public const string AudioFolder = "Audio/CharacterVoiceCN";
    private readonly Dictionary<(int, string), string[]> skills = new Dictionary<(int, string), string[]>();
    private readonly Dictionary<(int, CharacterVoiceAction), string[]> actions = new Dictionary<(int, CharacterVoiceAction), string[]>();
    private readonly HashSet<(int actor, string clip)> files = new HashSet<(int, string)>();
    public IEnumerable<(int actor, string clip)> Files => files;
    public float IdleWaitSeconds { get; private set; } = 5f;

    public IReadOnlyList<string> Skill(int actor, string key) =>
        skills.TryGetValue((actor, key), out var pool) ? pool : Array.Empty<string>();
    public IReadOnlyList<string> Action(int actor, CharacterVoiceAction key) =>
        actions.TryGetValue((actor, key), out var pool) ? pool : Array.Empty<string>();

    public static CharacterVoiceTable Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var result = new CharacterVoiceTable();
        foreach (string sheetName in new[] { "SkillVoice", "ActionVoice" })
        {
            var sheet = book.Workbook.Worksheets[sheetName];
            if (sheet?.Dimension == null) throw new InvalidDataException("语音表缺少 " + sheetName);
            bool skill = sheetName == "SkillVoice";
            string idColumn = skill ? "SkillID" : "CharacterID", keyColumn = skill ? "SkillID2" : "Action";
            var h = ExcelTableHeaders.For(sheet);
            h.Require(idColumn, keyColumn, "Soundtrack1");
            var columns = h.NumberedColumns("Soundtrack");
            for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                string idText = h.Cell(row, idColumn).Text.Trim();
                if (idText.Length == 0) continue;
                string key = h.Cell(row, keyColumn).Text.Trim();
                if (!int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || key.Length == 0)
                    throw new InvalidDataException($"{sheetName} 第 {row} 行身份或名称无效");
                int actor = skill ? id / 100 : id;
                if (actor <= 0) throw new InvalidDataException("无效角色编号 " + idText);
                var pool = new List<string>();
                foreach (int column in columns)
                {
                    string name = sheet.Cells[row, column].Text.Trim();
                    if (name.Length == 0) continue;
                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.Contains("\\") || name.Contains(".") || pool.Contains(name))
                        throw new InvalidDataException($"{sheetName} 第 {row} 行语音名无效或重复：{name}");
                    pool.Add(name); result.files.Add((actor, name));
                }
                if (skill) result.skills.Add((actor, key), pool.ToArray());
                else if (Enum.TryParse(key, false, out CharacterVoiceAction action) && action.ToString() == key && Enum.IsDefined(typeof(CharacterVoiceAction), action))
                    result.actions.Add((actor, action), pool.ToArray());
                else throw new InvalidDataException("未知角色语音动作 " + key);
            }
        }
        var parameters = book.Workbook.Worksheets["Parameters"];
        if (parameters != null)
        {
            var headers = ExcelTableHeaders.For(parameters);
            headers.Require("Key", "Value");
            bool found = false;
            for (int row = headers.DataStartRow; row <= parameters.Dimension.End.Row; row++)
            {
                if (headers.Cell(row, "Key").Text.Trim() != nameof(IdleWaitSeconds)) continue;
                if (found || !float.TryParse(headers.Cell(row, "Value").Text.Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float seconds) || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                    throw new InvalidDataException($"Parameters 第 {row} 行：IdleWaitSeconds 须为唯一的非负秒数。");
                found = true;
                result.IdleWaitSeconds = seconds;
            }
            if (!found) throw new InvalidDataException("语音 Parameters 缺少 IdleWaitSeconds。");
        }
        return result;
    }
}
