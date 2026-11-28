using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Read-only character details. Scenes supply their own battle, canvas, card service and portrait slots.</summary>
[DisallowMultipleComponent]
public sealed partial class BattleUiCharacterDetails : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private BattleManager battle;
    [SerializeField] private BattleInputController input;
    [SerializeField] private BattlefieldSwitchController switcher;
    [SerializeField] private UICardFactory cardFactory;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private RectTransform[] portraits = Array.Empty<RectTransform>();
    [SerializeField] private Vector2 panelFraction = new Vector2(.8f, .8f);
    [SerializeField] private bool english;

    private sealed class PortraitClick
    {
        internal UIBattlePressArea Area;
        internal Image Graphic;
        internal bool OriginalRaycast;
    }
    private readonly List<PortraitClick> portraitButtons = new List<PortraitClick>();
    private RectTransform overlay, panel, body, quickSwitch;
    private Button closeButton;
    private TMP_Text identityName, identitySummary;
    private RosterCardCarousel carousel;
    private BattleInputController lockedInput;
    private GameObject previousSelection;
    private DataManager data;
    private RosterTextCatalog descriptions;
    private CharacterBattleController actor;
    private Func<CharacterBattleController, IReadOnlyList<RoguelikeArtifactInstanceData>> equippedArtifacts;
    private int slot, tab, skillTab, entry;
    private Coroutine reveal;
    public void SetEnglish(bool value)
    {
        if (english == value) return;
        Close(); english = value;
    }

    /// <summary>Refresh only the live battle binding; keep the scene-authored canvas, font and layout.</summary>
    public void BindBattle(BattleManager manager, BattleInputController battleInput,
        Func<CharacterBattleController, IReadOnlyList<RoguelikeArtifactInstanceData>> artifacts = null)
        => Bind(manager, battleInput, canvas, cardFactory, framePrefab, font, portraits, switcher, artifacts);

    /// <summary>Portraits are ordered by zero-based allied slot. A null entry leaves that slot without a click target.</summary>
    public void Bind(BattleManager manager, BattleInputController battleInput, Canvas targetCanvas, UICardFactory cards,
        UIAdaptiveFrame frame, TMP_FontAsset typeface, RectTransform[] avatarSlots, BattlefieldSwitchController switching = null,
        Func<CharacterBattleController, IReadOnlyList<RoguelikeArtifactInstanceData>> artifacts = null)
    {
        Close();
        RemovePortraitButtons();
        battle = manager;
        input = battleInput;
        canvas = targetCanvas;
        cardFactory = cards;
        framePrefab = frame;
        font = typeface;
        portraits = avatarSlots ?? Array.Empty<RectTransform>();
        switcher = switching;
        equippedArtifacts = artifacts;
        if (isActiveAndEnabled) AddPortraitButtons();
    }

    private void OnEnable() => AddPortraitButtons();

    private void OnDisable()
    {
        Close();
        RemovePortraitButtons();
    }

    private void OnDestroy() => Close();

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) Close();
    }

    private void AddPortraitButtons()
    {
        if (portraitButtons.Count != 0 || portraits == null) return;
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i] == null) continue;
            int index = i;
            var portrait = portraits[i];
            // Raycast the visible portrait itself. Its owning avatar group receives the click
            // through the normal UI hierarchy, including when a frame graphic is on top.
            var graphic = portrait.GetComponentsInChildren<StreamingSpriteReference>(true)
                .Select(reference => reference.GetComponent<Image>()).FirstOrDefault(image => image != null)
                ?? portrait.GetComponent<Image>();
            if (graphic == null) continue;
            var area = portrait.parent.gameObject.AddComponent<UIBattlePressArea>();
            area.BindClick(() => TryOpen(index));
            portraitButtons.Add(new PortraitClick { Area = area, Graphic = graphic, OriginalRaycast = graphic.raycastTarget });
            graphic.raycastTarget = true;
        }
    }

    private void RemovePortraitButtons()
    {
        foreach (var binding in portraitButtons)
        {
            if (binding.Graphic != null) binding.Graphic.raycastTarget = binding.OriginalRaycast;
            if (binding.Area == null) continue;
            binding.Area.BindClick(null);
            binding.Area.enabled = false;
            Destroy(binding.Area);
        }
        portraitButtons.Clear();
    }

    private bool CanInspect => isActiveAndEnabled && battle != null && battle.IsAwaitingAllyInput && !battle.IsResolving
        && input != null && input.isActiveAndEnabled && canvas != null && canvas.isActiveAndEnabled
        && (switcher == null || !switcher.IsSwitching);

    private CharacterBattleController ActorAt(int index)
    {
        var member = battle.GetAllyBySlot(index);
        return member != null && member.Entity != null && !member.Entity.HasWithdrawn ? member : null;
    }

    public bool TryOpen(int allySlot)
    {
        if (overlay != null || !CanInspect || input.IsUiInputBlocked || cardFactory == null || !cardFactory.isActiveAndEnabled
            || framePrefab == null || allySlot < 0 || allySlot >= battle.AllySlotCount) return false;
        var member = ActorAt(allySlot);
        var manager = DataManager.Instance;
        if (member == null || manager == null || !manager.IsLoaded) return false;

        data = manager;
        try { descriptions = data.GetRosterText(); }
        catch (Exception error)
        {
            Debug.LogWarning("角色详情文案或套装表读取失败，沿用已加载的配表：" + error.Message, this);
            descriptions = new RosterTextCatalog(data);
        }

        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        try
        {
            BuildWindow();
            var visibility = overlay.gameObject.AddComponent<CanvasGroup>();
            visibility.alpha = 0;
            tab = skillTab = entry = 0;
            SelectMember(allySlot);
            lockedInput = input;
            lockedInput.SetUiInputBlocked(this, true);
            closeButton.Select();
            reveal = StartCoroutine(RevealWindow(overlay, visibility));
            return true;
        }
        catch (Exception error)
        {
            Close();
            Debug.LogException(error, this);
            return false;
        }
    }

    // Keep the battle keyboard locked through its Update on the frame that Escape closes the modal.
    private void LateUpdate()
    {
        if (overlay == null)
        {
            if (lockedInput != null) Close();
            return;
        }
        if (!CanInspect || ActorAt(slot) != actor || Input.GetKeyDown(KeyCode.Escape) || ProjectKeyBindings.Down(BattleShortcut.Cancel)) Close();
    }

    public void Close()
    {
        if (reveal != null) StopCoroutine(reveal);
        reveal = null;
        if (lockedInput != null) lockedInput.SetUiInputBlocked(this, false);
        lockedInput = null;
        if (overlay != null)
        {
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
        }
        overlay = null;
        actor = null;
        if (previousSelection != null && previousSelection.activeInHierarchy && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }

    private IEnumerator RevealWindow(RectTransform window, CanvasGroup visibility)
    {
        var frame = window.GetComponentInChildren<UIAdaptiveFrame>();
        var cards = window.GetComponentsInChildren<UICardView>();
        var art = window.GetComponentsInChildren<StreamingSpriteReference>();
        float deadline = Time.realtimeSinceStartup + 10;
        while (window != null && window == overlay)
        {
            string error = frame != null ? frame.Error : null;
            if (string.IsNullOrEmpty(error))
                error = cards.FirstOrDefault(card => card != null && !string.IsNullOrEmpty(card.Error))?.Error;
            if (!string.IsNullOrEmpty(error))
            {
                FailOpening(error);
                yield break;
            }
            bool loading = (frame != null && !frame.Ready) || cards.Any(card => card != null && !card.Ready)
                || art.Any(asset => asset != null && asset.IsLoading);
            if (!loading) break;
            if (Time.realtimeSinceStartup >= deadline)
            {
                FailOpening("边框、卡牌或图标加载超时。");
                yield break;
            }
            yield return null;
        }
        if (window == null || window != overlay || !PrepareWindowLayout()) yield break;
        yield return null;
        if (window == null || window != overlay) yield break;
        if (visibility != null) visibility.alpha = 1;
        reveal = null;
    }

    private bool PrepareWindowLayout()
    {
        try { Canvas.ForceUpdateCanvases(); return true; }
        catch (Exception error) { FailOpening(error.Message); return false; }
    }

    private void FailOpening(string error)
    {
        reveal = null;
        Close();
        Debug.LogWarning("角色详情打开失败，已关闭详情并释放输入锁：" + error, this);
    }

    private void SelectMember(int index)
    {
        var member = ActorAt(index);
        if (member == null) return;
        actor = member;
        slot = index;
        entry = 0;
        carousel.Select(member.CharacterID, index);
        identityName.text = CharacterName(member.CharacterID);
        string type = WeaponLabel(member.Entity.WeaponType);
        identitySummary.text = $"{ElementLabel(member.Element)}{type} · Lv. {member.Level}\n" +
            (member.IsActive ? Ui("ui_battle_detail_on_field") : Ui("ui_battle_detail_off_field"));
        BuildQuickSwitch();
        RenderTab();
    }
}
