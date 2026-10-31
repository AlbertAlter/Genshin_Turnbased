using System;
using UnityEngine;

/// <summary>Reusable Inspector-driven source for standalone HUD scenes; never starts a battle.</summary>
[AddComponentMenu("Battle UI/Data/Manual HUD Source")]
public sealed class BattleUiManualHudSource : MonoBehaviour, IBattleUiHealthSource,
    IBattleUiEnemyShieldSource, IBattleUiApSource, IBattleUiActionSource
{
    [Serializable] public sealed class Meter
    {
        public BattleSide side;
        public int slot = 1;
        public bool visible = true;
        public float current = 100, maximum = 100;
        public string element = "None";
        public BattleUiMeterState State => new BattleUiMeterState {
            Identity = this, Visible = visible, Current = current, Maximum = maximum, Element = element };
    }
    [Serializable] public sealed class Action
    {
        public int slot, skillType;
        public bool visible = true, frozen;
        public int cooldown;
        public string icon;
    }
    public Meter[] health = Array.Empty<Meter>(), enemyShields = Array.Empty<Meter>();
    public Action[] actions = Array.Empty<Action>();
    public int ap = 80, maxAP = 100, previewDelta = -20;
    public bool apAvailable = true, preview;
    public BattleUiMeterState GetHealth(BattleSide side, int slot) =>
        isActiveAndEnabled ? Array.Find(health, m => m != null && m.side == side && m.slot == slot)?.State ?? default : default;
    public BattleUiMeterState GetEnemyShield(int slot) =>
        isActiveAndEnabled ? Array.Find(enemyShields, m => m != null && m.slot == slot)?.State ?? default : default;
    public BattleUiApState GetAP() => new BattleUiApState { Identity = this, Available = isActiveAndEnabled && apAvailable,
        Current = ap, Maximum = maxAP, PreviewDelta = preview ? previewDelta : (int?)null };
    public BattleUiActionState GetAction(int slot, int skillType)
    {
        var a = isActiveAndEnabled ? Array.Find(actions, x => x != null && x.slot == slot && x.skillType == skillType) : null;
        return a == null ? default : new BattleUiActionState { Identity = a, Visible = a.visible,
            Frozen = a.frozen, Cooldown = a.cooldown, Icon = a.icon };
    }
}
