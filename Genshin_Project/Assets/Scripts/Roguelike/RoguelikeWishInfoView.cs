using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>祈愿规则和历史的只读视图。数据由当前祈愿服务提供。</summary>
public sealed class RoguelikeWishInfoView : MonoBehaviour
{
    private const float ProbabilityIconSize = 88f;
    private const float ProbabilityIconGap = 14f;
    private const float ProbabilitySidePadding = 6f;

    [SerializeField] private GameObject probability, history;
    [SerializeField] private Button close, all, characters, weapons, previous, next, featuredFive;
    [SerializeField] private TMP_Text title, closeLabel, rateHeading, rateFive, rateFour, rateThree, limits, remaining;
    [SerializeField] private TMP_Text featuredFiveHeading, featuredFourHeading;
    [SerializeField] private TMP_Text historyPrize, historyPool, historyRarity, historyTime, historyCount, pageLabel, empty;
    [SerializeField] private RoguelikeWishCardStrip probabilityCards;
    [SerializeField] private RectTransform probabilityPools;
    [SerializeField, Min(1)] private int rowsPerPage = 8;

    private RoguelikeWishService service;
    private RoguelikeWishRules rules;
    private RoguelikeWishOffer offer;
    private RoguelikeWishLanguage language;
    private UICardFactory probabilityFactory;
    private IReadOnlyList<RoguelikeWishOffer> offers;
    private readonly List<RoguelikeWishTile> probabilityTabs = new List<RoguelikeWishTile>();
    private Action<RoguelikeWishPrize> openProbabilityDetail;
    private Action resumeTheme;
    private UICardView featuredCard;
    private IReadOnlyList<RoguelikeWishHistoryEntry> entries = Array.Empty<RoguelikeWishHistoryEntry>();
    private RoguelikeWishHistoryEntry[] visibleRows = Array.Empty<RoguelikeWishHistoryEntry>();
    private readonly List<HistoryRow> historyRows = new List<HistoryRow>();
    private Action<RoguelikeWishPrize> openHistoryDetail;
    private RoguelikeWishIssue historyIssue;
    private int page;

    private sealed class HistoryRow
    {
        public Button button;
        public StreamingSpriteReference icon;
        public TMP_Text prize, rarity, pool, time;

        public void SetVisible(bool visible)
        {
            button.gameObject.SetActive(visible);
            rarity.gameObject.SetActive(visible);
            pool.gameObject.SetActive(visible);
            time.gameObject.SetActive(visible);
        }
    }

    private void OnEnable()
    {
        close.onClick.AddListener(CloseAndResume);
        all.onClick.AddListener(ShowAll);
        characters.onClick.AddListener(ShowCharacters);
        weapons.onClick.AddListener(ShowWeapons);
        previous.onClick.AddListener(Previous);
        next.onClick.AddListener(Next);
        featuredFive.onClick.AddListener(OpenFeaturedFive);
    }

    private void OnDisable()
    {
        close.onClick.RemoveListener(CloseAndResume);
        all.onClick.RemoveListener(ShowAll);
        characters.onClick.RemoveListener(ShowCharacters);
        weapons.onClick.RemoveListener(ShowWeapons);
        previous.onClick.RemoveListener(Previous);
        next.onClick.RemoveListener(Next);
        featuredFive.onClick.RemoveListener(OpenFeaturedFive);
    }

    internal void ShowProbability(RoguelikeWishRules rules, IReadOnlyList<RoguelikeWishOffer> offers,
        RoguelikeWishOffer offer, UICardFactory cards, RoguelikeWishTile poolTemplate, RoguelikeWishLanguage language,
        Action<RoguelikeWishPrize> openDetail, Action<string> select, Action onClose)
    {
        this.rules = rules;
        this.offers = offers;
        this.language = language;
        probabilityFactory = cards;
        openProbabilityDetail = openDetail;
        resumeTheme = onClose;
        gameObject.SetActive(true);
        probability.SetActive(true);
        history.SetActive(false);
        RoguelikeWishView.Clear(probabilityTabs);
        if (probabilityPools != null && poolTemplate != null)
        {
            float preferredStride = ProbabilityIconSize + ProbabilityIconGap;
            Rect bounds = probabilityPools.rect;
            float stride = Mathf.Clamp((bounds.width - 2 * ProbabilitySidePadding) / Mathf.Max(1, offers.Count),
                0f, preferredStride);
            float iconSize = stride * (ProbabilityIconSize / preferredStride);
            for (int i = 0; i < offers.Count; i++)
            {
                var pool = offers[i].Pool;
                var tile = RoguelikeWishView.Spawn(poolTemplate, probabilityPools);
                var rect = (RectTransform)tile.transform;
                rect.anchoredPosition = new Vector2(ProbabilitySidePadding + iconSize * .5f + i * stride,
                    -bounds.height * .5f);
                rect.localScale = Vector3.one * (iconSize / rect.rect.width);
                tile.BindIcon(pool.Featured, () => select(pool.Id));
                probabilityTabs.Add(tile);
            }
        }
        SelectProbabilityOffer(offer);
    }

