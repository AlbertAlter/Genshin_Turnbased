using TMPro;
using UnityEngine;

/// <summary>Displays committed input failures through the shared tip. Bind one event source to avoid duplicates.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiFloatingTips : MonoBehaviour
{
    public BattleManager battle;
    public Canvas canvas;
    public TMP_FontAsset font;
    public bool english;
    private BattleManager subscribed;
    private UIFloatingTip tip;

    private void OnEnable() => BindSource();
    private void LateUpdate() { if (subscribed != battle) BindSource(); }
    private void BindSource()
    {
        if (subscribed != null) subscribed.TipRequested -= Show;
        subscribed = battle;
        if (subscribed != null) subscribed.TipRequested += Show;
    }
    private void Show(string key)
    {
        if (canvas == null || font == null || string.IsNullOrEmpty(key)) return;
        var data = DataManager.Instance;
        if (data == null || !data.LangDict.ContainsKey(key))
        { Debug.LogWarning("战斗提示缺少 Lang：" + key, this); return; }
        if (tip == null) tip = UIFloatingTip.Create((RectTransform)canvas.transform, font, data);
        tip.Show(data.GetLangText(key, ProjectUserSettings.English(english)));
    }
    private void OnDisable()
    {
        if (subscribed != null) subscribed.TipRequested -= Show;
        subscribed = null;
        if (tip != null) Destroy(tip.gameObject);
        tip = null;
    }
}
