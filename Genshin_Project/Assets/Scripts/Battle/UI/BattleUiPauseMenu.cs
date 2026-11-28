using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>按需挂载的战斗暂停菜单。场景绑定入口、布局与公共设置Prefab，业务退出由宿主提供。</summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class BattleUiPauseMenu : MonoBehaviour
{
    public BattleManager battle;
    public BattleInputController input;
    public BattlefieldSwitchController switcher;
    public RectTransform mount;
    public TMP_FontAsset font;
    public UIAdaptiveFrame framePrefab;
    public ProjectSettingsView settingsPrefab;
    public Vector2 buttonInset = new Vector2(30, 25);
    private Button pauseButton;
    private RectTransform buttonRoot, overlay;
    private RoguelikeCampaignScreen view;
    private ProjectSettingsOverlay settings;
    private Action leave;
    private Func<bool> available;
    private bool held;
    public bool IsOpen => held;
    private string L(string key) => DataManager.Instance.GetLangText(key, ProjectUserSettings.English());

    public void BindCampaign(Action returnToMenu, Func<bool> canOpen)
    { leave = returnToMenu; available = canOpen; }

    private IEnumerator PrepareButton()
    {
        if (buttonRoot != null) { buttonRoot.gameObject.SetActive(true); yield break; }
        if (mount == null || font == null || input == null || battle == null || settingsPrefab == null)
        { Debug.LogError("暂停菜单缺少场景显式绑定。", this); enabled = false; yield break; }
        // 独立打开战斗场景时，运行器在自己的Start加载DataManager；菜单的早期输入顺序不抢先取Lang。
        while (DataManager.Instance == null || !DataManager.Instance.IsLoaded) yield return null;
        buttonRoot = RoguelikeCampaignScreen.Rect("Battle pause button", mount);
        var controls = new RoguelikeCampaignScreen(buttonRoot, font, Dispatch, framePrefab);
        RoguelikeCampaignScreen.Stretch(buttonRoot);
        pauseButton = controls.Button(controls.Root, L("btn_battle_pause"), Vector2.one,
            -buttonInset.x, -buttonInset.y, 84, 56, Open);
        pauseButton.gameObject.AddComponent<UIButtonClickFeedback>().Bind(pauseButton.GetComponent<RectTransform>());
    }
    private void Update()
    {
        if (pauseButton != null) pauseButton.interactable = held || CanOpen();
        bool escape = Input.GetKeyDown(KeyCode.Escape);
        if (ProjectKeyBindings.InputSuppressed || (!escape && !ProjectKeyBindings.Down(BattleShortcut.Pause)) || settings?.IsOpen == true) return;
        if (held)
        {
            if (escape && !UIPageNavigation.TryConsumeEscape()) return;
            if (view?.TryNavigateBack() != true) Resume();
        }
        else if (CanOpen() && input.isActiveAndEnabled && !input.IsSelecting && switcher?.IsSwitching != true
            && (!escape || UIPageNavigation.TryConsumeEscape())) Open();
    }
    private bool CanOpen() => isActiveAndEnabled && battle != null && battle.IsBattleRunning && !battle.IsBattleOver
        && DataManager.Instance?.IsLoaded == true && input != null && !input.IsUiInputBlocked && available?.Invoke() != false;

    public void Open()
    {
        if (held) { Resume(); return; }
        if (!CanOpen()) return;
        held = true; input.SetUiInputBlocked(this, true); BattlePause.SetPaused(this, true);
        try { Draw(); } catch { Resume(); throw; }
    }
    private void Draw()
    {
        if (overlay != null) { overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); }
        overlay = RoguelikeCampaignScreen.Rect("Battle pause menu", mount); RoguelikeCampaignScreen.Stretch(overlay);
        var canvas = overlay.gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true;
        var parent = mount.GetComponentInParent<Canvas>();
        canvas.sortingLayerID = parent.sortingLayerID; canvas.sortingOrder = parent.sortingOrder + 400;
        overlay.gameObject.AddComponent<GraphicRaycaster>();
        var shade = overlay.gameObject.AddComponent<Image>(); shade.color = new Color32(26,21,17,185); shade.raycastTarget = true;
        var outside = overlay.gameObject.AddComponent<Button>(); outside.targetGraphic = shade;
        outside.navigation = new Navigation { mode = Navigation.Mode.None }; outside.onClick.AddListener(Resume);
        view = new RoguelikeCampaignScreen(overlay, font, Dispatch, framePrefab);
        var panel = RoguelikeCampaignScreen.Rect("Pause functions", view.Root);
        RoguelikeCampaignScreen.Place(panel, Vector2.one * .5f, 0, 0, 560, 440);
        var fill = panel.gameObject.AddComponent<Image>(); fill.color = RoguelikeCampaignScreen.Deep; fill.raycastTarget = true;
        var inside = panel.gameObject.AddComponent<Button>(); inside.targetGraphic = fill; inside.transition = Selectable.Transition.None;
        inside.navigation = new Navigation { mode = Navigation.Mode.None };
        if (framePrefab != null) { var frame = Instantiate(framePrefab, panel, false); RoguelikeCampaignScreen.Stretch((RectTransform)frame.transform); frame.fillColor = RoguelikeCampaignScreen.Deep; }
        var title = RoguelikeCampaignScreen.Rect("Paused", panel);
        RoguelikeCampaignScreen.Place(title, new Vector2(.5f,1), 0, -30, 460, 55); title.pivot = new Vector2(.5f,1);
        view.Label(title, L("ui_battle_paused"), 36);
        void Button(string key, float y, Action action, bool primary = false)
        {
            var b = view.Button(panel, L(key), new Vector2(.5f,1), 0, y, 400, 64, action, primary);
            b.GetComponent<RectTransform>().pivot = new Vector2(.5f,1);
            b.gameObject.AddComponent<UIButtonClickFeedback>().Bind(b.GetComponent<RectTransform>());
        }
        Button("btn_battle_resume", -120, Resume, true);
        Button("btn_settings_open", -210, OpenSettings);
        Button("btn_main_back_to_menu", -300, ConfirmLeave);
    }
    private void OpenSettings()
    {
        if (settings?.IsOpen == true) return;
        settings = ProjectSettingsOverlay.Open(settingsPrefab, mount, () =>
        {
            settings = null;
            bool english = ProjectUserSettings.English();
            if (pauseButton != null) pauseButton.GetComponentInChildren<TMP_Text>().text = L("btn_battle_pause");
            var details = battle != null ? battle.GetComponent<BattleUiCharacterDetails>() : null;
            details?.SetEnglish(english);
            var popups = battle != null ? battle.GetComponent<BattlefieldDamagePopupController>() : null;
            if (popups != null) popups.english = english;
            if (held) Draw();
        });
    }
    private void ConfirmLeave() => view.Confirmation(L("ui_battle_leave_title"), L("ui_battle_leave_body"),
        L("btn_main_back_to_menu"), L("btn_cancel"), () =>
        {
            // 先结束原运行器，避免释放暂停后在转场遮罩期间继续敌方行动。
            battle?.StopBattle();
            Resume();
            if (leave != null) leave(); else SceneManager.LoadSceneAsync(0);
        }, view.CloseWindow);
    public void Resume()
    {
        settings?.Close(false); settings = null;
        if (overlay != null) { overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); overlay = null; }
        view = null;
        if (!held) return;
        held = false; input?.SetUiInputBlocked(this, false); BattlePause.SetPaused(this, false);
    }
    private void Dispatch(Action action)
    { try { action(); } catch (Exception error) { Debug.LogException(error, this); view?.Notice(error.Message); } }
    private void OnEnable() => StartCoroutine(PrepareButton());
    private void OnDisable() { StopAllCoroutines(); Resume(); if (buttonRoot != null) buttonRoot.gameObject.SetActive(false); }
    private void OnDestroy() { if (buttonRoot != null) Destroy(buttonRoot.gameObject); }
}
