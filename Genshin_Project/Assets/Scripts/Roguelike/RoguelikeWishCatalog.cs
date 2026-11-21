using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

public enum RoguelikeWishKind { Character = 0, Weapon = 1 }

public sealed class RoguelikeWishPrize
{
    public RoguelikeWishKind Kind { get; }
    public int Id { get; }
    public int Stars { get; }
    public string Name { get; }

    public RoguelikeWishPrize(RoguelikeWishKind kind, int id, int stars, string name)
    {
        Kind = kind;
        Id = id;
        Stars = stars;
        Name = name;
    }
}

public sealed class RoguelikeWishPool
{
    public string Id { get; }
    public Version Phase { get; }
    public string Title { get; }
    public RoguelikeWishPrize Featured { get; }
    public IReadOnlyList<RoguelikeWishPrize> FourStars { get; }

    internal RoguelikeWishPool(string id, Version phase, string title, RoguelikeWishPrize featured,
        RoguelikeWishPrize[] fourStars)
    {
        Id = id;
        Phase = phase;
        Title = title;
        Featured = featured;
        FourStars = Array.AsReadOnly(fourStars);
    }
}

public sealed class RoguelikeWishCatalog
{
    private static readonly string[] ConstantColumns = { "3StarConstant", "4StarConstant", "5StarConstant" };
    private readonly IReadOnlyList<RoguelikeWishPool> pools;
    private readonly IReadOnlyDictionary<Version, int> openingStages;
    private readonly IReadOnlyDictionary<int, int> chapterEndNodes;
    private readonly Dictionary<Version, RoguelikeWishPrize[]> constantsByPhase = new Dictionary<Version, RoguelikeWishPrize[]>();
    public const string WorkbookName = "Wishlist.xlsx";
    public IReadOnlyList<RoguelikeWishPool> Pools => pools;
    internal bool HasConstantPools { get; }

    private RoguelikeWishCatalog(IEnumerable<RoguelikeWishPool> source,
        ILookup<Version, RoguelikeWishPrize> additions, bool hasConstantPools, Dictionary<Version, int> openingStages,
        IReadOnlyDictionary<int, int> chapterEndNodes)
    {
        pools = Array.AsReadOnly(source.OrderBy(pool => pool.Featured.Kind).ToArray());
        HasConstantPools = hasConstantPools;
        this.openingStages = openingStages;
        this.chapterEndNodes = chapterEndNodes;
        var cumulative = new Dictionary<(RoguelikeWishKind, int), RoguelikeWishPrize>();
        foreach (var phase in pools.GroupBy(pool => pool.Phase).OrderBy(group => group.Key))
        {
            foreach (var prize in additions[phase.Key]) cumulative[(prize.Kind, prize.Id)] = prize;
            var up = new HashSet<(RoguelikeWishKind, int)>(phase
                .SelectMany(pool => pool.FourStars.Concat(new[] { pool.Featured }))
                .Select(prize => (prize.Kind, prize.Id)));
            constantsByPhase.Add(phase.Key, cumulative.Values
                .Where(prize => !up.Contains((prize.Kind, prize.Id)))
                .OrderBy(prize => prize.Kind).ThenBy(prize => prize.Id).ToArray());
        }
    }

    internal IEnumerable<RoguelikeWishPrize> ConstantPrizes(RoguelikeWishPool pool)
        => constantsByPhase[pool.Phase].Where(prize => prize.Stars != 5 || prize.Kind == pool.Featured.Kind);

    public IReadOnlyList<RoguelikeWishPool> ForPhase(Version phase)
        => pools.Where(pool => pool.Phase.Equals(phase)).ToArray();

    public Version FirstPhase(int chapterId)
        => pools.Select(pool => pool.Phase).Where(phase => phase.Major == chapterId).OrderBy(phase => phase).FirstOrDefault();

    internal int? OpeningStageForVersion(Version version)
    {
        var first = pools.Select(pool => pool.Phase)
            .Where(phase => phase.Major == version.Major && phase.Minor == version.Minor)
            .OrderBy(phase => phase).FirstOrDefault();
        return first != null && openingStages.TryGetValue(first, out int stage) ? stage : (int?)null;
    }

