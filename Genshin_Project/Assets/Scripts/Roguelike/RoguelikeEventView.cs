using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>公共遭遇页：只展示已选事件并转交迎战/离开命令。布局挂点由场景保存，不管理存档或随机抽取。</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeEventView : MonoBehaviour
{
    [SerializeField] private RectTransform backdropMount, headerMount, artMount, bodyMount, actionsMount;
    [SerializeField] private UICardFactory cardFactory;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    [SerializeField] private TMP_FontAsset font;
    private TMP_Text heading, context, title, story, place, enemyLabel, hint;
    private Button primary, leave;
    private TMP_Text primaryText, leaveText;
    private UIAdaptiveFrame border;
    private StreamingSpriteReference background;
    private UICardView card;
    private Image eventSymbol;
    private Action accepted, left;
    private bool built, dispatching;
    public bool Ready => built && (border == null || border.Ready || !string.IsNullOrEmpty(border.Error)) &&
        (background == null || !background.IsLoading) && (card == null || card.Ready || !string.IsNullOrEmpty(card.Error));

    public void Bind(DataManager data, RoguelikeEventEncounter encounter, int stage, int level, bool english,
        Action accept, Action exit, bool preview = false)
    {
        if (data == null || encounter == null) throw new ArgumentNullException(nameof(encounter));
        Build(); ReleaseCard(); accepted = accept; left = exit;
        string L(string key, params object[] args) => data.GetLangText(key, english, args);
        heading.text = L("ui_event_title");
        context.text = preview ? L("ui_event_preview") : L("ui_event_stage", stage);
        title.text = L(encounter.TitleKey); story.text = L(encounter.TextKey).Replace("\\n", "\n");
        place.text = string.IsNullOrEmpty(encounter.PlaceKey) ? "" : L(encounter.PlaceKey);
        string enemies = string.Join(" · ", encounter.EnemyIds.GroupBy(id => id).Select(group =>
            data.GetUiName(UiNameKind.Enemy, group.Key, group.Key.ToString(), english) +
            (group.Count() > 1 ? " × " + group.Count() : "")));
        enemyLabel.text = encounter.EnemyIds.Length == 0 ? "" : L("ui_event_enemy", enemies, level);
        primaryText.text = L(encounter.ActionKey); leaveText.text = L("btn_event_leave");
        hint.text = preview ? L("ui_event_preview_hint") : accept == null ? L("ui_event_unavailable") : L("ui_event_leave_hint");
        primary.interactable = accept != null; leave.interactable = exit != null;
        // 只向公共卡牌服务请求敌人身份，不卡面路径、不复制卡框或动态回退逻辑。
        eventSymbol.gameObject.SetActive(encounter.PreviewEnemyId <= 0);
        if (encounter.PreviewEnemyId > 0)
            card = cardFactory.Request(artMount, BattleSide.Enemy, encounter.PreviewEnemyId, animated: true, showShadows: false);
    }

    public void ShowBindingError(string message)
    {
        Build(); ReleaseCard(); accepted = left = null;
        heading.text = ""; title.text = ""; story.text = message;
        primary.interactable = leave.interactable = false;
    }

    private void Build()
    {
        if (built) return;
        if (backdropMount == null || headerMount == null || artMount == null || bodyMount == null ||
            actionsMount == null || font == null || framePrefab == null || cardFactory == null)
            throw new InvalidOperationException("事件场景缺少布局、公共卡牌工厂、边框或字体引用。");
        built = true;
        border = Instantiate(framePrefab, backdropMount, false);
        RoguelikeCampaignScreen.Stretch((RectTransform)border.transform);
        border.showFill = true;
        border.BindOpeningDecoration(opening => background = UIProjectMenuBackground.Apply(opening));
        var symbol = Region(artMount, "Encounter symbol", .15f, .25f, .85f, .75f);
        eventSymbol = symbol.gameObject.AddComponent<Image>(); eventSymbol.preserveAspect = true;
        eventSymbol.raycastTarget = false; eventSymbol.color = RoguelikeCampaignScreen.Cream;
        symbol.gameObject.AddComponent<StreamingSpriteReference>().SetResolvedPath("art_assets/rouge/routeicon/Event_v1.png");
        heading = Label(Region(headerMount, "Title", 0, 0, .65f, 1), 38);
        context = Label(Region(headerMount, "Context", .65f, 0, 1, 1), 24, TextAlignmentOptions.MidlineRight);
        title = Label(Region(bodyMount, "Encounter title", 0, .79f, 1, 1), 52);
        place = Label(Region(bodyMount, "Place", 0, .7f, 1, .79f), 24);
        var viewport = Region(bodyMount, "Story viewport", 0, .15f, 1, .67f);
        var mask = viewport.gameObject.AddComponent<Image>(); mask.color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Region(viewport, "Story", 0, 1, 1, 1);
        content.pivot = new Vector2(.5f, 1);
        story = Label(content, 30, TextAlignmentOptions.TopLeft); story.lineSpacing = 15;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
        enemyLabel = Label(Region(bodyMount, "Enemies", 0, 0, 1, .12f), 24);
        primary = Control(Region(actionsMount, "Accept", 0, .53f, 1, 1), true, () => Invoke(accepted), out primaryText);
        leave = Control(Region(actionsMount, "Leave", .7f, .05f, 1, .43f), false, () => Invoke(left), out leaveText);
        hint = Label(Region(actionsMount, "Hint", 0, .05f, .67f, .43f), 20);
    }

    private void Invoke(Action action)
    {
        if (dispatching || action == null || !Ready) return;
        dispatching = true;
        try { action(); } finally { dispatching = false; }
    }

    private Button Control(RectTransform target, bool highlighted, Action clicked, out TMP_Text caption)
    {
        var face = target.gameObject.AddComponent<Image>();
        face.color = highlighted ? new Color32(233, 207, 158, 255) : new Color32(83, 70, 55, 255);
        var button = target.gameObject.AddComponent<Button>(); button.targetGraphic = face;
        caption = Label(Region(target, "Label", .05f, .08f, .95f, .92f), 29, TextAlignmentOptions.Center);
        caption.color = highlighted ? RoguelikeCampaignScreen.Deep : RoguelikeCampaignScreen.Cream;
        button.onClick.AddListener(() => clicked());
        target.gameObject.AddComponent<UIButtonClickFeedback>().Bind(target, canUse: () => button.IsInteractable() && Ready);
        return button;
    }

    private TMP_Text Label(RectTransform target, float size, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = target.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = size; label.color = RoguelikeCampaignScreen.Cream;
        label.alignment = alignment; label.enableWordWrapping = true; label.raycastTarget = false;
        return label;
    }

    private static RectTransform Region(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        var child = RoguelikeCampaignScreen.Rect(name, parent);
        child.anchorMin = new Vector2(x0, y0); child.anchorMax = new Vector2(x1, y1);
        child.offsetMin = child.offsetMax = Vector2.zero; return child;
    }

    private void ReleaseCard() { if (card != null && cardFactory != null) cardFactory.Release(card); card = null; }
    private void OnDisable() { accepted = left = null; ReleaseCard(); }
    private void OnDestroy() { ReleaseCard(); }
}
