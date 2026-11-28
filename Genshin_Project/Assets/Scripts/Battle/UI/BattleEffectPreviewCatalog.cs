using System;
using System.Collections.Generic;
using System.Globalization;

public enum BattleEffectCategory { All, Character, Enemy, Common }
public enum BattleEffectLifetimeFilter { All, Sustained, OneShot }

/// <summary>Authoring/search metadata only; neither the owner name nor owner ID controls battle binding.</summary>
public sealed class BattleEffectPreviewEntry
{
    public readonly BattleEffectKind Kind;
    public readonly BattleEffectCategory Category;
    public readonly int OwnerID;
    public readonly string OwnerName, OwnerKey, Label, SearchText, Detail;
    public bool Sustained => BattleEffectParameters.IsSustained(Kind);
    public BattleEffectPreviewEntry(BattleEffectKind kind, BattleEffectCategory category, int ownerID, string ownerName, string title, string aliases = "")
    {
        if (!Enum.IsDefined(typeof(BattleEffectCategory), category) || category == BattleEffectCategory.All)
            throw new ArgumentException("特效必须有明确分类。");
        if (string.IsNullOrWhiteSpace(ownerName) || string.IsNullOrWhiteSpace(title) || (category == BattleEffectCategory.Common ? ownerID != 0 : ownerID <= 0))
            throw new ArgumentException("请填写有效的特效名称、来源名称与来源编号。");
        Kind = kind; Category = category; OwnerID = ownerID; OwnerName = ownerName;
        OwnerKey = category + ":" + ownerID.ToString(CultureInfo.InvariantCulture);
        Label = ownerName + " · " + title;
        string lifetime = Sustained ? "持续状态" : "单次行动";
        Detail = kind + " · " + lifetime;
        SearchText = Label + " " + ownerID.ToString(CultureInfo.InvariantCulture) + " UE." + kind + " " + kind + ".xlsx "
            + BattleEffectPreviewCatalog.CategoryLabel(category) + " " + lifetime + " " + (Sustained ? "state loop" : "action effect") + " " + aliases;
    }
}

public static class BattleEffectPreviewCatalog
{
    private static BattleEffectPreviewEntry C(BattleEffectKind kind, int id, string name, string title, string english)
        => new BattleEffectPreviewEntry(kind, BattleEffectCategory.Character, id, name, title, english);
    private static BattleEffectPreviewEntry G(BattleEffectKind kind, string title)
        => new BattleEffectPreviewEntry(kind, BattleEffectCategory.Common, 0, "公共爆炸", title, "general common explosion");
    private static readonly IReadOnlyList<BattleEffectPreviewEntry> entries = Array.AsReadOnly(new[]
    {
        G(BattleEffectKind.ExplosionLight, "轻量"), G(BattleEffectKind.Explosion, "标准"), G(BattleEffectKind.ExplosionLarge, "大型"),
        C(BattleEffectKind.AmberArrowRain, 1009, "安柏", "箭雨行动", "Amber"),
        C(BattleEffectKind.AmberBurningArea, 1009, "安柏", "持续燃烧区", "Amber"),
        C(BattleEffectKind.LisaVioletArc, 1011, "丽莎", "向上放电与落雷", "Lisa"),
        C(BattleEffectKind.KaeyaGlacialWaltz, 1010, "凯亚", "凛冽轮舞状态", "Kaeya"),
        C(BattleEffectKind.KaeyaIcicle, 1010, "凯亚", "冰棱发射与命中", "Kaeya"),
        C(BattleEffectKind.VentiStormEye, 1012, "温迪", "暴风之眼状态", "Venti"),
        C(BattleEffectKind.VentiStormPulse, 1012, "温迪", "风暴扩散", "Venti"),
        C(BattleEffectKind.VentiUpdraft, 1012, "温迪", "高天之歌风场", "Venti"),
        C(BattleEffectKind.VentiWindDomain, 1012, "温迪", "持续风场状态", "Venti"),
        C(BattleEffectKind.DandelionWindOrbit, 1013, "琴", "蒲公英环绕状态", "Jean")
    });
    public static IReadOnlyList<BattleEffectPreviewEntry> Entries => entries;
    static BattleEffectPreviewCatalog()
    {
        var kinds = new HashSet<BattleEffectKind>();
        foreach (var entry in entries) if (!kinds.Add(entry.Kind)) throw new InvalidOperationException("重复特效选项：" + entry.Kind);
        foreach (var kind in BattleEffectParameters.Kinds) Get(kind);
    }
    public static BattleEffectPreviewEntry Get(BattleEffectKind kind)
    {
        foreach (var entry in entries) if (entry.Kind == kind) return entry;
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "请补齐该特效的名称、来源分类和搜索元数据。");
    }
    public static string CategoryLabel(BattleEffectCategory category)
        => category == BattleEffectCategory.Character ? "角色" : category == BattleEffectCategory.Enemy ? "敌人" : category == BattleEffectCategory.Common ? "公共" : "全部";
}

/// <summary>Cached, composable filtering shared by authoring selectors. Never loads assets or changes playback.</summary>
public sealed class BattleEffectSelectionFilter
{
    private readonly IReadOnlyList<BattleEffectPreviewEntry> entries;
    private readonly List<BattleEffectPreviewEntry> results = new List<BattleEffectPreviewEntry>();
    private readonly List<BattleEffectPreviewEntry> owners = new List<BattleEffectPreviewEntry>();
    private BattleEffectCategory category;
    private BattleEffectLifetimeFilter lifetime;
    private string owner = "", query;
    public IReadOnlyList<BattleEffectPreviewEntry> Results => results;
    public IReadOnlyList<BattleEffectPreviewEntry> Owners => owners;
    public int TotalCount => entries.Count;
    public BattleEffectSelectionFilter(IReadOnlyList<BattleEffectPreviewEntry> entries = null)
    { this.entries = entries ?? BattleEffectPreviewCatalog.Entries; Update(BattleEffectCategory.All, "", BattleEffectLifetimeFilter.All, ""); }
    public bool Update(BattleEffectCategory nextCategory, string nextOwner, BattleEffectLifetimeFilter nextLifetime, string nextQuery)
    {
        nextOwner = nextOwner ?? ""; nextQuery = (nextQuery ?? "").Trim();
        if (category == nextCategory && owner == nextOwner && lifetime == nextLifetime && query == nextQuery) return false;
        category = nextCategory; owner = nextOwner; lifetime = nextLifetime; query = nextQuery;
        var tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        results.Clear(); owners.Clear(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (category != BattleEffectCategory.All && entry.Category != category) continue;
            if (seen.Add(entry.OwnerKey)) owners.Add(entry);
            if (owner.Length != 0 && entry.OwnerKey != owner) continue;
            if (lifetime == BattleEffectLifetimeFilter.Sustained && !entry.Sustained || lifetime == BattleEffectLifetimeFilter.OneShot && entry.Sustained) continue;
            bool match = true;
            foreach (string token in tokens) if (entry.SearchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) { match = false; break; }
            if (match) results.Add(entry);
        }
        owners.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : a.OwnerID.CompareTo(b.OwnerID));
        return true;
    }
}
