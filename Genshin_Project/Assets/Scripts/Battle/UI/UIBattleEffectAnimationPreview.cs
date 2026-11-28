using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared, manually clocked animation preview. Display requests only; no entities, battle declarations or gameplay RNG.</summary>
[DisallowMultipleComponent]
public sealed class UIBattleEffectAnimationPreview : MonoBehaviour
{
    public UICardAttackMotion motion;
    public UICardAirborneFlip airborne;
    public BattlefieldDamagePopupController popups;
    public bool Paused;
    public float Amount = 1234;
    public bool Critical;
    public float AbsorbedAmount = 1234;
    public float Elapsed { get; private set; }
    public float Duration { get; private set; }
    public bool Playing { get; private set; }
    public string Error { get; private set; }
    public string EffectID => plan?.Attack?.EffectID ?? "素材接触";
    private sealed class Shot
    {
        public UIBattleEffectPlayer View;
        public float Launch, Hit;
        public bool Started, Landed;
    }
    private readonly List<Shot> shots = new List<Shot>();
    private readonly List<UIBattleEffectPlayer> extraPlayers = new List<UIBattleEffectPlayer>();
    private readonly Dictionary<UIBattleEffectPlayer, BattleEffectKind> extraKinds = new Dictionary<UIBattleEffectPlayer, BattleEffectKind>();
    private readonly Dictionary<RectTransform, (BattleSide Side, int Slot)> anchors = new Dictionary<RectTransform, (BattleSide,int)>();
    private readonly List<RectTransform> targets = new List<RectTransform>();
    private BattleAttackAnimationTable attacks;
    private BattleEffectAnimationPreviewPlan plan;
    private BattleEffectParameters actionSettings, stateSettings;
    private UIBattleEffectPlayer statePlayer;
    private RectTransform caster;
    private float stateEnd;
    private bool stateStopped, stopped;
    private long self;
    private string absorbedElement;
    private Color? absorbedColor;

    public void SetVentiAbsorption(string element, Color? color)
    {
        absorbedElement = element; absorbedColor = color;
        if (stateSettings?.Kind == BattleEffectKind.VentiStormEye) statePlayer?.SetVentiAbsorbedColor(color);
        if (plan?.ActionKind == BattleEffectKind.VentiStormPulse)
            foreach (var shot in shots) if (shot.Started) shot.View.SetVentiAbsorbedColor(color);
    }
    public void ReloadModels(Action<UIBattleEffectPlayer,BattleEffectKind> bind)
    { foreach (var pair in extraKinds) bind(pair.Key, pair.Value); }

