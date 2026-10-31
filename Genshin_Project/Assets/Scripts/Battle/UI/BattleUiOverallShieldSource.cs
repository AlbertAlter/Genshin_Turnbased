using System.Collections.Generic;
using UnityEngine;

/// <summary>真实战斗到独立场地盾显示接口的适配器；场景显式绑定，不访问个人盾显示接口。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Overall Shield Source")]
public sealed class BattleUiOverallShieldSource : MonoBehaviour
{
    public BattleManager battle;
    public BattleUiLayout layout;
    public MonoBehaviour view;
    private IBattleUiOverallShields boundView;
    private readonly HashSet<long> shown = new HashSet<long>();
    private readonly Dictionary<Shield, long> identities = new Dictionary<Shield, long>();
    private long nextIdentity;

    // hit提交是同步操作，LateUpdate只读该帧已完成的批量结果，不显示逐目标的中间盾量。
    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        var nextView = view as IBattleUiOverallShields;
        if (!ReferenceEquals(nextView, boundView)) { Clear(); boundView = nextView; }
        if (boundView == null) return;
        if (battle == null || layout == null || !battle.IsBattleRunning || battle.IsBattleOver)
        { Clear(); return; }
        var active = new HashSet<long>();
        var activeShields = new HashSet<Shield>();
        foreach (var side in new[] { BattleSide.Ally, BattleSide.Enemy })
        {
            var members = OverallShieldSystem.Members(battle, side);
            var targets = new List<BattleUiCardBinding>();
            foreach (var member in members)
                if (layout.TryGetCard(side, member.SlotPosition, out var card)) targets.Add(card);
            foreach (var owner in OverallShieldSystem.Members(battle, side, includeOffField: true))
                foreach (var shield in owner.Shields)
                {
                    if (!OverallShieldSystem.IsActive(owner, shield)) continue;
                    activeShields.Add(shield);
                    if (!identities.TryGetValue(shield, out long id)) identities.Add(shield, id = ++nextIdentity);
                    active.Add(id);
                    if (boundView.SetOverallShield(id, shield.Element, targets)) shown.Add(id);
                }
        }
        foreach (long id in new List<long>(shown))
            if (!active.Contains(id)) { boundView.RemoveOverallShield(id); shown.Remove(id); }
        foreach (var shield in new List<Shield>(identities.Keys))
            if (!activeShields.Contains(shield)) identities.Remove(shield);
    }
    private void Clear()
    {
        foreach (long id in shown) boundView?.RemoveOverallShield(id);
        shown.Clear();
        identities.Clear();
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}
