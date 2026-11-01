using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Action Buttons")]
public sealed class BattleUiActionButtons : MonoBehaviour
{
    public BattlefieldActionStateView view;
    public BattleInputController input;
    public BattlefieldSwitchController switcher;
    public UIBattleController legacy;
    public Canvas canvas;
    public RectTransform switchRoot, cancelRoot, endTurnRoot;
    public RectTransform switchDisc, cancelDisc, endTurnDisc;
    public UIAdaptiveFrame projectFrame;
    public TMP_FontAsset hintFont;
    [Min(.05f)] public float holdSeconds = .45f;

    public readonly struct SkillHold
    {
        public readonly CharacterBattleController Actor;
        public readonly string SkillId;
        public readonly int SkillType;
        public readonly RectTransform Anchor;
        public SkillHold(CharacterBattleController actor, string skillId, int skillType, RectTransform anchor)
        {
            Actor = actor;
            SkillId = skillId;
            SkillType = skillType;
            Anchor = anchor;
        }
    }
    public event Action<SkillHold> SkillHoldStarted, SkillHoldEnded;
    private readonly List<GameObject> areas = new List<GameObject>();
    private RectTransform hint, hintAnchor;
    private CanvasGroup hintGroup;
    private TMP_Text hintLabel;
    private readonly Vector3[] corners = new Vector3[4];
    private bool started;
    private BattleInputController InputOwner => input != null ? input : legacy != null ? legacy.input : null;
    private bool Switching => switcher != null ? switcher.IsSwitching : legacy != null && legacy.IsSwitching;