    public void Bind(Canvas canvas, RectTransform space)
    {
        Stop(true); anchors.Clear();
        if (motion == null || airborne == null || popups == null) throw new InvalidOperationException("完整预览缺少显式动作、翻面或跳字组件。");
        motion.AutomaticClock = false; airborne.AutomaticClock = false; popups.AutomaticClock = false;
        popups.BindPresentationCanvas(canvas, space);
        ReloadConfiguration();
    }
    public void RegisterCard(BattleSide side, int slot, RectTransform card)
    { if (card != null) { anchors[card] = (side, slot); popups.RegisterCard(side, slot, card); } }
    public void ReloadConfiguration()
    {
        var next = BattleAttackAnimationTable.Load();
        popups.ReloadConfiguration();
        if (!popups.Ready) throw new InvalidOperationException("完整预览跳字未就绪，请检查运行时配表与数字字体。");
        attacks = next;
    }
    public bool Play(BattleEffectKind kind, IReadOnlyList<RectTransform> actualTargets, RectTransform actualCaster,
        UIBattleEffectPlayer primary, UIBattleEffectPlayer companion, Func<BattleEffectKind,BattleEffectParameters> settings,
        Action<UIBattleEffectPlayer,BattleEffectKind> bind)
    {
        Stop(true); Error = null;
        try
        {
            if (actualTargets == null || actualTargets.Count == 0) throw new InvalidOperationException("请至少选择一个预览目标。");
            plan = new BattleEffectAnimationPreviewPlan(kind, attacks, settings);
            targets.Clear(); targets.AddRange(actualTargets); caster = actualCaster;
            actionSettings = settings(plan.ActionKind);
            stateSettings = plan.StateKind.HasValue ? settings(plan.StateKind.Value) : null;
            statePlayer = stateSettings == null ? null : plan.StateKind.Value == kind ? primary : companion;
            var first = plan.ActionKind == kind ? primary : companion;
            shots.Clear();
            for (int i = 0; i < plan.Hits.Length; i++)
            {
                UIBattleEffectPlayer view = first;
                if (i > 0)
                {
                    if (extraPlayers.Count < i)
                    {
                        var go = new GameObject("AnimationPreviewHit", typeof(UIBattleEffectPlayer)); go.transform.SetParent(transform, false);
                        extraPlayers.Add(go.GetComponent<UIBattleEffectPlayer>());
                    }
                    view = extraPlayers[i-1];
                    if (!extraKinds.TryGetValue(view, out var bound) || bound != plan.ActionKind)
                    { bind(view, plan.ActionKind); extraKinds[view] = plan.ActionKind; }
                }
                shots.Add(new Shot { View = view, Launch = plan.Launches[i], Hit = plan.Hits[i] });
            }
            float lastHit = plan.Hits.Length > 0 ? plan.Hits[plan.Hits.Length-1] : 0;
            Duration = plan.HasDamage ? lastHit + Math.Max(popups.PresentationLifetime,
                UIBattleEffectPlayer.PlaybackDuration(actionSettings, targets.Count, BattleEffectAnimationPreviewPlan.Sliced(plan.ActionKind))
                - BattleEffectTiming.Contact(actionSettings)) : 0;
            if (plan.FlipCards) Duration = Math.Max(Duration, lastHit + actionSettings["FlipSeconds"]);
            stateEnd = stateSettings != null ? Math.Max(lastHit, stateSettings["DurationSeconds"]) : 0;
            if (stateSettings != null) Duration = Math.Max(Duration, stateEnd + stateSettings["FadeOutSeconds"]);
            Duration = Math.Max(Duration, .1f);
            ResetPlayback(); return true;
        }
        catch (Exception failure) { Error = failure.Message; Stop(true); return false; }
    }
    private void ResetPlayback()
    {
        foreach (var shot in shots) { shot.View.Stop(true); shot.Started = shot.Landed = false; }
        statePlayer?.Stop(true); motion.Cancel(-1); airborne.Cancel(-1); popups.Cancel(-1);
        motion.Paused = airborne.Paused = false;
        Elapsed = 0; stopped = stateStopped = false; Playing = true;
        if (statePlayer != null)
        {
            var stateTargets = BattleEffectAnimationPreviewPlan.TargetsCaster(stateSettings.Kind)
                ? new[] { caster } : (IReadOnlyList<RectTransform>)targets;
            statePlayer.Paused = false;
            if (!statePlayer.TryPlay(stateSettings, stateTargets, caster)) throw new InvalidOperationException(statePlayer.Error);
            if (stateSettings.Kind == BattleEffectKind.VentiStormEye) statePlayer.SetVentiAbsorbedColor(absorbedColor);
        }
        float direction = caster != null && targets.Count > 0 ? Mathf.Sign(targets[0].position.x - caster.position.x) : -1;
        self = plan.Attack != null ? motion.PlaySelf(0, caster, plan.Attack, direction,
            plan.Hits.Length > 0 ? plan.Hits[0] : plan.Attack.Windup, !plan.HasDamage, null) : 0;
        Dispatch();
    }
    public void Seek(float seconds)
    {
        if (plan == null || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        try { ResetPlayback(); AdvanceCore(Mathf.Clamp(seconds, 0, Duration)); }
        catch (Exception failure) { Error = failure.Message; Stop(true); }
    }
    public void Advance(float seconds)
    {
        if (Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || plan == null) return;
        try { AdvanceCore(seconds); }
        catch (Exception failure) { Error = failure.Message; Stop(true); }
    }
    private void AdvanceCore(float seconds)
    {
        float end = Math.Min(Duration, Elapsed + seconds);
        while (Elapsed < end)
        {
            float boundary = end;
            if (!stopped)
            {
                foreach (var shot in shots)
                {
                    if (!shot.Started && shot.Launch > Elapsed) boundary = Math.Min(boundary, shot.Launch);
                    if (!shot.Landed && shot.Hit > Elapsed) boundary = Math.Min(boundary, shot.Hit);
                }
                if (statePlayer != null && !stateStopped && stateEnd > Elapsed) boundary = Math.Min(boundary, stateEnd);
            }
            float step = boundary - Elapsed;
            foreach (var shot in shots) if (shot.Started)
            { shot.View.Advance(step); if (shot.View.Error != null) throw new InvalidOperationException(shot.View.Error); }
            statePlayer?.Advance(step);
            if (statePlayer?.Error != null) throw new InvalidOperationException(statePlayer.Error);
            motion.Advance(step); airborne.Advance(step); popups.Advance(step);
            Elapsed = boundary;
            if (!stopped) Dispatch();
        }
        if (Elapsed >= Duration) { statePlayer?.Stop(true); Playing = false; }
    }
    private void Dispatch()
    {
        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            if (!shot.Started && shot.Launch <= Elapsed)
            {
                shot.View.Paused = false;
                float offset = shot.Hit - shot.Launch - BattleEffectTiming.Contact(actionSettings);
                var slice = BattleEffectAnimationPreviewPlan.Sliced(plan.ActionKind) ? new BattleEffectHitSlice(i, shots.Count) : null;
                if (!shot.View.TryPlay(actionSettings, targets, caster, 12345, false, Math.Max(0,offset), slice))
                    throw new InvalidOperationException(shot.View.Error);
                if (plan.ActionKind == BattleEffectKind.KaeyaIcicle && targets.Count == 1 && statePlayer != null
                    && statePlayer.TryCaptureKaeyaDeparture(targets[0], out var departure)) shot.View.SetKaeyaDeparture(departure);
                if (plan.ActionKind == BattleEffectKind.VentiStormPulse && statePlayer != null)
                    shot.View.SetVentiPhase(statePlayer.Elapsed * actionSettings["OrbitSeconds"] / stateSettings["OrbitSeconds"]);
                if (plan.ActionKind == BattleEffectKind.VentiStormPulse) shot.View.SetVentiAbsorbedColor(absorbedColor);
                if (offset < 0) shot.View.Seek(-offset);
                shot.Started = true;
            }
            if (!shot.Landed && shot.Hit <= Elapsed)
            {
                shot.View.ConfirmContact(); motion.Impact(self); shot.Landed = true;
                foreach (var card in targets)
                {
                    if (card == null || !anchors.TryGetValue(card, out var anchor)) continue;
                    motion.PlayHit(0, card, plan.Attack?.HitAnimation ?? 2, -1, unchecked((uint)(12345+i)), plan.Attack?.Strength ?? 1, null, plan.Attack);
                    if (!popups.TryShowDamage(anchor.Side, anchor.Slot, Amount, plan.Element, Critical, 12345, i))
                        throw new InvalidOperationException("预览伤害跳字未接受显示请求。");
                    if (plan.ActionKind == BattleEffectKind.VentiStormPulse && absorbedElement != null
                        && !popups.TryShowDamage(anchor.Side, anchor.Slot, AbsorbedAmount, absorbedElement, Critical, 12345, i, lane: 1))
                        throw new InvalidOperationException("预览转化伤害跳字未接受显示请求。");
                    if (plan.FlipCards && i == 0)
                    {
                        var view = card.GetComponent<UICardView>();
                        if (view != null) airborne.Play(0, view, actionSettings["FlipSeconds"], actionSettings["LiftHeightRatio"], null);
                    }
                }
            }
        }
        if (statePlayer != null && !stateStopped && Elapsed >= stateEnd) { statePlayer.Stop(); stateStopped = true; }
    }
    public void Stop(bool immediate)
    {
        stopped = true; Playing = false;
        foreach (var shot in shots) shot.View?.Stop(immediate);
        statePlayer?.Stop(immediate); motion?.Cancel(-1); airborne?.Cancel(-1); popups?.Cancel(-1);
    }
    private void OnDisable()
    {
        Stop(true); shots.Clear(); anchors.Clear(); targets.Clear(); plan = null;
        foreach (var view in extraPlayers) if (view != null) Destroy(view.gameObject);
        extraPlayers.Clear();
        extraKinds.Clear();
    }
}
