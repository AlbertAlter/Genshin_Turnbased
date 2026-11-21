using System;
using UnityEngine;

/// <summary>把当前长按的真实技能绑定到公共详情；不施放技能，不改选段或目标。</summary>
[DisallowMultipleComponent]
public sealed class BattleUiSkillInfo : MonoBehaviour
{
    public BattleUiActionButtons buttons;
    public BattleInputController input;
    public BattleManager battle;
    public Canvas canvas;
    public BattleSkillInfoView viewPrefab;
    [Min(0f)] public float buttonGap = 20f;
    [Min(0f)] public float screenMargin = 16f;
    public Color inactiveDescriptionColor = new Color32(128, 128, 128, 255);

    private RectTransform overlay;
    private BattleSkillInfoView view, statusView;
    private BattleInfoPopupStyle style;
    private BattleUiActionButtons.SkillHold showing;
    private BattleInputController lockedInput;
    private BattleUiActionButtons subscribedButtons;
    private bool open;
    private bool statusOpen;
    private Vector2 layoutSize;
    private readonly Vector3[] corners = new Vector3[4];

    private void OnEnable()
    {
        SubscribeButtons();
    }

    private void OnDisable()
    {
        UnsubscribeButtons();
        Close();
    }

    private void OnDestroy()
    {
        UnsubscribeButtons();
        ReleaseViews();
    }

    /// <summary>装配者显式绑定；支持先启用组件再注入引用，不依赖OnEnable的调用顺序。</summary>
    public void Bind(BattleUiActionButtons actionButtons, BattleInputController battleInput,
        BattleManager battleManager, Canvas targetCanvas, BattleSkillInfoView prefab)
    {
        if (canvas != targetCanvas || viewPrefab != prefab) ReleaseViews();
        else Close();
        UnsubscribeButtons();
        buttons = actionButtons;
        input = battleInput;
        battle = battleManager;
        canvas = targetCanvas;
        viewPrefab = prefab;
        SubscribeButtons();
    }

    private void SubscribeButtons()
    {
        if (subscribedButtons == buttons) return;
        UnsubscribeButtons();
        if (!isActiveAndEnabled || buttons == null) return;
        subscribedButtons = buttons;
        subscribedButtons.SkillHoldStarted += Open;
    }

    private void UnsubscribeButtons()
    {
        if (subscribedButtons != null) subscribedButtons.SkillHoldStarted -= Open;
        subscribedButtons = null;
    }