    public Version PhaseAt(int chapterId, int stageIndex)
    {
        bool hasSchedule = false;
        int latestNode = 0;
        Version selected = null;
        foreach (var opening in openingStages)
        {
            if (opening.Value / 100 != chapterId) continue;
            hasSchedule = true;
            int node = opening.Value % 100;
            if (node - 1 > stageIndex || node < latestNode) continue;
            if (node == latestNode && opening.Key.CompareTo(selected) <= 0) continue;
            latestNode = node;
            selected = opening.Key;
        }
        if (hasSchedule) return selected;

        // 旧表及尚未配置开放节点的章节沿用首期。显式配置在别章的期数只在其开放章节使用。
        var fallback = FirstPhase(chapterId);
        return fallback != null && !openingStages.ContainsKey(fallback) ? fallback : null;
    }

    internal int? NodesUntilNextOpening(int chapterId, int stageIndex)
    {
        long currentNode = (long)stageIndex + 1;
        long currentStageId = (long)chapterId * 100 + currentNode;
        int next = openingStages.Values.Where(stageId => stageId > currentStageId).DefaultIfEmpty().Min();
        if (next == 0) return null;
        if (next / 100 == chapterId) return next % 100 - (int)currentNode;
        return chapterEndNodes.TryGetValue(chapterId, out int end) ? (int)Math.Max(0, end - currentNode) : (int?)null;
    }

    public static RoguelikeWishCatalog LoadFromStreamingAssets()
    {
        string root = Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike");
        return Load(Path.Combine(root, WorkbookName), LoadChapterEndNodes(Path.Combine(root, "StageConfig.xlsx")));
    }

    internal static RoguelikeWishCatalog LoadFromStreamingAssets(IEnumerable<int> stageIds) =>
        Load(Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike", WorkbookName),
            stageIds.GroupBy(id => id / 100).ToDictionary(group => group.Key, group => group.Max() % 100));

    public static RoguelikeWishCatalog Load(string path) => Load(path, new Dictionary<int, int>());

