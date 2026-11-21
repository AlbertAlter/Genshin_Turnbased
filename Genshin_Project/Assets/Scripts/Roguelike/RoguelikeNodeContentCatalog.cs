using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

/// <summary>节点内容配置和抽取。只读配置，已抽记录由本章存档持有。</summary>
public sealed class RoguelikeNodeContentCatalog : IRoguelikeStageEncounterCatalog
{
    private sealed class Candidate
    {
        public string Id;
        public RoguelikeNodeKind Kind;
        public int Chapter;
        public int From;
        public int To;
        public int Weight = 1;
        public int Background = 1;
        public List<StageEnemySetup> Enemies;
        public bool HasAdditionalWaves;
    }

    private readonly Dictionary<string, Candidate> contents = new Dictionary<string, Candidate>(StringComparer.Ordinal);
    private readonly Dictionary<int, (int Level, float HP, float ATK, int[] Weights)> stages =
        new Dictionary<int, (int, float, float, int[])>();
    private readonly Dictionary<int, int> chapterEnds = new Dictionary<int, int>();
    private readonly SortedDictionary<int, Roguelike.Tests.RouteGeneration.RouteStageConfig> routeStages = new();

    internal IEnumerable<int> StageIds => stages.Keys;

    internal List<Roguelike.Tests.RouteGeneration.RouteStageConfig> RouteStages(int chapter)
        => routeStages.Where(pair => pair.Key / 100 == chapter).Select(pair => pair.Value).ToList();

    private RoguelikeNodeContentCatalog() { }