    internal void SelectProbabilityOffer(RoguelikeWishOffer selected)
    {
        if (!probability.activeSelf || offer == selected) return;
        if (featuredCard != null && probabilityFactory != null) probabilityFactory.Release(featuredCard);
        offer = selected;
        WriteProbabilityLabels();
        featuredCard = probabilityFactory == null ? null :
            RoguelikeWishTile.RequestCard(probabilityFactory, (RectTransform)featuredFive.transform, offer.Pool.Featured);
        probabilityCards.Bind(probabilityFactory, offer.Pool, language, openProbabilityDetail);
        for (int i = 0; i < probabilityTabs.Count; i++) probabilityTabs[i].SetSelected(offers[i] == offer);
    }

    private void OpenFeaturedFive()
    {
        if (offer != null) openProbabilityDetail?.Invoke(offer.Pool.Featured);
    }

    private void WriteProbabilityLabels()
    {
        title.text = language.Label("ui_wish_rates_title", "{0} · 概率", "{0} · Rates", language.PoolTitle(offer.Pool));
        bool character = offer.Pool.Featured.Kind == RoguelikeWishKind.Character;
        float five = character ? rules.CharacterFiveRate : rules.WeaponFiveRate;
        float four = character ? rules.CharacterFourRate : rules.WeaponFourRate;
        closeLabel.text = language.Label("btn_wish_back_to_banners", "返回卡池", "Back to Banners");
        rateHeading.text = language.Label("ui_wish_base_rates", "单次基础概率", "Base Rates per Wish");
        featuredFiveHeading.text = language.Label("ui_wish_featured_five", "当期 5★", "Featured 5★");
        featuredFourHeading.text = language.Label("ui_wish_featured_four", "当期 4★", "Featured 4★");
        rateFive.text = $"<color=#694618>5★</color><pos=65%>{five:0.00%}";
        rateFour.text = $"<color=#593971>4★</color><pos=65%>{four:0.00%}";
        rateThree.text = $"<color=#3A4B60>3★</color><pos=65%>{1f - five - four:0.00%}";
        int limit = character ? rules.CharacterLimit : rules.WeaponLimit;
        limits.text = language.Label("ui_wish_rate_guarantees",
            "最多 {0} 抽获得 5★，距离保底还剩 {1} 抽。\n第 {2} 抽保底 4★，遇 5★ 顺延。距离保底还剩 {3} 抽。",
            "A 5★ reward is guaranteed within {0} wishes. {1} remaining.\n4★ pity is at wish {2}, delayed by a 5★. {3} remaining.",
            limit, offer.UntilFive, rules.FourStarLimit, offer.UntilFour);
        remaining.text = language.Label("ui_wish_pity_carryover", "同类型卡池共用保底计数。角色与武器卡池分别计数，进入下一章时继承。",
            "Banners of the same type share pity. Character and weapon counters are separate and carry over to the next chapter.");
    }

    internal void ShowHistory(RoguelikeWishService source, RoguelikeWishLanguage language,
        Action<RoguelikeWishPrize> openDetail, Action onClose)
    {
        service = source;
        this.language = language;
        openHistoryDetail = openDetail;
        resumeTheme = onClose;
        gameObject.SetActive(true);
        probability.SetActive(false);
        history.SetActive(true);
        ShowAll();
    }

    internal void RefreshLanguage()
    {
        if (language == null) return;
        if (probability.activeSelf)
        {
            WriteProbabilityLabels();
            probabilityCards.RefreshLanguage(language);
        }
        else ShowPage();
    }

    internal void Close()
    {
        service = null;
        offer = null;
        rules = null;
        language = null;
        historyIssue = RoguelikeWishIssue.None;
        entries = Array.Empty<RoguelikeWishHistoryEntry>();
        visibleRows = Array.Empty<RoguelikeWishHistoryEntry>();
        openHistoryDetail = null;
        openProbabilityDetail = null;
        resumeTheme = null;
        offers = null;
        if (featuredCard != null && probabilityFactory != null) probabilityFactory.Release(featuredCard);
        featuredCard = null;
        probabilityFactory = null;
        RoguelikeWishView.Clear(probabilityTabs);
        if (probabilityCards != null) probabilityCards.Clear();
        gameObject.SetActive(false);
    }

    internal void CloseAndResume()
    {
        var onClose = resumeTheme;
        Close();
        onClose?.Invoke();
    }

    private void ShowAll() => Filter(null);
    private void ShowCharacters() => Filter(RoguelikeWishKind.Character);
    private void ShowWeapons() => Filter(RoguelikeWishKind.Weapon);