    private static RoguelikeWishCatalog Load(string path, IReadOnlyDictionary<int, int> chapterEndNodes)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到祈愿表。", path);
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = book.Workbook.Worksheets["Sheet1"] ?? throw new InvalidDataException(WorkbookName + " 缺少 Sheet1。");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("WishID", "WishType", "UPVersion", "5starUP", "4starUP", "5starUPName", "4starUPName", "WishTitle");
        int stageColumn = headers.Optional("StageID");
        bool hasConstantPools = ConstantColumns.Any(column => headers.Optional(column) > 0);
        if (hasConstantPools) headers.Require(ConstantColumns);
        var rows = new List<RoguelikeWishPool>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var additions = new List<(Version Phase, RoguelikeWishPrize Prize)>();
        var constantStars = new Dictionary<(RoguelikeWishKind, int), int>();
        var openingStages = new Dictionary<Version, int>();
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string id = Field("WishID");
            if (id.Length == 0 && Enumerable.Range(1, sheet.Dimension.End.Column)
                .All(col => string.IsNullOrWhiteSpace(sheet.Cells[row, col].Text))) continue;
            string error = $"{WorkbookName}/Sheet1 第 {row} 行（{id}）：";
            if (id.Length == 0 || !ids.Add(id)) throw new InvalidDataException(error + "WishID 为空或重复。");
            if (!int.TryParse(Field("WishType"), NumberStyles.None, CultureInfo.InvariantCulture, out int type) ||
                !Enum.IsDefined(typeof(RoguelikeWishKind), type))
                throw new InvalidDataException(error + "WishType 必须为 0（角色）或 1（武器）。");
            if (!Version.TryParse(Field("UPVersion"), out var phase) || phase.Major < 1 ||
                phase.Build < 1 || phase.Revision >= 0)
                throw new InvalidDataException(error + "UPVersion 必须为 大版本.小版本.期数。");
            string stage = stageColumn > 0 ? sheet.Cells[row, stageColumn].Text.Trim() : string.Empty;
            if (stage.Length != 0)
            {
                int stageId = RoguelikeStageId.Parse(stage, error + "StageID ");
                if (openingStages.TryGetValue(phase, out int existing) && existing != stageId)
                    throw new InvalidDataException(error + $"UPVersion {phase} 对应多个 StageID（R{existing}、R{stageId}）。");
                openingStages[phase] = stageId;
            }
            var kind = (RoguelikeWishKind)type;
            var featured = Prize(kind, Field("5starUP"), 5, Field("5starUPName"), error);
            string title = Field("WishTitle");
            if (title.Length == 0 && kind == RoguelikeWishKind.Character)
                throw new InvalidDataException(error + "角色卡池缺少 WishTitle。");
            string[] fourIds = Field("4starUP").Split(';');
            string[] fourNames = Field("4starUPName").Split(';');
            if (fourIds.Length != fourNames.Length)
                throw new InvalidDataException(error + "4starUP 与 4starUPName 的条目数不同。");
            var fourStars = fourIds.Select((value, index) => Prize(kind, value.Trim(), 4, fourNames[index].Trim(), error)).ToArray();
            if (fourStars.Select(prize => prize.Id).Distinct().Count() != fourStars.Length ||
                fourStars.Any(prize => prize.Id == featured.Id))
                throw new InvalidDataException(error + "UP 名单包含重复模板。");
            rows.Add(new RoguelikeWishPool(id, phase, title.Length == 0 ? "神铸赋形" : title, featured, fourStars));
            if (!hasConstantPools) continue;
            for (int i = 0; i < ConstantColumns.Length; i++)
            {
                string column = ConstantColumns[i];
                string field = Field(column);
                if (field.Length == 0) continue;
                int stars = i + 3;
                foreach (string entry in field.Split(';'))
                {
                    if (!int.TryParse(entry.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int templateId) || templateId <= 0)
                        throw new InvalidDataException(error + $"{column} 含无效编号：[{entry}]。");
                    var key = (kind, templateId);
                    if (constantStars.TryGetValue(key, out int previous) && previous != stars)
                        throw new InvalidDataException(error + $"常驻编号 {templateId} 出现在不同稀有度列。");
                    constantStars[key] = stars;
                    additions.Add((phase, new RoguelikeWishPrize(kind, templateId, stars, null)));
                }
            }
        }
        if (rows.Count == 0) throw new InvalidDataException(WorkbookName + " 没有可读取的卡池。");
        return new RoguelikeWishCatalog(rows, additions.ToLookup(entry => entry.Phase, entry => entry.Prize),
            hasConstantPools, openingStages, chapterEndNodes);
    }

    private static IReadOnlyDictionary<int, int> LoadChapterEndNodes(string path)
    {
        var ends = new Dictionary<int, int>();
        if (!File.Exists(path)) return ends;
        using var book = new ExcelPackage(new FileInfo(path));
        foreach (var sheet in book.Workbook.Worksheets)
        {
            var headers = ExcelTableHeaders.For(sheet);
            if (headers.Optional("StageID") < 1) continue;
            for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
            {
                string value = headers.Cell(row, "StageID").Text.Trim();
                if (value.Length == 0) continue;
                int stageId = RoguelikeStageId.Parse(value, $"StageConfig.xlsx/{sheet.Name} 第 {row} 行：");
                int chapterId = stageId / 100;
                ends.TryGetValue(chapterId, out int end);
                ends[chapterId] = Math.Max(end, stageId % 100);
            }
        }
        return ends;
    }

    private static RoguelikeWishPrize Prize(RoguelikeWishKind kind, string id, int stars, string name, string error)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0 || name.Length == 0)
            throw new InvalidDataException(error + $"{stars}★ 名单含无效编号或空名称。");
        return new RoguelikeWishPrize(kind, value, stars, name);
    }
}
