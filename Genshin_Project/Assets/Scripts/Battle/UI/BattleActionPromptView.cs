using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared side prompts for player target selection and the battle results timeline.</summary>
[DisallowMultipleComponent]
public sealed class BattleActionPromptView : BattleSkillTrackBehaviour, IBattleSkillAnimationTrack
{
    private const string EmblemFile = "PaimonEmblem.png";
    public static readonly string[] LayerFiles = { "OuterRing.png", "InnerRing.png", "PortraitMask.png", EmblemFile };
    public BattleUiLayout layout;
    public BattleInputController selectionInput;
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
    private Lane selectionLane;
    private BattleEntity selectionActor;
    private string selectionSkillId;
    private float selectionTime, selectionFadeTime;

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
        var frameView = Instantiate(framePrefab, lane.Content, false);
        var frame = (RectTransform)frameView.transform;
        frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one; frame.offsetMin = frame.offsetMax = Vector2.zero;
        frameView.BindOpeningDecoration(opening =>
        {
            // BattleField1.psd: 145×152 px, 3 px from the inner edge, 44 px above the top edge.
            var emblem = Box("PaimonEmblem", opening, new Vector2(1 - x, 1), Vector2.one * .5f,
                new Vector2(145, 152), new Vector2(-sign * 75.5f, -32));
            if (right) emblem.localScale = new Vector3(-1, 1, 1);
            Picture(emblem, EmblemFile);
        });
        RectTransform Face(string name, int size) => Box(name, lane.Content, edge, Vector2.one * .5f,
            Vector2.one * size, new Vector2(sign * 73, 0));
        Picture(Face("OuterRing", 108), LayerFiles[0]); Picture(Face("InnerRing", 94), LayerFiles[1]);
        var mask = Face("PortraitMask", 80); Picture(mask, LayerFiles[2]);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        lane.Portrait = Picture(Box("Portrait", mask, Vector2.one * .5f, Vector2.one * .5f, Vector2.one * 80, Vector2.zero));
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
            !isActiveAndEnabled || player.ActionStyle == null || ally == null ||
            (fact.FollowsTargetSelection && selectionInput != null && selectionInput.isActiveAndEnabled))
        { done(null); return; }
        bool right = cue.Result.TargetSide == BattleSide.Enemy;
        var lane = right ? enemy : ally;
        lane.Queue.Enqueue(new Item { Id = id, Fact = fact, Done = done, Style = player.ActionStyle });
        if (lane.Current == null) Begin(lane, right);
    }
    private void Begin(Lane lane, bool right)
    {
        if (selectionLane == lane) ClearSelection();
        var item = lane.Current = lane.Queue.Dequeue();
        SetContent(lane, item.Style, item.Fact.Name, item.Fact.SubjectId, right, item.Fact.IsStatus);
        lane.Time = 0;
        SetReveal(lane, lane.Time, item.Style.Reveal);
        lane.Opacity.alpha = 1;
    }

    private static void SetContent(Lane lane, BattleActionStyle style, string name, int actorId, bool enemyActor, bool isStatus = false)
    {
        string key = isStatus ? "ui_action_prompt_status" : enemyActor ? "ui_action_prompt_enemy" : "ui_action_prompt_ally";
        lane.Header.text = DataManager.Instance?.GetLangText(key) ?? string.Empty;
        lane.Detail.text = name;
        float Measure(TMP_Text label, string text) => label.GetPreferredValues(text, Mathf.Infinity, Mathf.Infinity).x;
        float content = Mathf.Max(Measure(lane.Detail, lane.Detail.text), Measure(lane.Header, lane.Header.text));
        float width = TextWidth(content, Measure(lane.Detail, "行"), style.MinTextCharacters);
        lane.Header.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        lane.Detail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        lane.Width = TextInset + width + EndPadding;
        lane.Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lane.Width);
        ((RectTransform)lane.Portrait.transform).sizeDelta = Vector2.one * (enemyActor ? 72 : 80);
        string portrait = "art_assets/" + (enemyActor ? $"Enemy_Icon/{actorId}_01.png" : CharacterCardArtManifest.PortraitFile(actorId));
        if (lane.Portrait.relativePath != portrait) lane.Portrait.SetResolvedPath(portrait);
    }
    private static float TextWidth(float measuredWidth, float fullWidthGlyph, int minimumCharacters)
        => Mathf.Ceil(Mathf.Max(measuredWidth, Mathf.Max(0, minimumCharacters) * fullWidthGlyph)) + 4;

    private static void SetReveal(Lane lane, float elapsed, float seconds)
    {
        float reveal = seconds <= 0 ? 1 : Mathf.Clamp01(elapsed / seconds);
        lane.Clip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lane.Width * Mathf.SmoothStep(0, 1, reveal));
    }

    private void LateUpdate()
    {
        var style = player != null && player.isActiveAndEnabled ? player.ActionStyle : null;
        if (ally == null || style == null) { ClearSelection(); return; }
        if (selectionInput == null || !selectionInput.TryGetSkillSelection(out var caster, out int button, out var targetSide))
        {
            if (selectionLane != null)
            {
                selectionFadeTime += BattlePause.DeltaTime;
                selectionLane.Opacity.alpha = FadeAlpha(selectionFadeTime, style.Fade);
                if (selectionLane.Opacity.alpha <= 0) ClearSelection();
            }
            return;
        }

        var lane = targetSide == BattleSide.Enemy ? enemy : ally;
        var skill = caster.GetBoundSkillData(button);
        if (skill == null || lane.Current != null) { ClearSelection(); return; }
        if (selectionLane != lane || selectionActor != caster.Entity || selectionSkillId != skill.SkillID2 || selectionFadeTime > 0)
        {
            string name = caster.GetUiSkillName(button, currentSkill: skill);
            ClearSelection();
            if (string.IsNullOrWhiteSpace(name)) return;
            SetContent(lane, style, name, caster.Entity.EntityID, enemyActor: false);
            selectionLane = lane;
            selectionActor = caster.Entity;
            selectionSkillId = skill.SkillID2;
            SetReveal(lane, 0, style.Reveal);
            lane.Opacity.alpha = 1;
        }

        if (selectionTime < style.Reveal)
        {
            selectionTime = Mathf.Min(selectionTime + BattlePause.DeltaTime, style.Reveal);
            SetReveal(lane, selectionTime, style.Reveal);
        }
    }

    private void ClearSelection()
    {
        if (selectionLane == null) return;
        selectionLane.Opacity.alpha = 0;
        selectionLane = null;
        selectionActor = null;
        selectionSkillId = null;
        selectionTime = selectionFadeTime = 0;
    }

    private static float FadeAlpha(float elapsed, float seconds) => seconds <= 0 ? 0 : 1 - Mathf.Clamp01(elapsed / seconds);

    private void Update() { if (ally != null) { Tick(ally, false); Tick(enemy, true); } }
    private void Tick(Lane lane, bool right)
    {
        if (lane.Current == null) { if (lane.Queue.Count != 0) Begin(lane, right); else return; }
        lane.Time += BattlePause.DeltaTime;
        var style = lane.Current.Style;
        SetReveal(lane, lane.Time, style.Reveal);
        float endHold = style.Reveal + style.Hold;
        if (lane.Time < endHold) return;
        lane.Opacity.alpha = FadeAlpha(lane.Time - endHold, style.Fade);
        if (lane.Opacity.alpha > 0) return;
        var done = lane.Current.Done; lane.Current = null; done(null);
    }
    public override void Cancel(long id)
    {
        if (id < 0) ClearSelection();
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