    public static RoguelikeNodeContentCatalog LoadFromStreamingAssets()
        => Load(Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike"));

    public static RoguelikeNodeContentCatalog Load(string directory)
    {
        var catalog = new RoguelikeNodeContentCatalog();
        catalog.ReadStages(Path.Combine(directory, "StageConfig.xlsx"));
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "Battle"), "BattleConfig*.xlsx")
            .OrderBy(path => path, StringComparer.Ordinal))
            catalog.ReadBattles(path);
        string events = Path.Combine(directory, "EventConfig.xlsx");
        if (File.Exists(events)) catalog.ReadEvents(events);
        foreach (var stage in catalog.stages)
        {
            foreach (RoguelikeNodeKind kind in Enum.GetValues(typeof(RoguelikeNodeKind)))
                if (stage.Value.Weights[(int)kind] > 0 &&
                    !catalog.Candidates(stage.Key, kind).Any())
                    throw new InvalidDataException($"R{stage.Key} 的 {kind} 权重为正，但没有可分配的内容。");
        }
        return catalog;
    }

    internal RoguelikeSaveResult Select(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node,
        IBattleRandomSource random = null)
    {
        if (!TryGetStageId(snapshot.CurrentChapterId, node.StageIndex, out int stageId) ||
            !stages.TryGetValue(stageId, out var stage) || stage.Weights[(int)node.Kind] <= 0)
            return Failure($"该章节层未配置 {node.Kind} 节点。");

        var allowed = Candidates(stageId, node.Kind).ToArray();
        if (allowed.Length == 0)
            return Failure($"R{stageId} 没有可用的 {node.Kind} 内容。");

        var used = snapshot.UsedNodeContentIds ??= new List<string>();
        var available = allowed.Where(candidate => !used.Contains(candidate.Id)).ToArray();
        if (available.Length == 0) available = allowed;

        Candidate selected = available[0];
        if (available.Length > 1)
        {
            // 同一个已保存的随机位置总是得到同一结果，写档失败重试也可复现。
            int step = snapshot.RandomStep;
            if (step == int.MaxValue) return Failure("本局随机步数已到上限。");
            long totalWeight = available.Sum(entry => (long)entry.Weight);
            if (totalWeight > int.MaxValue) return Failure($"R{stageId} 的 {node.Kind} 候选总权重超出范围。");
            int seed = unchecked(snapshot.RunSeed * 397 ^ step);
            int roll = (random ?? new SystemBattleRandomSource(seed)).NextInt(0, (int)totalWeight);
            foreach (var candidate in available)
            {
                roll -= candidate.Weight;
                if (roll >= 0) continue;
                selected = candidate;
                break;
            }
            snapshot.RandomStep = step + 1;
        }

        node.ContentId = selected.Id;
        node.IsChapterFinalBoss = node.Kind == RoguelikeNodeKind.Boss && stageId == chapterEnds[snapshot.CurrentChapterId];
        if (!used.Contains(selected.Id)) used.Add(selected.Id);
        return RoguelikeSaveResult.Success();
    }

    public RoguelikeSaveResult<StageSetupData> Resolve(string contentId)
    {
        if (!contents.TryGetValue(contentId ?? string.Empty, out var candidate) || candidate.Enemies == null)
            return InvalidBattle($"战斗配置不存在：{contentId}。");
        var stage = stages[candidate.From];
        return BuildBattle(candidate, stage.Level, stage.HP, stage.ATK);
    }

    public RoguelikeSaveResult<StageSetupData> Resolve(int chapter, RoguelikeNodeSelection node)
    {
        if (node == null || !TryGetStageId(chapter, node.StageIndex, out int stageId) ||
            !stages.TryGetValue(stageId, out var stage) ||
            !contents.TryGetValue(node.ContentId ?? string.Empty, out var candidate) || candidate.Enemies == null ||
            candidate.Kind != node.Kind || stageId < candidate.From || stageId > candidate.To)
            return InvalidBattle("已选战斗与当前章节层或节点类型不匹配。");
        return BuildBattle(candidate, stage.Level, stage.HP, stage.ATK);
    }

    private static RoguelikeSaveResult<StageSetupData> BuildBattle(Candidate candidate, int level, float hp, float atk)
    {
        if (candidate.HasAdditionalWaves)
            return InvalidBattle($"{candidate.Id} 包含后续波次，当前战斗入口仅支持单波。");
        return RoguelikeSaveResult<StageSetupData>.Success(new StageSetupData
        {
            StageName = candidate.Id,
            BattleID = candidate.Background,
            EnemyHPMultiplier = hp,
            EnemyATKMultiplier = atk,
            Enemies = candidate.Enemies.Select(enemy => new StageEnemySetup
            {
                EnemyID = enemy.EnemyID, Slot = enemy.Slot, Level = level,
            }).ToList(),
        });
    }

    private IEnumerable<Candidate> Candidates(int stage, RoguelikeNodeKind kind)
        => contents.Values.Where(entry => entry.Kind == kind &&
            (entry.Chapter == 0 || entry.Chapter == stage / 100) && stage >= entry.From && stage <= entry.To)
            .OrderBy(entry => entry.Id, StringComparer.Ordinal);

    private void ReadStages(string path)
    {
        using var book = new ExcelPackage(new FileInfo(path));
        foreach (var sheet in book.Workbook.Worksheets)
        {
            var headers = ExcelTableHeaders.For(sheet);
            if (headers.Optional("StageID") < 1) continue;
            string[] kinds = { "Battle", "Event", "Elite", "Boss" };
            headers.Require(kinds);
            headers.Require("EnemyLevel", "HPMultiplier", "ATKMultiplier");
            for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
            {
                string id = headers.Cell(row, "StageID").Text.Trim();
                if (id.Length == 0) continue;
                string where = $"StageConfig/{sheet.Name} 第 {row} 行";
                int stage = RoguelikeStageId.Parse(id, where);
                int level = ReadInt(headers.Cell(row, "EnemyLevel").Text, 1, 100, where + "/EnemyLevel");
                float hp = ReadMultiplier(headers.Cell(row, "HPMultiplier").Text, where + "/HPMultiplier");
                float atk = ReadMultiplier(headers.Cell(row, "ATKMultiplier").Text, where + "/ATKMultiplier");
                int[] weights = kinds.Select(kind => ReadInt(headers.Cell(row, kind).Text, 0, int.MaxValue, where + "/" + kind)).ToArray();
                long total = weights.Sum(weight => (long)weight);
                if (total <= 0 || total > int.MaxValue) throw new InvalidDataException($"{where}：节点总权重无效。");
                if (stages.ContainsKey(stage)) throw new InvalidDataException($"{where}：重复节点 {id}。");
                stages.Add(stage, (level, hp, atk, weights));
                if (headers.Optional("MinEncounter") > 0 && headers.Optional("MaxEncounter") > 0)
                {
                    int min = ReadInt(headers.Cell(row, "MinEncounter").Text, 1, 6, where);
                    routeStages.Add(stage, new Roguelike.Tests.RouteGeneration.RouteStageConfig {
                        StageId = id, MinEncounter = min, MaxEncounter = ReadInt(headers.Cell(row, "MaxEncounter").Text, min, 6, where),
                        BattleWeight = weights[0], EventWeight = weights[1], EliteWeight = weights[2], BossWeight = weights[3] });
                }
                chapterEnds.TryGetValue(stage / 100, out int end);
                chapterEnds[stage / 100] = Math.Max(stage, end);
            }
        }
        if (stages.Count == 0) throw new InvalidDataException("StageConfig 没有节点配置。");
        foreach (var chapter in chapterEnds)
            for (int stage = chapter.Key * 100 + 1; stage <= chapter.Value; stage++)
                if (!stages.ContainsKey(stage)) throw new InvalidDataException($"StageConfig 缺少 R{stage} 节点配置。");
    }

    private void ReadBattles(string path)
    {
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = RequireSheet(book, "Main", path);
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("BattleID", "BattleType", "AvailableStageFrom", "AvailableStageTo", "BattleBG");
        var battles = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string id = Field("BattleID");
            if (id.Length == 0) continue;
            string where = $"{Path.GetFileName(path)}/Main 第 {row} 行";
            string type = Field("BattleType");
            if (!Enum.TryParse(type, true, out RoguelikeNodeKind kind) || kind == RoguelikeNodeKind.Event ||
                !string.Equals(kind.ToString(), type, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{where}：无效的 BattleType {type}。");
            int from = RoguelikeStageId.Parse(Field("AvailableStageFrom"), where);
            int to = RoguelikeStageId.Parse(Field("AvailableStageTo"), where);
            if (from > to || from / 100 != to / 100 || !stages.ContainsKey(from) || !stages.ContainsKey(to))
                throw new InvalidDataException($"{where}：开放区间须是同一章内存在的起止节点。");
            var battle = new Candidate { Id = id, Kind = kind, Chapter = from / 100, From = from, To = to };
            string bg = Field("BattleBG");
            if (bg.Length != 0) battle.Background = ReadInt(bg, 1, int.MaxValue, where + "/BattleBG");
            Add(battle, where);
            battles.Add(id, battle);
        }
        ReadFormations(RequireSheet(book, "Battle_Enemy", path), battles);
        foreach (var battle in battles.Values)
            if (battle.Enemies == null)
                throw new InvalidDataException($"{battle.Id} 缺少第一波敌人。");
    }

    private static void ReadFormations(ExcelWorksheet sheet, Dictionary<string, Candidate> battles)
    {
        int Column(string name)
        {
            // Slot1～5 后面的同名列只供 Excel 显示敌人名称。
            for (int col = 1; col <= (sheet.Dimension?.End.Column ?? 0); col++)
                if (string.Equals(ExcelTableHeaders.Normalize(sheet.Cells[1, col].Text), name, StringComparison.OrdinalIgnoreCase))
                    return col;
            throw new InvalidDataException($"Battle_Enemy 缺少 {name}。");
        }

        int idColumn = Column("BattleID"), waveColumn = Column("Wave");
        int[] slots = Enumerable.Range(1, 5).Select(slot => Column("Slot" + slot)).ToArray();
        for (int row = 2; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string id = sheet.Cells[row, idColumn].Text.Trim();
            if (id.Length == 0 || row == 2 && id == "string") continue;
            string where = $"Battle_Enemy 第 {row} 行（{id}）";
            if (!battles.TryGetValue(id, out var battle)) throw new InvalidDataException(where + "：未知 BattleID。");
            int wave = ReadInt(sheet.Cells[row, waveColumn].Text, 1, int.MaxValue, where + "/Wave");
            if (wave != 1) { battle.HasAdditionalWaves = true; continue; }
            if (battle.Enemies != null) throw new InvalidDataException(where + "：第一波重复。");
            battle.Enemies = new List<StageEnemySetup>();
            for (int slot = 0; slot < slots.Length; slot++)
            {
                string value = sheet.Cells[row, slots[slot]].Text.Trim();
                if (value.Length == 0) continue;
                int enemy = ReadInt(value, 1, int.MaxValue, where + $"/Slot{slot + 1}");
                battle.Enemies.Add(new StageEnemySetup { EnemyID = enemy, Slot = slot + 1 });
            }
            if (battle.Enemies.Count == 0) throw new InvalidDataException(where + "：第一波为空。");
        }
    }

    private void ReadEvents(string path)
    {
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = RequireSheet(book, "Event_Main", path);
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("EventID", "Enabled");
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string id = headers.Cell(row, "EventID").Text.Trim();
            if (id.Length == 0 || headers.Cell(row, "Enabled").Text.Trim() != "1") continue;
            string where = $"EventConfig/Event_Main 第 {row} 行";
            int chapter = ReadInt(headers.Cell(row, "ChapterID").Text, 0, int.MaxValue, where + "/ChapterID");
            int weight = ReadInt(headers.Cell(row, "Weight").Text, 1, int.MaxValue, where + "/Weight");
            int condition = headers.Optional("EntryConditionGroupID");
            if (condition > 0 && !string.IsNullOrWhiteSpace(sheet.Cells[row, condition].Text))
                throw new InvalidDataException(where + "：事件出现条件需由事件模块接入后启用。");
            Add(new Candidate { Id = id, Kind = RoguelikeNodeKind.Event, Chapter = chapter, To = int.MaxValue, Weight = weight }, where);
        }
    }

    private void Add(Candidate candidate, string where)
    {
        if (contents.ContainsKey(candidate.Id)) throw new InvalidDataException($"{where}：内容编号重复 {candidate.Id}。");
        contents.Add(candidate.Id, candidate);
    }

    private static bool TryGetStageId(int chapter, int index, out int stageId)
    {
        stageId = 0;
        if (chapter <= 0 || chapter > (int.MaxValue - 99) / 100 || index < 0 || index >= 99) return false;
        stageId = chapter * 100 + index + 1;
        return true;
    }

    private static ExcelWorksheet RequireSheet(ExcelPackage book, string name, string path)
        => book.Workbook.Worksheets[name] ?? throw new InvalidDataException($"{Path.GetFileName(path)} 缺少 {name}。");

    private static int ReadInt(string value, int min, int max, string where)
        => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result >= min && result <= max
            ? result : throw new InvalidDataException($"{where}：无效整数 {value}。");

    private static float ReadMultiplier(string value, string where)
        => float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) &&
            result > 0f && !float.IsInfinity(result)
            ? result : throw new InvalidDataException($"{where}：须填写有限正数倍率。");

    private static RoguelikeSaveResult Failure(string message)
        => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.NotFound, message);

    private static RoguelikeSaveResult<StageSetupData> InvalidBattle(string message)
        => RoguelikeSaveResult<StageSetupData>.Failure(RoguelikeSaveErrorCode.ValidationFailed, message);
}