    private void Start() { started = true; Build(); }
    private void OnEnable() { if (started) Build(); }
    private void Build()
    {
        if (view != null)
        {
            foreach (var action in view.actions)
            {
                if (action?.Icon == null) continue;
                var root = action.Root != null ? action.Root.transform : action.Icon.transform.parent;
                int slot = action.slot, skill = action.skillType;
                AddSkill((RectTransform)root, slot, skill, () => {
                    if (slot > 0) InputOwner?.UIEnterBurstBySlot(slot - 1);
                    else InputOwner?.UIEnterSelectSkill(skill);
                });
            }
        }
        if (legacy != null)
        {
            void BindLegacy(Button button, int slot, int skill)
            {
                if (button != null) AddSkill((RectTransform)button.transform, slot, skill, () => Click(button));
            }
            BindLegacy(legacy.btnAttack, 0, 0);
            BindLegacy(legacy.btnHeavy, 0, 1);
            BindLegacy(legacy.btnSkill, 0, 2);
            BindLegacy(legacy.btnBurst, 0, 3);
            for (int i = 0; i < legacy.allySlots.Length; i++)
            {
                var slot = legacy.allySlots[i];
                if (slot != null) BindLegacy(slot.burstButton != null ? slot.burstButton : slot.energyBall?.GetComponent<Button>(), i + 1, 3);
            }
        }
        AddFunction("F", switchRoot, switchDisc, Switch, () => Switching ? "ui_battle_hint_confirm_switch" : "ui_battle_hint_switch_character");
        AddFunction("X", cancelRoot, cancelDisc, Cancel, () => "ui_battle_hint_cancel_selection");
        AddFunction("SPACE", endTurnRoot, endTurnDisc, Space, () => Switching ? "ui_battle_hint_confirm_switch" :
            InputOwner != null && InputOwner.IsSelectionVisible ? "ui_battle_hint_confirm_target" : "ui_battle_hint_end_turn");
    }
    private static void Click(Button button)
    {
        if (button != null && button.isActiveAndEnabled && button.IsInteractable()) button.onClick.Invoke();
    }
    private void Switch()
    {
        if (switcher != null && switcher.isActiveAndEnabled) switcher.UIBeginSwitch();
        else if (legacy != null) Click(legacy.btnSwitch);
    }
    private void Cancel()
    {
        if (Switching && switcher != null) switcher.UICancelSwitch();
        else if (Switching && legacy != null) legacy.UICancelSwitch();
        else if (InputOwner != null && InputOwner.isActiveAndEnabled) InputOwner.UICancelSelection();
    }
    private void Space()
    {
        if (Switching) Switch();
        else if (InputOwner != null && InputOwner.IsSelecting) InputOwner.UIConfirmSelection();
        else InputOwner?.UIEndTurn();
    }
    private void AddSkill(RectTransform anchor, int slot, int skill, Action click)
    {
        SkillHold? current = null;
        AddArea(anchor, click, () => {
            var battle = BattleManager.Instance;
            var actor = slot > 0 ? battle?.GetAllyBySlot(slot - 1) : battle?.GetActiveAlly();
            string skillId = actor?.GetBoundSkillID(skill);
            if (string.IsNullOrEmpty(skillId)) return;
            current = new SkillHold(actor, skillId, skill, anchor);
            SkillHoldStarted?.Invoke(current.Value);
        }, () => {
            var previous = current;
            current = null;
            if (previous.HasValue) SkillHoldEnded?.Invoke(previous.Value);
        });
    }
    private void AddFunction(string shortcut, RectTransform root, RectTransform disc, Action click, Func<string> key)
    {
        if (root == null) return;
        var anchor = disc != null ? disc : root;
        void Trace(string gesture, string hintKey)
        {
            var battle = BattleManager.Instance;
            string command = DataManager.Instance?.GetLangText(hintKey) ?? hintKey;
            string phase = battle == null ? "未绑定战斗" : $"{battle.CurrentPhase}，等待我方操作={battle.IsAwaitingAllyInput}，结算中={BattleExecution.IsBusy}";
            LogManager.Log(LogCategory.UI, $"{gesture} {shortcut}（{command}），{phase}");
        }
        AddArea(root, () => {
            Trace("短按", key());
            click();
        }, () => {
            string hintKey = key();
            Trace("长按", hintKey);
            ShowHint(anchor, hintKey);
        }, () => {
            if (hintAnchor == anchor) HideHint();
        });
    }
    private void AddArea(RectTransform anchor, Action click, Action hold, Action release)
    {
        var go = new GameObject("ButtonPressTarget", typeof(RectTransform), typeof(Image), typeof(UIBattlePressArea));
        var rect = (RectTransform)go.transform;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(anchor);
        rect.SetParent(anchor, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.Min(Vector2.zero, (Vector2)bounds.min - anchor.rect.min);
        rect.offsetMax = Vector2.Max(Vector2.zero, (Vector2)bounds.max - anchor.rect.max);
        var graphic = go.GetComponent<Image>(); graphic.color = Color.clear; graphic.raycastTarget = true;
        go.GetComponent<UIBattlePressArea>().Bind(holdSeconds, click, hold, release); areas.Add(go);
    }
    private void ShowHint(RectTransform anchor, string key)
    {
        if (projectFrame == null || canvas == null || hintFont == null || DataManager.Instance == null) return;
        if (hint == null)
        {
            hint = (RectTransform)new GameObject("BattleButtonHint", typeof(RectTransform), typeof(CanvasGroup)).transform;
            hint.SetParent(canvas.transform, false); hintGroup = hint.GetComponent<CanvasGroup>();
            hintGroup.interactable = hintGroup.blocksRaycasts = false;
            var frame = Instantiate(projectFrame, hint, false);
            // The single-line hint is 58 units tall. Keep the project's 64px corners intact.
            frame.pixelsPerUnitMultiplier = Mathf.Max(3f, frame.pixelsPerUnitMultiplier);
            Stretch((RectTransform)frame.transform);
            hintLabel = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            hintLabel.rectTransform.SetParent(hint, false); Stretch(hintLabel.rectTransform);
            hintLabel.font = hintFont; hintLabel.fontSize = 22; hintLabel.color = new Color32(65, 54, 43, 255);
            hintLabel.alignment = TextAlignmentOptions.MidlineGeoAligned; hintLabel.enableWordWrapping = false;
            hintLabel.richText = false; hintLabel.raycastTarget = false;
        }
        hintAnchor = anchor; hintLabel.text = DataManager.Instance.GetLangText(key);
        hint.sizeDelta = new Vector2(Mathf.Ceil(hintLabel.GetPreferredValues(hintLabel.text).x) + 44, 58);
        hint.SetAsLastSibling(); PlaceHint(); hintGroup.alpha = 1;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private void LateUpdate() { if (hintAnchor != null) PlaceHint(); }
    private void PlaceHint()
    {
        var parent = (RectTransform)canvas.transform;
        Rect face = CanvasRect(hintAnchor, parent);
        Vector2 half = hint.sizeDelta * .5f;
        float right = face.xMax + 12 + half.x, left = face.xMin - 12 - half.x;
        float above = face.yMax + 12 + half.y;
        Vector2 best = face.center;
        float leastOverlap = float.PositiveInfinity;
        void Candidate(float x, float y)
        {
            if (leastOverlap == 0) return;
            Vector2 center = new Vector2(Mathf.Clamp(x, parent.rect.xMin + half.x, parent.rect.xMax - half.x),
                Mathf.Clamp(y, parent.rect.yMin + half.y, parent.rect.yMax - half.y));
            Rect box = new Rect(center - half, hint.sizeDelta);
            float overlap = 0;
            foreach (var area in areas)
            {
                if (!area.activeInHierarchy) continue;
                Rect button = CanvasRect((RectTransform)area.transform, parent);
                float w = Mathf.Min(button.xMax, box.xMax) - Mathf.Max(button.xMin, box.xMin);
                float h = Mathf.Min(button.yMax, box.yMax) - Mathf.Max(button.yMin, box.yMin);
                if (w > 0 && h > 0) overlap += w * h;
            }
            if (overlap >= leastOverlap) return;
            best = center;
            leastOverlap = overlap;
        }
        Candidate(right, face.center.y);
        Candidate(left, face.center.y);
        Candidate(face.center.x, above);
        Candidate(left, above);
        Candidate(right, above);
        hint.localPosition = best;
    }
    private Rect CanvasRect(RectTransform rect, RectTransform parent)
    {
        rect.GetWorldCorners(corners);
        var low = parent.InverseTransformPoint(corners[0]);
        var high = parent.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
    }
    private void HideHint() { hintAnchor = null; if (hintGroup != null) hintGroup.alpha = 0; }
    private void OnDisable()
    {
        foreach (var area in areas) if (area != null) { area.SetActive(false); Destroy(area); }
        areas.Clear(); HideHint();
    }
    private void OnDestroy() { if (hint != null) Destroy(hint.gameObject); }
}
