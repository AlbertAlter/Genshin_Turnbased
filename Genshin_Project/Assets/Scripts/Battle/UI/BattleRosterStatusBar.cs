using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Visible personal statuses, newest first, for one roster slot.</summary>
public sealed class BattleRosterStatusBar : MonoBehaviour
{
    int capacity;
    public const string Background = "UI/BattleField1/UI_components/StatusBar/Status_BG.png";
    sealed class Badge
    {
        public CanvasGroup group;
        public StreamingSpriteReference icon;
        public TMP_Text count;
        public BattleStatusDescriptionView description;
        public StatusInstance status;
        public string configuredIcon;
    }

    readonly List<Badge> badges = new List<Badge>(5);
    BattleSide side;
    int position;
    float nextRefresh;
    BattleEntity occupant;
    TMP_Text sample;
    BattleInputController input;
    bool initialized;

    public static void Bind(Transform container, BattleSide side, int position, bool boss = false, BattleInputController input = null)
    {
        if (container == null) return;
        var bar = container.GetComponent<BattleRosterStatusBar>() ?? container.gameObject.AddComponent<BattleRosterStatusBar>();
        bar.side = side;
        bar.position = position;
        bar.input = input;
        bar.nextRefresh = 0;
        bar.capacity = boss ? 10 : side == BattleSide.Enemy ? 4 : 5;
        if (!bar.initialized)
        {
            bar.sample = container.GetComponentInChildren<TMP_Text>(true);
            foreach (Transform child in container) child.gameObject.SetActive(false);
            bar.initialized = true;
        }
        container.gameObject.SetActive(true);
    }

    void OnEnable() => nextRefresh = 0;

    void LateUpdate()
    {
        var battle = BattleManager.Instance;
        var entity = battle != null ? battle.GetEntityByPosition(side, position) : null;
        if (entity == null || !entity.IsAlive || !entity.IsOnField) entity = null;
        if (entity != occupant || BattlePause.UnscaledTime >= nextRefresh)
        {
            occupant = entity;
            nextRefresh = BattlePause.UnscaledTime + .1f;
            var statuses = entity?.GetStatusList(); // Sorted by the actual instance's ApplyOrder, oldest first.
            int used = 0;
            for (int i = (statuses?.Count ?? 0) - 1; i >= 0 && used < capacity; i--)
            {
                var status = statuses[i];
                if (!status.IsActive || status.MainData?.Display != 1) continue;
                if (used == badges.Count) badges.Add(CreateBadge(used));
                Show(badges[used++], status, entity);
            }
            for (int i = used; i < badges.Count; i++)
            {
                badges[i].status = null;
                badges[i].group.gameObject.SetActive(false);
            }
        }
        foreach (var badge in badges)
            if (badge.status != null)
                badge.group.alpha = !badge.status.IsPermanent && badge.status.RemainingPhaseCount == 1
                    ? .35f + .65f * Mathf.Abs(Mathf.Sin(BattlePause.UnscaledTime * 4f)) : 1f;
    }

    void Show(Badge badge, StatusInstance status, BattleEntity entity)
    {
        badge.group.gameObject.SetActive(true);
        badge.status = status;
        badge.description.Bind(status, entity, input);
        var data = status.MainData;
        string icon = string.IsNullOrWhiteSpace(data.Icon)
            ? (data.StatusType == "Debuff" ? "GeneralDebuff" : "GeneralBuff") : data.Icon;
        if (badge.configuredIcon != icon)
        {
            badge.configuredIcon = icon;
            badge.icon.SetResolvedPath(IconPath(icon));
        }
        bool counted = BattleStatusDescriptionView.ShowsCount(status);
        badge.count.enabled = counted;
        if (counted)
            badge.count.text = status.HasIntegerRuntimeValue ? status.RuntimeIntValue.ToString(CultureInfo.InvariantCulture)
                : status.RuntimeValue.ToString("0.##", CultureInfo.InvariantCulture);
    }

    internal static string IconPath(string id)
    {
        string name = Path.GetFileNameWithoutExtension(id);
        if (name.Length > 0 && char.IsDigit(name[0]) && CharacterSkillIconResolver.TryGetRelativePath(name, out string skill)) return skill;
        string path = "art_assets/Status_Icon/" + name + ".png";
        return File.Exists(Path.Combine(Application.streamingAssetsPath, path)) ? path : "art_assets/Status_Icon/Enemy/" + name + ".png";
    }

    Badge CreateBadge(int index)
    {
        var rect = NewRect("Status " + (index + 1), transform, 40);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(17 + index * (side == BattleSide.Enemy ? 48 : 43.5f), -17);
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        var outer = ImageLayer(rect, "Status_BG", 40);
        outer.GetComponent<Image>().raycastTarget = true;
        outer.SetResolvedPath("art_assets/" + Background);
        var icon = ImageLayer(rect, "Icon", 25);
        var label = NewRect("Count", rect, 40);
        label.anchoredPosition = new Vector2(4, -13);
        var count = label.gameObject.AddComponent<TextMeshProUGUI>();
        if (sample != null) { count.font = sample.font; count.fontSharedMaterial = sample.fontSharedMaterial; count.color = sample.color; }
        count.fontSize = 20.83333f;
        count.enableWordWrapping = false;
        count.alignment = TextAlignmentOptions.MidlineRight;
        count.raycastTarget = false;
        label.gameObject.AddComponent<PsdApTextOutline>().Configure(new Color32(113, 103, 88, 255), 1f);
        return new Badge { group = group, icon = icon, count = count, description = rect.gameObject.AddComponent<BattleStatusDescriptionView>() };
    }

    static RectTransform NewRect(string name, Transform parent, float size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(size, size);
        return rect;
    }

    static StreamingSpriteReference ImageLayer(RectTransform parent, string name, float size)
    {
        var rect = NewRect(name, parent, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        return rect.gameObject.AddComponent<StreamingSpriteReference>();
    }
}
