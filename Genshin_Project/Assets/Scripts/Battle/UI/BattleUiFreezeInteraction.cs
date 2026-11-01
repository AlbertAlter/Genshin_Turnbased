using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public struct BattleUiFreezeState
{
    public ReactionStateInstance Instance;
    public bool CanInteract;
}
public interface IBattleUiFreezeSource
{
    BattleUiFreezeState GetFreeze();
    FrozenThawClickResult ClickThaw();
}

/// <summary>Reusable click/keyboard adapter. Reads real progress and calls shared thaw + existing hit-shake modules.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiFreezeInteraction : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    public MonoBehaviour dataSource;
    public UICardFreeze effect;
    public UICardFreezeAudio clickAudio;
    public UIHitShakeController shake;
    public RectTransform shakeRoot;
    public Graphic hitArea;
    public string shakeProfile = "CardNormal";
    public bool keyboardEnabled;
    public KeyCode clickKey = KeyCode.Space;
    public FrozenThawClickResult LastResult { get; private set; }
    private HitShakeStyle.Profile profile;
    private ReactionStateInstance observed;
    private int renderedProgress;
    private uint clickSerial;
    private long Playback => long.MaxValue - unchecked((uint)GetInstanceID());
    private IBattleUiFreezeSource Source => dataSource as IBattleUiFreezeSource;

    private void OnEnable()
    {
        try { profile = HitShakeStyle.Load().Get(shakeProfile); }
        catch (Exception ex) { profile = null; Debug.LogError("冻结点击震动配置：" + ex.Message, this); }
    }
    private void Update()
    {
        Refresh();
        if (keyboardEnabled && Input.GetKeyDown(clickKey)) RequestClick();
    }
    public void OnPointerClick(PointerEventData eventData)
    { if (eventData.button == PointerEventData.InputButton.Left) { RequestClick(); eventData.Use(); } }
    // Capture the press on this hit area; an ancestor target/switch Button must not steal the thaw click.
    public void OnPointerDown(PointerEventData eventData)
    { if (eventData.button == PointerEventData.InputButton.Left) eventData.Use(); }
    public void OnPointerUp(PointerEventData eventData)
    { if (eventData.button == PointerEventData.InputButton.Left) eventData.Use(); }

    public bool RequestClick()
    {
        if (!isActiveAndEnabled || Source == null || effect == null || !effect.Available) return false;
        Refresh();
        var state = Source.GetFreeze();
        if (state.Instance == null || !state.CanInteract) return false;
        LastResult = Source.ClickThaw();
        if (LastResult == FrozenThawClickResult.InsufficientAP) Pulse();
        Refresh();
        return LastResult == FrozenThawClickResult.Cracked || LastResult == FrozenThawClickResult.Thawed;
    }

    public void Refresh()
    {
        if (Source == null || effect == null || !effect.Available)
        { if (hitArea != null) hitArea.raycastTarget = false; return; }
        var state = Source.GetFreeze();
        if (hitArea != null) hitArea.raycastTarget = isActiveAndEnabled && state.Instance != null && state.CanInteract;
        if (state.Instance == null)
        {
            if (observed != null && observed.ThawClicks == FrozenThawInteraction.RequiredClicks)
            { effect.Shatter(); Pulse(); clickAudio?.PlayClick(FrozenThawInteraction.RequiredClicks); }
            else if (!effect.IsShattering) effect.Hide();
            observed = null; return;
        }
        if (!ReferenceEquals(observed, state.Instance))
        {
            observed = state.Instance; renderedProgress = observed.ThawClicks;
            LastResult = FrozenThawClickResult.Ignored; effect.ShowStage(renderedProgress, false); return;
        }
        if (renderedProgress != observed.ThawClicks)
        {
            renderedProgress = observed.ThawClicks; effect.ShowStage(renderedProgress); Pulse();
            clickAudio?.PlayClick(renderedProgress);
        }
        else if (effect.CurrentStage != Mathf.Clamp(renderedProgress, 0, 2) && !effect.IsShattering)
            effect.ShowStage(renderedProgress, false);
    }
    private void Pulse()
    {
        if (shake != null && shake.isActiveAndEnabled && profile != null && shakeRoot != null)
            shake.PulseCard(Playback, shakeRoot, profile, HitEffectMath.Mix(++clickSerial));
    }
    private void OnDisable()
    {
        shake?.Cancel(Playback); observed = null;
        if (hitArea != null) hitArea.raycastTarget = false;
        effect?.Hide();
    }
}
