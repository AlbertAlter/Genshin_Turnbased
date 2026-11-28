using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public List<StageWaveSetup> Waves;
        public bool EventBattleOnly;
        public int MaxRound;
        public string WinCondition, LoseCondition;
        public string ConditionKey;
        public List<StageTrialSetup> Trials = new List<StageTrialSetup>();
        public RoguelikeEventEncounter Event;
    }

    private readonly Dictionary<string, Candidate> contents = new Dictionary<string, Candidate>(StringComparer.Ordinal);
    private readonly Dictionary<int, (int Level, float HP, float ATK, int[] Weights)> stages =
        new Dictionary<int, (int, float, float, int[])>();
    private readonly Dictionary<int, int> chapterEnds = new Dictionary<int, int>();
    private readonly Dictionary<string, List<(string Item, int Quantity, string Hook)>> eventRewards = new();
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
        // 每章一张小表：EventConfig1.xlsx对应第一章。旧分支树不参与。
        string eventDirectory = Path.Combine(directory, "Event");
        if (Directory.Exists(eventDirectory))
            foreach (string path in Directory.GetFiles(eventDirectory, "EventBattleConfig*.xlsx").Where(path =>
                Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"^EventBattleConfig[1-9]\d*$")).OrderBy(path => path))
                catalog.ReadEventBattles(path);
        string[] eventBooks = Directory.Exists(eventDirectory)
            ? Directory.GetFiles(eventDirectory, "EventConfig*.xlsx").Where(path =>
                Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"^EventConfig[1-9]\d*$")).OrderBy(path => path).ToArray()
            : Array.Empty<string>();
        foreach (string path in eventBooks) catalog.ReadChapterEvents(path);
        // 兼容尚未发布章节小表的旧包；存在新表时不再混入早期示例遭遇。
        string events = Path.Combine(eventDirectory, "EventEncounters.xlsx");
        if (eventBooks.Length == 0 && File.Exists(events)) catalog.ReadEvents(events);
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
            candidate.Kind != node.Kind || stageId < candidate.From || stageId > candidate.To ||
            !string.IsNullOrEmpty(node.EventBattleContentId) && candidate.Event?.BattleContentId != node.EventBattleContentId)
            return InvalidBattle("已选战斗与当前章节层或节点类型不匹配。");
        return BuildBattle(candidate, stage.Level, stage.HP, stage.ATK);
    }

    private static RoguelikeSaveResult<StageSetupData> BuildBattle(Candidate candidate, int level, float hp, float atk)
    {
        var waves = RoguelikeSaveJson.DeepClone(candidate.Waves ?? new List<StageWaveSetup> {
            new StageWaveSetup { Enemies = candidate.Enemies } });
        foreach (var enemy in waves.SelectMany(w => w.Enemies))
            if (enemy.Level <= 0) enemy.Level = level;
        return RoguelikeSaveResult<StageSetupData>.Success(new StageSetupData
        {
            StageName = candidate.Id,
            BattleID = candidate.Background,
            EnemyHPMultiplier = hp,
            EnemyATKMultiplier = atk,
            Enemies = waves[0].Enemies, Waves = waves, MaxRound = candidate.MaxRound,
            WinCondition = candidate.WinCondition, LoseCondition = candidate.LoseCondition,
            ConditionKey = candidate.ConditionKey,
            Trials = RoguelikeSaveJson.DeepClone(candidate.Trials),
        });
    }

    private IEnumerable<Candidate> Candidates(int stage, RoguelikeNodeKind kind)
        => contents.Values.Where(entry => !entry.EventBattleOnly && entry.Kind == kind &&
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
            battle.ConditionKey = headers.Optional("ConditionKey") > 0 ? Field("ConditionKey") : null;
            string bg = Field("BattleBG");
            if (bg.Length != 0) battle.Background = ReadInt(bg, 1, int.MaxValue, where + "/BattleBG");
            Add(battle, where);
            battles.Add(id, battle);
        }
        var formations = RoguelikeBattleWaves.Read(book.Workbook, battles.Keys);
        foreach (var entry in battles)
        {
            entry.Value.Waves = formations[entry.Key];
            entry.Value.Enemies = entry.Value.Waves[0].Enemies;
        }
        foreach (var battle in battles.Values)
            if (battle.Enemies == null)
                throw new InvalidDataException($"{battle.Id} 缺少第一波敌人。");
    }

    private void ReadEventBattles(string path)
    {
        int chapter = int.Parse(Path.GetFileNameWithoutExtension(path).Substring("EventBattleConfig".Length), CultureInfo.InvariantCulture);
        if (!chapterEnds.TryGetValue(chapter, out int end)) throw new InvalidDataException("事件战斗表对应章节不存在：" + path);
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = RequireSheet(book, "Main", path); var headers = ExcelTableHeaders.For(sheet);
        headers.Require("BattleID", "BattleBG", "WinCondition", "LoseCondition", "MaxRound");
        var battles = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string id = Field("BattleID"); if (id.Length == 0) continue;
            var candidate = new Candidate { Id = id, Kind = RoguelikeNodeKind.Battle, EventBattleOnly = true,
                Chapter = chapter, From = chapter * 100 + 1, To = end,
                MaxRound = Field("MaxRound").Length == 0 ? 0 : ReadInt(Field("MaxRound"), 1, int.MaxValue, id + "/MaxRound"),
                WinCondition = Field("WinCondition"), LoseCondition = Field("LoseCondition") };
            candidate.ConditionKey = headers.Optional("ConditionKey") > 0 ? Field("ConditionKey") : null;
            if (Field("BattleBG").Length > 0) candidate.Background = ReadInt(Field("BattleBG"), 1, int.MaxValue, id + "/BattleBG");
            _ = new BattleEventObjective(candidate.WinCondition, candidate.LoseCondition, candidate.MaxRound);
            candidate.Trials = RoguelikeTrialParty.Read(book.Workbook,
                headers.Optional("Tryout") > 0 ? Field("Tryout") : "",
                headers.Optional("TryoutConfig") > 0 ? Field("TryoutConfig") : "");
            Add(candidate, path + "/Main第" + row + "行"); battles.Add(id, candidate);
        }
        var formations = RoguelikeBattleWaves.Read(book.Workbook, battles.Keys, allowUnfilledBattles: true);
        foreach (var entry in battles)
            if (formations[entry.Key].Count > 0)
            { entry.Value.Waves = formations[entry.Key]; entry.Value.Enemies = entry.Value.Waves[0].Enemies; }
    }

    private void ReadEvents(string path)
    {
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = RequireSheet(book, "Encounters", path);
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("EventID", "Enabled", "AvailableStageFrom", "AvailableStageTo", "Weight",
            "TitleKey", "TextKey", "PlaceKey", "ActionKey", "BattleID", "PreviewEnemyID");
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string id = Field("EventID");
            if (id.Length == 0 || headers.Cell(row, "Enabled").Text.Trim() != "1") continue;
            string where = $"EventEncounters/Encounters 第 {row} 行";
            int from = RoguelikeStageId.Parse(Field("AvailableStageFrom"), where);
            int to = RoguelikeStageId.Parse(Field("AvailableStageTo"), where);
            if (from > to || from / 100 != to / 100 || !stages.ContainsKey(from) || !stages.ContainsKey(to))
                throw new InvalidDataException(where + "：事件区间须是本章存在的节点。");
            if (!contents.TryGetValue(Field("BattleID"), out var battle) || battle.Kind == RoguelikeNodeKind.Event ||
                battle.Enemies == null)
                throw new InvalidDataException(where + "：BattleID须引用已有的战斗。");
            int preview = ReadInt(Field("PreviewEnemyID"), 1, int.MaxValue, where);
            if (!battle.Enemies.Any(enemy => enemy.EnemyID == preview))
                throw new InvalidDataException(where + "：预览敌人须属于该战斗。");
            foreach (string key in new[] { "TitleKey", "TextKey", "PlaceKey", "ActionKey" })
                if (Field(key).Length == 0) throw new InvalidDataException(where + "：缺少" + key);
            var detail = new RoguelikeEventEncounter { EventId = id, TitleKey = Field("TitleKey"),
                TextKey = Field("TextKey"), PlaceKey = Field("PlaceKey"), ActionKey = Field("ActionKey"),
                BattleContentId = battle.Id, PreviewEnemyId = preview,
                EnemyIds = battle.Enemies.Select(enemy => enemy.EnemyID).ToArray() };
            Add(new Candidate { Id = id, Kind = RoguelikeNodeKind.Event, Chapter = from / 100, From = from, To = to,
                Weight = ReadInt(Field("Weight"), 1, int.MaxValue, where), Event = detail,
                Background = battle.Background, Enemies = battle.Enemies, Waves = battle.Waves,
                MaxRound = battle.MaxRound, WinCondition = battle.WinCondition, LoseCondition = battle.LoseCondition,
                ConditionKey = headers.Optional("ConditionKey") > 0 && Field("ConditionKey").Length > 0 ? Field("ConditionKey") : battle.ConditionKey,
                Trials = battle.Trials }, where);
        }
    }

    private void ReadChapterEvents(string path)
    {
        string number = Path.GetFileNameWithoutExtension(path).Substring("EventConfig".Length);
        if (!int.TryParse(number, out int chapter) || !chapterEnds.TryGetValue(chapter, out int end))
            throw new InvalidDataException("事件表对应的章节尚未配置：" + path);
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = RequireSheet(book, "Encounters", path);
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("EventID", "Weight", "TitleKey", "DescriptionKey", "OptionKey", "ConditionKey", "BattleID");
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string id = Field("EventID"); if (id.Length == 0) continue;
            string where = Path.GetFileName(path) + "/Encounters 第" + row + "行";
            int weight = ReadInt(Field("Weight"), 0, int.MaxValue, where);
            if (weight == 0) continue;
            foreach (string key in new[] { "TitleKey", "DescriptionKey", "OptionKey" })
                if (Field(key).Length == 0) throw new InvalidDataException(where + "：缺少" + key);
            Candidate battle = null;
            string battleId = Field("BattleID");
            if (battleId.Length > 0 && (!contents.TryGetValue(battleId, out battle) ||
                battle.Kind == RoguelikeNodeKind.Event || battle.Enemies == null))
                throw new InvalidDataException(where + "：BattleID须引用已有的战斗。");
            // 战斗列允许暂空：可以查看事件或离开，但不能用其他敌阵代替进入战斗。
            var detail = new RoguelikeEventEncounter { EventId = id, TitleKey = Field("TitleKey"),
                TextKey = Field("DescriptionKey"), ActionKey = Field("OptionKey"), ConditionKey = Field("ConditionKey"),
                BattleContentId = battleId, PreviewEnemyId = battle?.Enemies.OrderBy(enemy => enemy.Slot).First().EnemyID ?? 0,
                EnemyIds = battle?.Enemies.Select(enemy => enemy.EnemyID).ToArray() ?? Array.Empty<int>() };
            Add(new Candidate { Id = id, Kind = RoguelikeNodeKind.Event, Chapter = chapter, From = chapter * 100 + 1,
                To = end, Weight = weight, Event = detail, Background = battle?.Background ?? 1, Enemies = battle?.Enemies, Waves = battle?.Waves,
                MaxRound = battle?.MaxRound ?? 0, WinCondition = battle?.WinCondition, LoseCondition = battle?.LoseCondition,
                ConditionKey = Field("ConditionKey").Length > 0 ? Field("ConditionKey") : battle?.ConditionKey,
                Trials = battle?.Trials ?? new List<StageTrialSetup>() }, where);
        }
        ReadEventRewards(book.Workbook);
    }

    public List<RoguelikeItemStackData> EventRewards(string eventId, int defeatedEnemies)
    {
        if (defeatedEnemies < 0) throw new ArgumentOutOfRangeException(nameof(defeatedEnemies));
        if (!eventRewards.TryGetValue(eventId ?? "", out var rows)) return new List<RoguelikeItemStackData>();
        return rows.Select(r => new RoguelikeItemStackData { ItemId = r.Item,
            Quantity = checked(r.Quantity + (r.Hook == "RE103()" ? defeatedEnemies / 10 : 0)) }).Where(r => r.Quantity > 0).ToList();
    }

    private void ReadEventRewards(ExcelWorkbook book)
    {
        var sheet = book.Worksheets["Rewards"]; if (sheet?.Dimension == null) return;
        var h = ExcelTableHeaders.For(sheet); h.Require("EventID", "ItemID", "Quantity", "RewardHook");
        string current = null;
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = h.Cell(row, "EventID").Text.Trim(), item = h.Cell(row, "ItemID").Text.Trim();
            if (id.Length > 0) current = id;
            if (item.Length == 0) continue;
            if (current == null || !contents.TryGetValue(current, out var entry) || entry.Kind != RoguelikeNodeKind.Event)
                throw new InvalidDataException("Rewards引用未知事件：" + current);
            string hook = h.Cell(row, "RewardHook").Text.Trim();
            if (hook.Length > 0 && (hook != "RE103()" || current != "RE103")) throw new InvalidDataException("未知事件奖励钩子：" + hook);
            if (!eventRewards.TryGetValue(current, out var rewards)) eventRewards.Add(current, rewards = new());
            if (rewards.Any(r => r.Item == item)) throw new InvalidDataException("事件奖励物品重复：" + current + "/" + item);
            rewards.Add((item, ReadInt(h.Cell(row, "Quantity").Text, 0, int.MaxValue, current + "/Quantity"), hook));
        }
    }

    public int EventEnemyLevel(int chapter, RoguelikeNodeSelection node)
        => TryGetStageId(chapter, node.StageIndex, out int stage) && stages.TryGetValue(stage, out var entry)
            ? entry.Level : throw new InvalidDataException("事件所在层不存在。");

    public RoguelikeSaveResult<RoguelikeEventEncounter> ResolveEvent(int chapter, RoguelikeNodeSelection node)
    {
        if (node == null || node.Kind != RoguelikeNodeKind.Event ||
            !TryGetStageId(chapter, node.StageIndex, out int stage) ||
            !contents.TryGetValue(node.ContentId ?? "", out var candidate) || candidate.Event == null ||
            stage < candidate.From || stage > candidate.To)
            return RoguelikeSaveResult<RoguelikeEventEncounter>.Failure(RoguelikeSaveErrorCode.ValidationFailed, "当前事件与节点配置不匹配。");
        return RoguelikeSaveResult<RoguelikeEventEncounter>.Success(RoguelikeSaveJson.DeepClone(candidate.Event));
    }

    /// <summary>独立场景只读预览；不生成测试存档，也不推进正式流程。</summary>
    public RoguelikeSaveResult<RoguelikeEventEncounter> PreviewEvent(string id)
        => contents.TryGetValue(id ?? "", out var candidate) && candidate.Event != null
            ? RoguelikeSaveResult<RoguelikeEventEncounter>.Success(RoguelikeSaveJson.DeepClone(candidate.Event))
            : RoguelikeSaveResult<RoguelikeEventEncounter>.Failure(RoguelikeSaveErrorCode.NotFound, "事件配置不存在：" + id);

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