    private void ReleaseViews()
    {
        Close();
        if (view != null) view.DescriptionLinkClicked -= OpenDescription;
        if (statusView != null) statusView.DescriptionLinkClicked -= OpenDescription;
        if (overlay != null) Destroy(overlay.gameObject);
        overlay = null;
        view = statusView = null;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) Close(); }

    // LateUpdate releases the input lock after all keyboard readers consumed this frame.
    private void LateUpdate()
    {
        if (!open) return;
        if (canvas == null || !canvas.isActiveAndEnabled || input == null || !input.isActiveAndEnabled || buttons == null || !buttons.isActiveAndEnabled
            || battle == null || battle.IsBattleOver || BattleExecution.IsBusy
            || !battle.IsAwaitingAllyInput || showing.Anchor == null || view == null || showing.Actor == null || showing.Actor.Entity?.IsAlive != true
            || showing.Actor.GetBoundSkillID(showing.SkillType) != showing.SkillId
            || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X))
        { Close(); return; }
        if (layoutSize != ((RectTransform)canvas.transform).rect.size) LayoutPanels(false, false);
    }

    private void Open(BattleUiActionButtons.SkillHold request)
    {
        if (canvas == null || viewPrefab == null || input == null || !input.isActiveAndEnabled
            || battle == null || battle.IsBattleOver || !battle.IsAwaitingAllyInput || BattleExecution.IsBusy
            || request.Anchor == null || request.Actor == null || DataManager.Instance == null) return;
        var skill = request.Actor.GetBoundSkillData(request.SkillType);
        if (skill == null || skill.SkillID2 != request.SkillId) return;
        // Resolve all text before opening so malformed data cannot leave an input lock behind.
        var dataManager = DataManager.Instance;
        string name = dataManager.GetLangRichText(request.Actor.GetUiSkillName(request.SkillType, currentSkill: skill));
        string body = Describe("skill_" + skill.SkillID + "_desc", skill.Description, request.Actor);
        style = BattleInfoPopupStyle.Load();
        EnsureView();
        showing = request;
        overlay.SetAsLastSibling();
        overlay.gameObject.SetActive(true);
        view.Show(name, request.Actor.GetEffectiveSkillLevel(request.SkillType),
            request.Actor.GetBoundSkillAPCost(request.SkillType), skill.Cooldown, body);
        statusOpen = false;
        if (statusView != null) statusView.gameObject.SetActive(false);
        LayoutPanels(true, false);
        lockedInput = input;
        lockedInput.SetUiInputBlocked(this, true);
        open = true;
    }

    public void Close()
    {
        if (lockedInput != null) lockedInput.SetUiInputBlocked(this, false);
        lockedInput = null;
        open = false;
        statusOpen = false;
        showing = default;
        if (overlay != null) overlay.gameObject.SetActive(false);
    }

    private void EnsureView()
    {
        if (overlay != null) return;
        overlay = BattleSkillInfoView.CreateDismissOverlay(canvas, "SkillInfoOverlay", Close);
        view = Instantiate(viewPrefab, overlay, false);
        view.DescriptionLinkClicked += OpenDescription;
    }

    private void OpenDescription(string key, string name)
    {
        if (!open || DataManager.Instance == null || !DataManager.Instance.LangDict.ContainsKey(key))
        {
            if (open) Debug.LogWarning("[UI] 说明链接找不到 Lang key：" + key, this);
            return;
        }
        // Resolve before replacing the current child panel; a malformed template keeps the old view intact.
        string body, titleKey = key.EndsWith("_desc", StringComparison.Ordinal)
            ? key.Substring(0, key.Length - 5) + "_title" : null;
        bool richTitle = titleKey != null && DataManager.Instance.LangDict.ContainsKey(titleKey);
        try
        {
            body = Describe(key, null, showing.Actor);
            if (richTitle) name = DataManager.Instance.GetLangRichText(titleKey);
        }
        catch (FormatException error) { Debug.LogError(error.Message, this); return; }
        if (statusView == null)
        {
            statusView = Instantiate(viewPrefab, overlay, false);
            statusView.name = "StatusInfo";
            statusView.DescriptionLinkClicked += OpenDescription;
        }
        statusView.gameObject.SetActive(true);
        statusView.ShowDescription(name, body, richTitle);
        statusOpen = true;
        LayoutPanels(false, true);
    }

    private void LayoutPanels(bool resetSkill, bool resetStatus)
    {
        var canvasRect = (RectTransform)canvas.transform;
        Rect bounds = canvasRect.rect;
        layoutSize = bounds.size;
        float margin = Mathf.Min(screenMargin, Mathf.Min(bounds.width, bounds.height) * .1f);
        float availableWidth = Mathf.Max(1, bounds.width - 2 * margin);
        float availableHeight = Mathf.Max(1, bounds.height - 2 * margin);
        BattleInfoPopupLayout.FitWidths(style.SkillWidth, style.StatusWidth, availableWidth,
            style.PanelGap, statusOpen, out float skillWidth, out float statusWidth);
        view.Reflow(skillWidth, Mathf.Min(style.SkillMaxHeight, availableHeight), resetSkill);
        if (statusOpen) statusView.Reflow(statusWidth, Mathf.Min(style.StatusMaxHeight, availableHeight), resetStatus);
        showing.Anchor.GetWorldCorners(corners);
        Vector2 lower = canvasRect.InverseTransformPoint(corners[0]);
        Vector2 upper = canvasRect.InverseTransformPoint(corners[2]);
        var panel = (RectTransform)view.transform;
        var child = statusOpen ? (RectTransform)statusView.transform : null;
        bool rightSide = showing.SkillType == 3;
        Vector2 size = panel.rect.size;
        float x = rightSide ? upper.x + buttonGap : lower.x - buttonGap - size.x;
        float top = (lower.y + upper.y + size.y) * .5f;
        var pair = BattleInfoPopupLayout.Place(bounds.xMin + margin, bounds.xMax - margin,
            bounds.yMin + margin, bounds.yMax - margin, x, top, size.x, size.y,
            statusWidth, child != null ? child.rect.height : 0, style.PanelGap, statusOpen, rightSide);
        view.PlaceInCanvas(canvasRect, pair.SkillLeft, pair.Top);
        if (child != null) statusView.PlaceInCanvas(canvasRect, pair.StatusLeft, pair.Top);
    }

    private string Describe(string key, string fallback, CharacterBattleController actor)
        => DataManager.Instance.GetInfoDescriptionText(key, fallback, actor.Entity.EntityID,
            addition => IsAdditionActive(addition, actor), inactiveColor: "#" + ColorUtility.ToHtmlStringRGB(inactiveDescriptionColor));

    private bool IsAdditionActive(LangInfoAddition addition, CharacterBattleController actor)
    {
        var entity = actor.Entity;
        if (entity == null || entity.EntityID != addition.CharacterID) return false;
        if (addition.UnlockType == "Constellation") return entity.ConstellationLevel >= addition.UnlockIndex;
        if (addition.UnlockIndex > 0)
        {
            if (!DataManager.Instance.TalentDict.TryGetValue(entity.EntityID, out var talents)) return false;
            var talent = talents.Find(t => t.TalentIndex == addition.UnlockIndex);
            if (talent == null || !CharacterAscensionRules.IsTalentUnlocked(talent.UnlockAfter, entity.Level, actor.Ascension)) return false;
        }
        switch (addition.UnlockType)
        {
            case "Talent": return true;
            case "Hexerei": return battle.TeamBonuses?.HasBonus("Hexerei") == true;
            case "MoonsignGleam": return battle.TeamBonuses?.HasBonus("MoonsignGleam") == true;
            case "MoonsignFull": return battle.TeamBonuses?.HasBonus("MoonsignAscendant") == true;
            default: return false;
        }
    }
}
