using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable settings page. Scene owns its mounts; this view owns controls and user preference commands.</summary>
public sealed partial class ProjectSettingsView : MonoBehaviour
{
    public RectTransform backdropMount, headerMount, controlsMount, footerMount;
    public TMP_FontAsset font;
    public UIAdaptiveFrame framePrefab;
    private RoguelikeCampaignScreen view;
    private TMP_Text title;
    private bool english, fullscreen;
    private int resolution;
    private Button generalTab, keysTab;
    private bool showingKeys;
    private readonly Vector2Int[] sizes = { new Vector2Int(1280,720), new Vector2Int(1600,900), new Vector2Int(1920,1080), new Vector2Int(2560,1440) };
    private string L(string key, params object[] args) => DataManager.Instance.GetLangText(key, english, args);
    public void Bind(RectTransform content)
    {
        ((RectTransform)transform).SetParent(content, false);
        RoguelikeCampaignScreen.Stretch((RectTransform)transform);
        english = ProjectUserSettings.English(); fullscreen = ProjectUserSettings.Fullscreen;
        resolution = Array.FindIndex(sizes, s => s.x == ProjectUserSettings.Width && s.y == ProjectUserSettings.Height);
        if (resolution < 0) resolution = 2;
        UIProjectMenuBackground.Apply(backdropMount);
        var head = RoguelikeCampaignScreen.Rect("Settings title", headerMount); RoguelikeCampaignScreen.Stretch(head);
        view = new RoguelikeCampaignScreen(controlsMount, font, Dispatch, framePrefab);
        title = view.Label(head, "", 44, TextAlignmentOptions.MidlineLeft);
        generalTab = view.Button(headerMount, L("ui_settings_general"), Vector2.one, -260, -10, 220, 56, () => SelectPage(false));
        keysTab = view.Button(headerMount, L("ui_settings_controls"), Vector2.one, -20, -10, 220, 56, () => SelectPage(true));
        Draw();
    }
    private void Dispatch(Action action) { try { action(); } catch (Exception error) { view.Notice(error.Message); } }
    private TMP_Text Label(string text, float x, float y, float width, float size = 26)
    {
        var rect = RoguelikeCampaignScreen.Rect(text, view.Root);
        RoguelikeCampaignScreen.Place(rect, new Vector2(0,1), x, y, width, 52);
        return view.Label(rect, text, size, TextAlignmentOptions.MidlineLeft);
    }
    private void Draw()
    {
        view.Clear(); title.text = L(showingKeys ? "ui_settings_controls" : "ui_settings_title");
        generalTab.GetComponentInChildren<TMP_Text>().text = L("ui_settings_general");
        keysTab.GetComponentInChildren<TMP_Text>().text = L("ui_settings_controls");
        generalTab.interactable = showingKeys; keysTab.interactable = !showingKeys;
        if (showingKeys) { DrawKeys(); return; }
        Label(L("ui_settings_audio"), 20, 0, 650, 32);
        string[] channels = { "Master", "Music", "Effects", "Voice" };
        for (int i = 0; i < channels.Length; i++) Slider(channels[i], -100 - i * 100);
        Label(L("ui_settings_display"), 900, 0, 650, 32);
        view.Button(view.Root, L(fullscreen ? "ui_settings_fullscreen" : "ui_settings_windowed"), new Vector2(0,1), 900, -100, 670, 66,
            () => { fullscreen = !fullscreen; Draw(); });
        var size = sizes[resolution];
        view.Button(view.Root, L("ui_settings_resolution", size.x, size.y), new Vector2(0,1), 900, -200, 670, 66,
            () => { resolution = (resolution + 1) % sizes.Length; Draw(); });
        view.Button(view.Root, L("ui_settings_language"), new Vector2(0,1), 900, -300, 670, 66,
            () => { english = !english; ProjectUserSettings.SetEnglish(english); Draw(); });
        view.Button(view.Root, L("btn_settings_apply_display"), new Vector2(0,1), 900, -400, 670, 66,
            () => { var value = sizes[resolution]; ProjectUserSettings.SetDisplay(value.x, value.y, fullscreen); view.Notice(L("tip_settings_saved")); }, true);
        Label(L("ui_settings_display_hint"), 900, -490, 680, 22);
        view.Button(view.Root, L("btn_settings_reset_audio"), new Vector2(0,0), 20, 5, 360, 56,
            () => { ProjectUserSettings.ResetAudio(); Draw(); });
    }
    private void Slider(string channel, float y)
    {
        Label(L("ui_settings_" + channel.ToLowerInvariant()), 20, y, 230);
        float initial = channel == "Master" ? ProjectUserSettings.Master : channel == "Music" ? ProjectUserSettings.Music : channel == "Voice" ? ProjectUserSettings.Voice : ProjectUserSettings.Effects;
        var value = Label(Mathf.RoundToInt(initial * 100) + "%", 720, y, 120);
        var rect = RoguelikeCampaignScreen.Rect(channel + " volume", view.Root);
        RoguelikeCampaignScreen.Place(rect, new Vector2(0,1), 275, y - 10, 410, 32);
        var track = rect.gameObject.AddComponent<Image>(); track.color = new Color32(66,54,44,255);
        var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.minValue = 0; slider.maxValue = 1;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        var fill = RoguelikeCampaignScreen.Rect("Fill", rect); RoguelikeCampaignScreen.Stretch(fill);
        fill.gameObject.AddComponent<Image>().color = new Color32(233,207,158,255); slider.fillRect = fill;
        var handle = RoguelikeCampaignScreen.Rect("Handle", rect); handle.anchorMin = handle.anchorMax = Vector2.one * .5f; handle.sizeDelta = new Vector2(18,42);
        var knob = handle.gameObject.AddComponent<Image>(); knob.color = RoguelikeCampaignScreen.Cream;
        slider.handleRect = handle; slider.targetGraphic = knob; slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(amount => { ProjectUserSettings.SetVolume(channel, amount); value.text = Mathf.RoundToInt(amount * 100) + "%"; });
    }
}
