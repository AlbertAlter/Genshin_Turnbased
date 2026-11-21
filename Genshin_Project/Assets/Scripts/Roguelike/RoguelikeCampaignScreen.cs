using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Roguelike.Tests.RouteGeneration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared campaign presentation. Every scene explicitly supplies its content frame.</summary>
internal sealed class RoguelikeCampaignScreen
{
    internal static readonly Color Cream = new Color32(238, 224, 196, 255);
    internal static readonly Color Deep = new Color32(70, 57, 46, 255);
    private static readonly Color Gold = new Color32(233, 207, 158, 255);
    private static readonly Color Ink = new Color32(57, 42, 30, 255);
    private readonly RectTransform mount;
    private readonly TMP_FontAsset font;
    private readonly UIAdaptiveFrame frame;
    private readonly Action<Action> dispatch;
    private readonly UIFloatingTip tip;
    private RectTransform page, modal;
    private Action modalBack;
    private Func<bool> modalChildBack;
    private int choices, links;
    private bool ready = true;
    private readonly List<StreamingSpriteReference> readinessArt = new();
    internal bool Ready
    {
        get
        {
            if (!ready || page == null) return false;
            page.GetComponentsInChildren(false, readinessArt);
            foreach (var asset in readinessArt)
                if (asset.IsLoading) return false;
            return true;
        }
    }
    internal RectTransform Root => page;