    private void Filter(RoguelikeWishKind? kind)
    {
        if (service == null) return;
        var result = service.ReadHistory(kind);
        entries = result.Succeeded ? result.Data : Array.Empty<RoguelikeWishHistoryEntry>();
        historyIssue = result.Succeeded ? RoguelikeWishIssue.None : RoguelikeWishService.ReadIssue(result);
        all.interactable = kind.HasValue;
        characters.interactable = kind != RoguelikeWishKind.Character;
        weapons.interactable = kind != RoguelikeWishKind.Weapon;
        page = 0;
        ShowPage();
    }

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)rowsPerPage));
    private void Previous() { if (page > 0) { page--; ShowPage(); } }
    private void Next() { if (page + 1 < PageCount) { page++; ShowPage(); } }

    private void ShowPage()
    {
        title.text = language.Label("ui_wish_history", "祈愿历史", "Wish History");
        closeLabel.text = language.Label("btn_wish_back_to_banners", "返回卡池", "Back to Banners");
        empty.text = historyIssue switch
        {
            RoguelikeWishIssue.None => language.Label("ui_wish_history_empty", "暂无祈愿记录", "No wishes recorded."),
            RoguelikeWishIssue.NoSave => language.DrawFeedback(historyIssue, null, 0),
            _ => language.Label("tip_wish_history_load", "祈愿历史加载失败。", "Could not load wish history.")
        };
        var rows = entries.Skip(page * rowsPerPage).Take(rowsPerPage).ToArray();
        BindHistoryRows(rows);
        if (historyCount != null)
            historyCount.text = language.Label("ui_wish_history_count", "共 {0} 条", "{0} entries", entries.Count);
        empty.gameObject.SetActive(entries.Count == 0);
        previous.interactable = page > 0;
        next.interactable = page + 1 < PageCount;
        pageLabel.text = $"{page + 1} / {PageCount}";
    }

    private void BindHistoryRows(RoguelikeWishHistoryEntry[] rows)
    {
        visibleRows = rows;
        while (historyRows.Count < rowsPerPage)
        {
            int index = historyRows.Count;
            var target = new GameObject($"Open prize {index + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)target.transform;
            PlaceRow(rect, historyPrize.rectTransform, index);
            var button = target.GetComponent<Button>();
            button.targetGraphic = target.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = new Color(1, 1, 1, .01f);
            colors.highlightedColor = colors.selectedColor = new Color(1, 1, 1, .10f);
            colors.pressedColor = new Color(1, 1, 1, .16f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                if (index >= visibleRows.Length) return;
                var row = visibleRows[index];
                openHistoryDetail?.Invoke(new RoguelikeWishPrize(row.PrizeKind, row.PrizeId, row.Stars, row.PrizeName));
            });
            var icon = new GameObject("Reward icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.rectTransform.SetParent(rect, false);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(30f, 0f);
            icon.rectTransform.sizeDelta = new Vector2(52f, 52f);
            icon.raycastTarget = false;
            var prizeLabel = AddCell(historyPrize, rect);
            prizeLabel.rectTransform.offsetMin = new Vector2(68f, 0f);
            historyRows.Add(new HistoryRow
            {
                button = button,
                icon = icon.gameObject.AddComponent<StreamingSpriteReference>(),
                prize = prizeLabel,
                rarity = AddCell(historyRarity, historyRarity.rectTransform, index),
                pool = AddCell(historyPool, historyPool.rectTransform, index),
                time = AddCell(historyTime, historyTime.rectTransform, index)
            });
        }
        for (int i = 0; i < historyRows.Count; i++)
        {
            var display = historyRows[i];
            if (i < rows.Length)
            {
                var row = rows[i];
                string path = RoguelikeWishTile.IconPath(row.PrizeKind, row.PrizeId);
                if (display.icon.relativePath != path) display.icon.SetResolvedPath(path);
                display.prize.text = language.Name(row.PrizeKind, row.PrizeId, row.PrizeName);
                display.rarity.text = $"{row.Stars}★";
                display.pool.text = language.PoolTitle(row.WishId, row.PoolTitle);
                display.time.text = DateTimeOffset.TryParse(row.DrawnAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
                display.button.interactable = openHistoryDetail != null;
            }
            display.SetVisible(i < rows.Length);
        }
    }

    private TMP_Text AddCell(TMP_Text style, RectTransform parent, int index = 0)
    {
        var label = new GameObject("Cell", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        if (parent == style.rectTransform) PlaceRow(label.rectTransform, parent, index);
        else
        {
            label.rectTransform.SetParent(parent, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        }
        label.font = style.font;
        label.fontSize = style.fontSize;
        label.color = style.color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private void PlaceRow(RectTransform rect, RectTransform column, int index)
    {
        rect.SetParent(column, false);
        rect.anchorMin = new Vector2(0, 1f - (index + 1f) / rowsPerPage);
        rect.anchorMax = new Vector2(1, 1f - index / (float)rowsPerPage);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
