using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Consumes the same scheduled hit cue as popups. Never subscribes to damage a second time.</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldHitShakeView : MonoBehaviour, IBattlePresentationView
{
    private HitShakeStyle style;
    private UIHitShakeController motion;
    private readonly Dictionary<(BattleSide, int), RectTransform> cards = new Dictionary<(BattleSide, int), RectTransform>();
    private long lastPlayback = -1;
    private void OnEnable()
    {
        motion = GetComponent<UIHitShakeController>() ?? gameObject.AddComponent<UIHitShakeController>();
        ReloadConfiguration(); BindCards();
        GetComponent<BattlefieldPresentationPlayer>()?.RegisterView(this);
    }
    public void ReloadConfiguration()
    {
        try { style = HitShakeStyle.Load(); }
        catch (Exception error) { style = null; Debug.LogError("命中抖动配表加载失败：" + error.Message, this); }
    }
    private void BindCards()
    {
        var canvas = GetComponentInParent<Canvas>()?.rootCanvas ?? GetComponentInChildren<Canvas>(true)?.rootCanvas ??
            FindObjectsOfType<Canvas>(true).FirstOrDefault(item => item.isRootCanvas && item.gameObject.scene == gameObject.scene);
        if (canvas == null) return;
        motion.BindScreen(canvas.transform as RectTransform);
        var transforms = canvas.GetComponentsInChildren<Transform>(true);
        void Bind(string name, string prefix, BattleSide side)
        {
            var container = transforms.FirstOrDefault(item => item.name == name); if (container == null) return;
            var groups = Enumerable.Range(0, container.childCount).Select(container.GetChild)
                .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(item => Slot(item.name));
            int slot = 1; foreach (var group in groups) RegisterCard(side, slot++, group as RectTransform);
        }
        Bind("character", "CharacterCard", BattleSide.Ally); Bind("Enemy", "EnemyCard", BattleSide.Enemy);
    }
    private static int Slot(string name)
    {
        string digits = new string(name.Where(char.IsDigit).ToArray()); return int.TryParse(digits, out int number) ? number : int.MaxValue;
    }
    public void RegisterCard(BattleSide side, int slot, RectTransform root)
    { if (root != null) { cards[(side, slot)] = root; motion?.RegisterCard(root); } }
    public void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            if (!isActiveAndEnabled || style == null || !(cue.Result.Data is DamageEventData damage) || !damage.HitLanded || !cue.Result.TargetSide.HasValue)
            { completed(null); return; }
            if (lastPlayback != playbackID) { motion.ForgetScreenHits(lastPlayback); lastPlayback = playbackID; }
            var selection = style.Resolve(damage.DamageType, cue.Result.EffectID, damage.HitIndex);
            var key = (cue.Result.TargetSide.Value, cue.Result.TargetSlot);
            if (!cards.TryGetValue(key, out var card) || card == null) { BindCards(); cards.TryGetValue(key, out card); }
            var header = cue.Result.Header;
            uint seed = HitEffectMath.CueSeed(unchecked((int)header.SessionId), damage.HitIndex, header.TargetIndex);
            seed = HitEffectMath.Mix(seed ^ unchecked((uint)header.EventId));
            motion.PulseCard(playbackID, card, selection.Card, seed);
            // Execution ID distinguishes repeated applications of the same effect within one skill.
            long execution = header.EffectExecutionId != 0 ? header.EffectExecutionId : cue.Result.RequestID != 0 ? cue.Result.RequestID : header.HitId;
            motion.PulseScreen(playbackID, header.SessionId, execution, cue.Result.EffectID, damage.HitIndex,
                selection.Screen, HitEffectMath.Mix(unchecked((uint)header.HitId) ^ (uint)execution));
        }
        catch (Exception error) { completed(error); return; }
        completed(null); // Cosmetic tails do not delay the next real result, like floating numbers.
    }
    public void Cancel(long playbackID) => motion?.Cancel(playbackID);
    private void OnDisable()
    { GetComponent<BattlefieldPresentationPlayer>()?.UnregisterView(this); motion?.Cancel(-1); cards.Clear(); }
}
