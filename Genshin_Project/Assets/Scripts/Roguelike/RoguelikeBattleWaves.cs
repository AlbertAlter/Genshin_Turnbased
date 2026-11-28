using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

/// <summary>Battle_Enemy是唯一名单来源；Wave_Config只修改已列敌人的配置。</summary>
public static class RoguelikeBattleWaves
{
    public static Dictionary<string, List<StageWaveSetup>> Read(ExcelWorkbook book, IEnumerable<string> battleIds, bool allowUnfilledBattles = false)
    {
        var result = battleIds.ToDictionary(id => id, _ => new List<StageWaveSetup>(), StringComparer.Ordinal);
        var sheet = book.Worksheets["Battle_Enemy"] ?? throw new InvalidDataException("缺少Battle_Enemy。");
        int Column(string name)
        {
            for (int c = 1; c <= sheet.Dimension.End.Column; c++)
                if (string.Equals(ExcelTableHeaders.Normalize(sheet.Cells[1, c].Text), name, StringComparison.OrdinalIgnoreCase)) return c;
            throw new InvalidDataException("Battle_Enemy缺少" + name);
        }
        int idColumn = Column("BattleID"), waveColumn = Column("Wave");
        int[] slots = Enumerable.Range(1, 5).Select(i => Column("Slot" + i)).ToArray();
        string currentBattle = null;
        int currentWave = 0;
        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            string id = sheet.Cells[row, idColumn].Text.Trim();
            if (row == 2 && id == "string") continue;
            string numberText = sheet.Cells[row, waveColumn].Text.Trim();
            string[] enemies = slots.Select(c => sheet.Cells[row, c].Text.Trim()).ToArray();
            // 只看名单列；右侧名称公式及格式预留行不构成敌人，也不重置分组。
            if (id.Length == 0 && numberText.Length == 0 && enemies.All(v => v.Length == 0)) continue;
            if (id.Length > 0 && id != currentBattle) { currentBattle = id; currentWave = 0; }
            id = currentBattle;
            if (string.IsNullOrEmpty(id)) throw new InvalidDataException("Battle_Enemy第" + row + "行缺少可继承的BattleID。");
            if (!result.TryGetValue(id, out var waves)) throw new InvalidDataException("Battle_Enemy未知BattleID：" + id);
            if (numberText.Length > 0) currentWave = Positive(numberText, id + "/Wave");
            // 未完成的事件战斗允许只有ID/波次的占位行，不生成空波次或假敌人。
            if (enemies.All(v => v.Length == 0)) continue;
            if (currentWave == 0) throw new InvalidDataException(id + "/Battle_Enemy第" + row + "行缺少Wave。");
            int number = currentWave;
            var wave = waves.FirstOrDefault(w => w.Wave == number);
            if (wave == null) { wave = new StageWaveSetup { Wave = number }; waves.Add(wave); }
            for (int slot = 0; slot < 5; slot++)
            {
                string value = enemies[slot];
                if (value.Length != 0) wave.Enemies.Add(new StageEnemySetup { EnemyID = Positive(value, id + "/EnemyID"), Slot = slot + 1 });
            }
        }
        foreach (var pair in result)
        {
            pair.Value.Sort((a, b) => a.Wave.CompareTo(b.Wave));
            if (pair.Value.Count == 0 && !allowUnfilledBattles || pair.Value.Where((w, i) => w.Wave != i + 1 || w.Enemies.Count == 0).Any())
                throw new InvalidDataException(pair.Key + "的波次须从1连续填写，且每波敌人非空。");
        }
        ApplyConfiguration(book, result, allowUnfilledBattles);
        return result;
    }

    private static void ApplyConfiguration(ExcelWorkbook book, Dictionary<string, List<StageWaveSetup>> battles, bool allowUnfilledBattles)
    {
        var sheet = book.Worksheets["Wave_Config"];
        if (sheet?.Dimension == null) return;
        var h = ExcelTableHeaders.For(sheet);
        h.Require("BattleID", "Wave");
        string Read(int r, string key)
        {
            for (int col = 1; col <= sheet.Dimension.End.Column; col++)
                if (string.Equals(ExcelTableHeaders.Normalize(sheet.Cells[1, col].Text), key, StringComparison.OrdinalIgnoreCase)) return sheet.Cells[r, col].Text.Trim();
            return "";
        }
        var groups = ReadStatuses(book.Worksheets["Initial_Status"], allowUnfilledBattles);
        var assigned = new HashSet<(StageEnemySetup, string)>();
        string currentBattle = null, currentWave = "";
        string[] fields = { "EnemyID", "Slot", "LevelOverride", "HPMultiplier", "ATKMultiplier", "InitialHPPercent",
            "InitialStatusGroupID", "AIProfileID", "CountsForVictory" };
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = Read(row, "BattleID"), n = Read(row, "Wave");
            bool hasConfiguration = fields.Any(k => Read(row, k).Length > 0);
            if (id.Length == 0 && n.Length == 0 && !hasConfiguration) continue;
            if (id.Length > 0 && id != currentBattle) { currentBattle = id; currentWave = ""; }
            id = currentBattle;
            if (string.IsNullOrEmpty(id)) throw new InvalidDataException("Wave_Config第" + row + "行缺少可继承的BattleID。");
            if (!battles.TryGetValue(id, out var waves)) throw new InvalidDataException("Wave_Config未知BattleID：" + id);
            if (n.Length > 0) currentWave = n;
            n = currentWave;
            if (!hasConfiguration) continue;
            // 敌阵尚未配置的事件模板不可入战，其预留覆盖字段不影响其他可用事件。
            if (allowUnfilledBattles && waves.Count == 0) continue;
            if (n.Length == 0)
            {
                if (new[] { "EnemyID", "Slot", "LevelOverride", "HPMultiplier", "ATKMultiplier", "InitialHPPercent",
                    "InitialStatusGroupID", "AIProfileID", "CountsForVictory" }.Any(k => Read(row, k).Length > 0))
                    throw new InvalidDataException("Wave_Config第" + row + "行缺少Wave。");
                continue; // Only BattleID filled is an unused configuration placeholder.
            }
            int wave = Positive(n, id + "/Wave"), enemy = OptionalInt(Read(row, "EnemyID")), slot = OptionalInt(Read(row, "Slot"));
            var targets = waves.FirstOrDefault(w => w.Wave == wave)?.Enemies
                .Where(e => (enemy == 0 || e.EnemyID == enemy) && (slot == 0 || e.Slot == slot)).ToArray();
            if (targets == null || targets.Length == 0)
                throw new InvalidDataException($"{id}/Wave_Config第{row}行未匹配Battle_Enemy中的既有敌人。");
            foreach (var target in targets)
            {
                void Set(string key, Action<string> write)
                {
                    string value = Read(row, key); if (value.Length == 0) return;
                    if (!assigned.Add((target, key))) throw new InvalidDataException(id + "/Wave_Config重复配置" + key);
                    write(value);
                }
                Set("LevelOverride", v => target.Level = Positive(v, "LevelOverride"));
                Set("HPMultiplier", v => target.HPMultiplier = PositiveFloat(v, "HPMultiplier"));
                Set("ATKMultiplier", v => target.ATKMultiplier = PositiveFloat(v, "ATKMultiplier"));
                Set("InitialHPPercent", v => { target.InitialHPPercent = PositiveFloat(v, "InitialHPPercent");
                    if (target.InitialHPPercent > 100) throw new InvalidDataException("InitialHPPercent须不大于100。"); });
                Set("AIProfileID", v => target.AIProfileID = Positive(v, "AIProfileID"));
                Set("CountsForVictory", v => { if (v != "0" && v != "1") throw new InvalidDataException("CountsForVictory只填0/1。"); target.IgnoreForVictory = v == "1"; });
                Set("InitialStatusGroupID", v => { if (!groups.TryGetValue(v, out var statuses)) throw new InvalidDataException("未知InitialStatusGroupID：" + v);
                    target.InitialStatuses = statuses.Select(s => new StageEnemyStatusSetup { StatusID = s.StatusID, Turns = s.Turns }).ToList(); });
            }
        }
        foreach (var wave in battles.Values.SelectMany(w => w))
            if (wave.Enemies.All(e => e.IgnoreForVictory)) throw new InvalidDataException("每波至少需要一名计入胜利条件的敌人。");
    }

    private static Dictionary<string, List<StageEnemyStatusSetup>> ReadStatuses(ExcelWorksheet sheet, bool allowUnfilledGroups)
    {
        var result = new Dictionary<string, List<StageEnemyStatusSetup>>(StringComparer.Ordinal);
        if (sheet?.Dimension == null) return result;
        var h = ExcelTableHeaders.For(sheet); h.Require("InitialStatusGroupID", "StatusID", "Turns");
        string currentGroup = null;
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = h.Cell(row, "InitialStatusGroupID").Text.Trim();
            string status = h.Cell(row, "StatusID").Text.Trim();
            string turns = h.Cell(row, "Turns").Text.Trim();
            if (id.Length == 0 && status.Length == 0 && turns.Length == 0) continue;
            if (id.Length > 0) currentGroup = id;
            id = currentGroup;
            if (string.IsNullOrEmpty(id)) throw new InvalidDataException("Initial_Status缺少可继承的InitialStatusGroupID。");
            if (status.Length == 0)
            {
                if (allowUnfilledGroups) continue; // 填入真实状态前不创建可被引用的空组。
                throw new InvalidDataException("Initial_Status缺少StatusID。");
            }
            if (!result.TryGetValue(id, out var list)) result.Add(id, list = new List<StageEnemyStatusSetup>());
            list.Add(new StageEnemyStatusSetup { StatusID = status, Turns = Positive(turns, "Turns") });
        }
        return result;
    }
    private static int OptionalInt(string v) => v.Length == 0 ? 0 : Positive(v, "Wave_Config筛选");
    private static int Positive(string v, string where)
        => int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n
            : throw new InvalidDataException(where + "须为正整数。");
    private static float PositiveFloat(string v, string where)
        => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && n > 0 && !float.IsInfinity(n) ? n
            : throw new InvalidDataException(where + "须为有限正数。");
}
