using System.Collections.Generic;
using UnityEngine;

/// <summary>与 IBattleUiShields 完全独立。一次命令是一份场地盾及其全体受保护卡牌。</summary>
public interface IBattleUiOverallShields
{
    bool SetOverallShield(long instanceId, string element, IReadOnlyList<BattleUiCardBinding> targets);
    void RemoveOverallShield(long instanceId);
    void ClearOverallShields();
}

/// <summary>可选：场景配置的特效Prefab接收元素信息；不由显示模块猜测颜色或材质。</summary>
public interface IOverallShieldVisual
{
    void SetElement(string element);
}

/// <summary>独立场地盾特效层。只克隆显式绑定的Prefab，不安装或修改个人盾、命中特效。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Overall Shields")]
public sealed class BattleUiOverallShields : MonoBehaviour, IBattleUiOverallShields
{
    public GameObject effectPrefab;
    private readonly Dictionary<long, Dictionary<RectTransform, GameObject>> groups
        = new Dictionary<long, Dictionary<RectTransform, GameObject>>();

    public bool SetOverallShield(long instanceId, string element, IReadOnlyList<BattleUiCardBinding> targets)
    {
        if (!isActiveAndEnabled || effectPrefab == null || targets == null) return false;
        if (!groups.TryGetValue(instanceId, out var group))
            groups.Add(instanceId, group = new Dictionary<RectTransform, GameObject>());
        var current = new HashSet<RectTransform>();
        foreach (var card in targets)
        {
            if (card?.art == null || !current.Add(card.art)) continue;
            if (!group.TryGetValue(card.art, out var visual) || visual == null)
            {
                visual = Instantiate(effectPrefab, card.shieldLayer != null ? card.shieldLayer : card.art, false);
                visual.name = "OverallShield_" + instanceId;
                group[card.art] = visual;
                foreach (var component in visual.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is IOverallShieldVisual sink) sink.SetElement(element);
                visual.SetActive(true);
            }
            else if (card.shieldLayer != null && visual.transform.parent != card.shieldLayer)
                visual.transform.SetParent(card.shieldLayer, false);
        }
        foreach (var card in new List<RectTransform>(group.Keys))
            if (card == null || !current.Contains(card)) { Release(group[card]); group.Remove(card); }
        return true;
    }

    public void RemoveOverallShield(long instanceId)
    {
        if (!groups.TryGetValue(instanceId, out var group)) return;
        foreach (var visual in group.Values) Release(visual);
        groups.Remove(instanceId);
    }
    public void ClearOverallShields()
    {
        foreach (long id in new List<long>(groups.Keys)) RemoveOverallShield(id);
    }
    private void Release(GameObject visual)
    {
        if (visual == null) return;
        visual.SetActive(false); // 同帧全队撤下，Destroy的帧末延迟不留下单张卡残影。
        Destroy(visual);
    }
    private void OnDisable() => ClearOverallShields();
    private void OnDestroy() => ClearOverallShields();
}
