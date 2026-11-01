using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Two independently queued, edge-anchored PSD action prompts on the common results timeline.</summary>
[DisallowMultipleComponent]
public sealed class BattleActionPromptView : BattleSkillTrackBehaviour, IBattleSkillAnimationTrack
{
    public static readonly string[] LayerFiles = { "OuterRing.png", "InnerRing.png", "PortraitMask.png" };
    public BattleUiLayout layout;
    public UIAdaptiveFrame framePrefab;
    public TMP_FontAsset font;
    public Vector2 allyInset = new Vector2(48, 191), enemyInset = new Vector2(49, 191);
    private const float Height = 165, TextInset = 135, EndPadding = 26;
    private sealed class Item
    {
        public long Id;
        public BattleActionPromptData Fact;
        public BattleActionStyle Style;
        public Action<Exception> Done;
    }
    private sealed class Lane
    {
        public RectTransform Clip, Content;
        public CanvasGroup Opacity;
        public TMP_Text Header, Detail;
        public StreamingSpriteReference Portrait;
        public readonly Queue<Item> Queue = new Queue<Item>();
        public Item Current;
        public float Time, Width;
    }
    private Lane ally, enemy;

    protected override void Start()
    {
        base.Start();
        if (layout == null || layout.canvas == null || framePrefab == null || font == null || player == null)
        { Debug.LogError("行动提示缺少布局、自由边框、字体或时间轴。", this); enabled = false; return; }
        ally = Build(false); enemy = Build(true);
    }
    private static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 offset)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = offset; return rect;
    }
    private static StreamingSpriteReference Picture(RectTransform rect, string path = null)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = false;
        var reference = rect.gameObject.AddComponent<StreamingSpriteReference>();
        if (path != null) reference.SetResolvedPath("art_assets/UI/ActionPrompt/" + path);
        return reference;
    }
    private Lane Build(bool right)
    {
        float x = right ? 1 : 0, sign = right ? -1 : 1;
        Vector2 edge = new Vector2(x, .5f), top = new Vector2(x, 1);
        Vector2 inset = right ? enemyInset : allyInset;
        var clip = Box(right ? "EnemyActionPrompt" : "AllyActionPrompt", layout.canvas.transform, top, top,
            new Vector2(0, Height), new Vector2(inset.x * sign, -inset.y));
        clip.gameObject.AddComponent<RectMask2D>();
        var lane = new Lane { Clip = clip, Opacity = clip.gameObject.AddComponent<CanvasGroup>() };
        lane.Opacity.alpha = 0; lane.Opacity.blocksRaycasts = lane.Opacity.interactable = false;
        lane.Content = Box("Content", clip, edge, edge, new Vector2(0, Height), Vector2.zero);
        var frame = (RectTransform)Instantiate(framePrefab, lane.Content, false).transform;
        frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one; frame.offsetMin = frame.offsetMax = Vector2.zero;
        RectTransform Face(string name, int size) => Box(name, lane.Content, edge, Vector2.one * .5f,
            Vector2.one * size, new Vector2(sign * 73, 0));
        Picture(Face("OuterRing", 108), LayerFiles[0]); Picture(Face("InnerRing", 94), LayerFiles[1]);
        var mask = Face("PortraitMask", 80); Picture(mask, LayerFiles[2]);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        lane.Portrait = Picture(Box("Portrait", mask, Vector2.one * .5f, Vector2.one * .5f, Vector2.one * (right ? 72 : 80), Vector2.zero));
        TMP_Text Label(string name, float y, Color32 color)
        {
            var rect = Box(name, lane.Content, edge, edge, new Vector2(1, 40), new Vector2(sign * TextInset, y));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = 23.3f; label.color = color; label.raycastTarget = false;
            label.alignment = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false; label.richText = false; return label;
        }
        lane.Header = Label("ActionType", 21, new Color32(118, 109, 93, 255));
        lane.Detail = Label("ActionName", -21, new Color32(243, 243, 243, 255));
        DamagePopupOutline.Apply(lane.Detail, 2, new Color32(115, 107, 91, 255));
        return lane;
    }
    public override void Present(long id, BattlePresentationCue cue, Action<Exception> done)
    {
        if (!(cue.Result.Data is BattleActionPromptData fact) || !cue.Result.TargetSide.HasValue ||
            !isActiveAndEnabled || player.ActionStyle == null || ally == null) { done(null); return; }
        bool right = cue.Result.TargetSide == BattleSide.Enemy;
        var lane = right ? enemy : ally;
        lane.Queue.Enqueue(new Item { Id = id, Fact = fact, Done = done, Style = player.ActionStyle });
        if (lane.Current == null) Begin(lane, right);
    }
    private void Begin(Lane lane, bool right)
    {
        var item = lane.Current = lane.Queue.Dequeue();
        string key = item.Fact.IsStatus ? "ui_action_prompt_status" : right ? "ui_action_prompt_enemy" : "ui_action_prompt_ally";
        lane.Header.text = DataManager.Instance.GetLangText(key);
        lane.Detail.text = item.Fact.Name;
        float Measure(TMP_Text label, string text) => label.GetPreferredValues(text, Mathf.Infinity, Mathf.Infinity).x;
        float minimum = item.Style.MinTextCharacters * Measure(lane.Detail, "行");
        float content = Mathf.Max(Measure(lane.Detail, lane.Detail.text), Measure(lane.Header, lane.Header.text));
        float width = Mathf.Ceil(Mathf.Max(minimum, content)) + 4;
        lane.Header.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        lane.Detail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        lane.Width = TextInset + width + EndPadding;
        lane.Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lane.Width);
        string portrait = "art_assets/" + (right ? $"Enemy_Icon/{item.Fact.SubjectId}_01.png" : CharacterCardArtManifest.PortraitFile(item.Fact.SubjectId));
        if (lane.Portrait.relativePath != portrait) lane.Portrait.SetResolvedPath(portrait);
        lane.Time = 0; lane.Clip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0); lane.Opacity.alpha = 1;
    }
    private void Update() { if (ally != null) { Tick(ally, false); Tick(enemy, true); } }
    private void Tick(Lane lane, bool right)
    {
        if (lane.Current == null) { if (lane.Queue.Count != 0) Begin(lane, right); else return; }
        lane.Time += Time.unscaledDeltaTime;
        var style = lane.Current.Style;
        float reveal = style.Reveal <= 0 ? 1 : Mathf.Clamp01(lane.Time / style.Reveal);
        lane.Clip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lane.Width * Mathf.SmoothStep(0, 1, reveal));
        float endHold = style.Reveal + style.Hold;
        if (lane.Time < endHold) return;
        lane.Opacity.alpha = style.Fade <= 0 ? 0 : 1 - Mathf.Clamp01((lane.Time - endHold) / style.Fade);
        if (lane.Opacity.alpha > 0) return;
        var done = lane.Current.Done; lane.Current = null; done(null);
    }
    public override void Cancel(long id)
    {
        CancelLane(ally, id); CancelLane(enemy, id);
    }
    private static void CancelLane(Lane lane, long id)
    {
        if (lane == null) return;
        int count = lane.Queue.Count;
        while (count-- > 0)
        {
            var item = lane.Queue.Dequeue();
            if (id < 0 || item.Id == id) item.Done(null); else lane.Queue.Enqueue(item);
        }
        if (lane.Current != null && (id < 0 || lane.Current.Id == id))
        {
            var done = lane.Current.Done; lane.Current = null; lane.Opacity.alpha = 0; done(null);
        }
    }
    private void OnDestroy()
    {
        if (ally?.Clip != null) Destroy(ally.Clip.gameObject);
        if (enemy?.Clip != null) Destroy(enemy.Clip.gameObject);
    }
}