    internal RoguelikeCampaignScreen(RectTransform mount, TMP_FontAsset font, Action<Action> dispatch, UIAdaptiveFrame frame = null)
    {
        this.mount = mount; this.font = font; this.dispatch = dispatch; this.frame = frame;
        tip = UIFloatingTip.Create(mount, font, DataManager.Instance); Clear();
    }
    internal static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer; return rect;
    }
    internal static void Stretch(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
    }
    internal static void Place(RectTransform rect, Vector2 anchor, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
    private static Image Fill(RectTransform target, Color color, bool raycast = false)
    {
        var image = target.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
    }
    private static void Remove(RectTransform target)
    {
        if (target == null) return;
        target.gameObject.SetActive(false); UnityEngine.Object.Destroy(target.gameObject);
    }
    internal void Clear()
    {
        Remove(page); modal = null; modalBack = null; modalChildBack = null;
        page = Rect("Campaign controls", mount); Stretch(page); choices = links = 0; ready = true;
    }
    internal void Page(string title)
    {
        Clear(); Fill(page, new Color32(153, 143, 129, 255));
        var head = Rect("Chapter", page); Place(head, new Vector2(0, 1), 52, -25, 1000, 74);
        Label(head, title, 36, TextAlignmentOptions.MidlineLeft, Ink);
    }
    internal TMP_Text Label(RectTransform rect, string message, float size, TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
    {
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = message; label.fontSize = size;
        label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
        label.color = color ?? Cream; label.alignment = alignment; label.raycastTarget = false;
        label.enableWordWrapping = true; label.richText = false; return label;
    }
    internal Button Button(RectTransform parent, string title, Vector2 anchor, float x, float y, float width, float height, Action action, bool primary = false)
    {
        var rect = Rect(title, parent); Place(rect, anchor, x, y, width, height);
        var image = Fill(rect, new Color32(231, 201, 145, 163), true);
        Round(image);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => dispatch(action));
        var face = Rect("Face", rect); Stretch(face, 1);
        Round(Fill(face, primary ? Gold : Deep));
        var content = Rect("Label", rect); Stretch(content, 8);
        Label(content, title, 24, color: primary ? Ink : Cream); return button;
    }
    internal void Choice(string title, Action action)
        => Button(page, title, new Vector2(.5f, .5f), 0, -20 - 68 * choices++, 384, 56, action, choices == 1);
    internal void Nav(string title, Action action)
        => Button(page, title, Vector2.one, -42 - 156 * links++, -28, 144, 54, action);
    internal void Back(string title, Action action)
        => Button(page, title, Vector2.zero, 42, 28, 248, 54, action);
    internal void SceneBack(Action action)
    {
        RouteTool("Back", Vector2.one, -25, -25, action, strokes: new[] {
            new[] { new Vector2(15, 4), new Vector2(7, 12), new Vector2(15, 20) } });
    }
    internal void RoutePage()
    {
        Clear(); Fill(page, new Color32(170, 153, 125, 255));
    }
    private Button RouteTool(string title, Vector2 anchor, float x, float y, Action action, string art = null, Vector2[][] strokes = null)
    {
        var rect = Rect(title, page); Place(rect, anchor, x, y, 62, 62);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = Fill(rect, Color.clear, true);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => dispatch(action));
        var icon = Rect("Icon", rect); Stretch(icon, 9.3f);
        if (art != null) Art(icon, art, Cream);
        else if (strokes != null) { var graphic = icon.gameObject.AddComponent<UIStrokeIcon>(); graphic.color = Cream; graphic.SetPaths(strokes); }
        return button;
    }
    internal void RouteNavigation(int fates, string[] names, Action exit, Action wish, Action shop, Action characters, Action bag, Action formation)
    {
        RouteTool(names[0], new Vector2(0, 1), 31, -24, exit, strokes: new[] {
            new[] { new Vector2(12, 4), new Vector2(19, 4), new Vector2(19, 20), new Vector2(12, 20) },
            new[] { new Vector2(13, 12), new Vector2(4, 12), new Vector2(8, 8) }, new[] { new Vector2(4, 12), new Vector2(8, 16) } });
        var wallet = Rect("Fates", page); Place(wallet, Vector2.one, -446, -34, 110, 42);
        Round(Fill(wallet, new Color32(68, 56, 46, 232)));
        var currency = Rect("Currency icon", wallet); Place(currency, new Vector2(0, .5f), 13, 0, 29, 29);
        Art(currency, "art_assets/ItemIcon/670101.png", Color.white);
        var value = Rect("Balance", wallet); Stretch(value); value.offsetMin = new Vector2(50, 0);
        Label(value, fates.ToString(), 20, TextAlignmentOptions.MidlineLeft);
        RouteTool(names[1], Vector2.one, -363, -24, wish, "art_assets/UI/Icon/WishIcon.png");
        RouteTool(names[2], Vector2.one, -280, -24, shop, strokes: new[] {
            new[] { new Vector2(5,10), new Vector2(5,20), new Vector2(19,20), new Vector2(19,10) },
            new[] { new Vector2(3,9), new Vector2(3,7), new Vector2(5,3), new Vector2(19,3), new Vector2(21,7), new Vector2(21,9), new Vector2(19,11), new Vector2(17,10), new Vector2(15,11), new Vector2(12,10), new Vector2(10,11), new Vector2(7,10), new Vector2(5,11), new Vector2(3,9) },
            new[] { new Vector2(10,20), new Vector2(10,15), new Vector2(14,15), new Vector2(14,20) },
            new[] { new Vector2(3,7), new Vector2(21,7) } });
        RouteTool(names[3], Vector2.one, -197, -24, characters, "art_assets/UI/Icon/CharacterIcon.png");
        RouteTool(names[4], Vector2.one, -114, -24, bag, "art_assets/UI/Icon/BackpackIcon.png");
        RouteTool(names[5], Vector2.one, -31, -24, formation, "art_assets/UI/Icon/PartySetupIcon.png");
    }
    internal void Notice(string message) => tip.Show(message);
    internal static void Art(RectTransform rect, string path, Color color)
    {
        var image = Fill(rect, color); image.enabled = false;
        rect.gameObject.AddComponent<StreamingSpriteReference>().SetResolvedPath(path);
    }
    private static void Round(Image image)
    {
        image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4;
        var asset = image.gameObject.AddComponent<StreamingSpriteReference>();
        asset.spriteBorder = Vector4.one * 28; asset.allowCrop = true;
        asset.SetResolvedPath("art_assets/UI/CharacterList/Shapes/RoundedMask.png");
    }
    internal void Menu(string summary)
    {
        Clear(); UIProjectMenuBackground.Apply(page);
        var logo = Rect("Game logo", page); Place(logo, Vector2.one * .5f, 0, 244, 749, 288);
        Art(logo, "art_assets/Title_Menu.png", Color.white);
        var resume = Rect("Resume summary", page); Place(resume, Vector2.one * .5f, 0, 39, 900, 45);
        Label(resume, summary, 21);
    }
    internal RectTransform Window(string title, string subtitle, Action close, bool wide = false)
    {
        CloseWindow(); modalBack = close; modal = Rect("Campaign modal", page); Stretch(modal);
        var dismiss = Fill(modal, new Color32(26, 21, 17, 170), true);
        var outside = modal.gameObject.AddComponent<Button>(); outside.targetGraphic = dismiss;
        outside.onClick.AddListener(() => dispatch(close)); outside.navigation = new Navigation { mode = Navigation.Mode.None };
        var panel = Rect("Window", modal); Place(panel, Vector2.one * .5f, 0, 0, wide ? 1536 : 1018, wide ? 864 : 810);
        Fill(panel, Deep, true);
        if (frame != null)
        {
            var border = UnityEngine.Object.Instantiate(frame, panel, false);
            Stretch((RectTransform)border.transform); border.fillColor = Deep;
        }
        var heading = Rect("Window title", panel); Place(heading, new Vector2(0, 1), 32, -26, wide ? 1300 : 830, 54);
        Label(heading, title, 32, TextAlignmentOptions.MidlineLeft);
        var sub = Rect("Window subtitle", panel); Place(sub, new Vector2(0, 1), 32, -86, wide ? 1300 : 890, 48);
        Label(sub, subtitle, 22, TextAlignmentOptions.MidlineLeft);
        Button(panel, "×", Vector2.one, -28, -26, 52, 52, close);
        var body = Rect("Window content", panel); Stretch(body);
        body.offsetMin = new Vector2(32, 30); body.offsetMax = new Vector2(-32, -150); return body;
    }
    internal void CloseWindow() { Remove(modal); modal = null; modalBack = null; modalChildBack = null; }
    internal void SetModalChildBack(Func<bool> back) => modalChildBack = back;
    internal bool TryNavigateBack()
    {
        if (modal == null) return false;
        if (modalChildBack?.Invoke() != true) dispatch(modalBack ?? CloseWindow);
        return true;
    }
    internal void Confirmation(string title, string message, string confirm, string cancel, Action yes, Action no)
    {
        var body = Window(title, "", no);
        var text = Rect("Confirmation", body); Stretch(text); text.offsetMin = new Vector2(10, 120); Label(text, message, 27);
        Button(body, cancel, Vector2.zero, 15, 15, 250, 56, () => { CloseWindow(); no(); });
        Button(body, confirm, new Vector2(1, 0), -15, 15, 300, 56, () => { CloseWindow(); yes(); }, true);
    }
    internal void Slot(RectTransform parent, int index, string title, string summary, int[] party,
        string actionLabel, Action action, string detailsLabel, Action details, string recoverLabel, Action recover)
    {
        var row = Rect("Save slot " + index, parent);
        Place(row, new Vector2(0, 1), 0, -(index - 1) * 187, 954, 172); Fill(row, new Color32(53, 43, 36, 255));
        var name = Rect("Slot title", row); Place(name, new Vector2(0, 1), 18, -12, 520, 37); Label(name, title, 25, TextAlignmentOptions.MidlineLeft);
        var description = Rect("Saved at", row); Place(description, new Vector2(0, 1), 18, -50, 570, 32); Label(description, summary, 20, TextAlignmentOptions.MidlineLeft);
        for (int i = 0; i < party.Length; i++)
        {
            var portrait = Rect("Party member", row); Place(portrait, Vector2.zero, 18 + i * 64, 15, 56, 56);
            Art(portrait, RoguelikeRosterView.CharacterListPortraitPath(party[i]), Color.white);
        }
        if (action != null) Button(row, actionLabel, Vector2.one, -14, -14, 270, 55, action, true);
        if (details != null) Button(row, detailsLabel, new Vector2(1, 0), -14, 14, 130, 50, details);
        if (recover != null) Button(row, recoverLabel, new Vector2(1, 0), details == null ? -14 : -154, 14, 180, 50, recover);
    }
    internal void Map(RoguelikePlayableRoutes routes, RoguelikeProgressSnapshot snapshot, Action<RouteNodeData> select)
    {
        ready = false;
        var map = routes.Map(snapshot); var allowed = routes.Next(snapshot).Select(node => node.Id).ToHashSet();
        var viewport = Rect("Route", page); Stretch(viewport); viewport.SetAsFirstSibling();
        Fill(viewport, new Color32(153, 143, 129, 255), true); viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
        var content = Rect("Map", viewport); Place(content, new Vector2(0, .5f), 0, 0, map.ContentWidth, routes.Settings.RouteHeight);
        scroll.content = content; scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        foreach (int direction in new[] { -1, 1 })
            RouteTool(direction < 0 ? "‹" : "›", new Vector2(.5f, 0), direction * 41.5f, 20,
                () => { scroll.velocity = Vector2.zero; scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition + direction * viewport.rect.width / Mathf.Max(1, map.ContentWidth - viewport.rect.width) * .75f); },
                strokes: new[] { new[] { new Vector2(direction < 0 ? 15 : 9, 4), new Vector2(direction < 0 ? 7 : 17, 12), new Vector2(direction < 0 ? 15 : 9, 20) } });
        var edges = Rect("Paths", content); Stretch(edges); var nodes = Rect("Nodes", content); Stretch(nodes);
        var view = viewport.gameObject.AddComponent<RouteMapView>();
        view.Initialize(content, null, edges, nodes, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), Vector2.one * 30);
        view.CanSelect = node => allowed.Contains(node.Id); view.Selected = node => dispatch(() => select(node));
        void Draw()
        {
            if (view == null || viewport == null) return;
            view.Render(map, routes.Settings); Canvas.ForceUpdateCanvases();
            float range = map.ContentWidth - viewport.rect.width;
            scroll.horizontalNormalizedPosition = range <= 0 ? 0 : Mathf.Clamp01((routes.Settings.StageStartX + snapshot.CurrentStageIndex * routes.Settings.StageWidth - viewport.rect.width / 2) / range);
        }
        IEnumerator Style()
        {
            yield return view.ApplyStyle(routes.Style, routes.Art);
            Draw(); ready = true;
        }
        view.StartCoroutine(Style());
    }
}
