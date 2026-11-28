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

public interface IBattleUiControlReleaseSource
{
    AllyControlState GetControl();
    bool CanClickControl { get; }
    AllyControlClickResult ClickControl();
}

/// <summary>Optional passive stun display. It provides no click-to-release operation.</summary>
public interface IBattleUiStunSource
{
    bool Stunned { get; }
}

/// <summary>Shared control view. Frozen/airborne retain their input; actual stun displays the same stars for either side.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiFreezeInteraction : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    public MonoBehaviour dataSource;
    public UICardFreeze effect;
    public UICardStunStars airborneStars;
    public UICardFreezeAudio clickAudio;
    public UIHitShakeController shake;
    public RectTransform shakeRoot;
    public Graphic hitArea;
    public string shakeProfile = "FreezeClick1";
    public bool keyboardEnabled;
    public KeyCode clickKey = KeyCode.Space;
    public FrozenThawClickResult LastResult { get; private set; }
    private HitShakeStyle.Profile profile;
    private ReactionStateInstance observed;
    private int renderedProgress;
    private StatusInstance observedAirborne;
    private int renderedAirborneProgress;
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
        if (!isActiveAndEnabled || Source == null) return false;
        Refresh();
        if (Source is IBattleUiControlReleaseSource controls && controls.GetControl().Airborne != null)
        {
            if (!controls.CanClickControl) return false;
            LastResult = FrozenThawClickResult.Ignored;
            var clicked = controls.ClickControl();
            if (clicked == AllyControlClickResult.InsufficientAP) Pulse();
            Refresh();
            return clicked == AllyControlClickResult.Progressed || clicked == AllyControlClickResult.Released;
        }
        if (effect == null || !effect.Available) return false;
        var state = Source.GetFreeze();
        if (state.Instance == null || !state.CanInteract) return false;
        LastResult = Source.ClickThaw();
        if (LastResult == FrozenThawClickResult.InsufficientAP) Pulse();
        Refresh();
        return LastResult == FrozenThawClickResult.Cracked || LastResult == FrozenThawClickResult.Thawed;
    }

    public void Refresh()
    {
        if (Source == null)
        { airborneStars?.SetRemaining(0); if (hitArea != null) hitArea.raycastTarget = false; return; }
        var controls = Source as IBattleUiControlReleaseSource;
        var airborne = controls?.GetControl().VisibleAirborne;
        // 先读旧实例。成功解除后即使已经换成下一份控制，第三下仍对应刚解除的实例。
        if (observedAirborne != null && observedAirborne.ControlReleaseClicks != renderedAirborneProgress) Pulse();
        observedAirborne = airborne;
        renderedAirborneProgress = airborne?.ControlReleaseClicks ?? 0;
        bool stunned = (Source as IBattleUiStunSource)?.Stunned == true;
        airborneStars?.SetRemaining(stunned ? AllyControlRelease.RequiredClicks :
            airborne == null ? 0 : AllyControlRelease.RequiredClicks - renderedAirborneProgress);
        var state = Source.GetFreeze();
        bool iceAvailable = effect != null && effect.Available;
        if (hitArea != null) hitArea.raycastTarget = isActiveAndEnabled &&
            ((airborne != null && controls.CanClickControl) || (iceAvailable && state.Instance != null && state.CanInteract));
        if (!iceAvailable) return;
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
        shake?.Cancel(Playback); observed = null; observedAirborne = null;
        airborneStars?.SetRemaining(0);
        if (hitArea != null) hitArea.raycastTarget = false;
        effect?.Hide();
    }
}
