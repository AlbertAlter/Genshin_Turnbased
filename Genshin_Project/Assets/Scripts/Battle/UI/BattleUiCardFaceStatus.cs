using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads real personal/position statuses and adapts them to the independent card-face display host.
/// StatusType is just a classification: ChargeTarget needs no extra instance data or battle logic.
/// </summary>
[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Status Card Face UI")]
public sealed class BattleUiCardFaceStatus : MonoBehaviour
{
    public BattleUiLayout layout;
    public BattleManager battle;
    [Tooltip("独立显示层；不要放在会随占位者死亡而禁用的卡牌节点下。")]
    public RectTransform displayMount;
    public UICardTargetAssets targetedAssets;
    public UICardConductiveAssets talismanAssets;
    public BattleUiShields membraneAssets;
    public CardFaceConductiveStyle talismanLayout = new CardFaceConductiveStyle
    { referenceSize = new Vector2(80, 100) };
    public CardTargetIndicatorStyle targetedLayout = new CardTargetIndicatorStyle();
    public bool automaticClock = true;
    public bool Paused { get; set; }
    public float Speed { get; set; } = 1;
    public string Error { get; private set; }
    private CardFaceStatusTable table;
    private UICardTargetAssets subscribedAssets;
    private UICardConductiveAssets subscribedTalisman;
    private readonly Dictionary<string, UICardFaceUI.Registration> factories = new Dictionary<string, UICardFaceUI.Registration>(StringComparer.Ordinal);
    private readonly Dictionary<string, CardFaceStatusTable.Rule> desired = new Dictionary<string, CardFaceStatusTable.Rule>(StringComparer.Ordinal);
    private readonly HashSet<string> reportedTypes = new HashSet<string>(StringComparer.Ordinal);
    private sealed class Entry { public FieldPosition Position; public RectTransform Area; public UICardFaceUI UI; }
    private readonly Dictionary<BattleUiCardBinding, Entry> entries = new Dictionary<BattleUiCardBinding, Entry>();
    private readonly HashSet<BattleUiCardBinding> active = new HashSet<BattleUiCardBinding>();
    private readonly List<BattleUiCardBinding> removal = new List<BattleUiCardBinding>();
    private readonly List<StatusInstance> statuses = new List<StatusInstance>();

    /// <summary>Future card-face effects register their factory explicitly; no per-scene effect algorithms.</summary>
    public void Register(string uiType, UICardFaceUI.Factory factory, Func<bool> isReady = null)
    {
        if (string.IsNullOrWhiteSpace(uiType) || factory == null) throw new ArgumentException("卡面 UI 注册需要类型与工厂。");
        Clear(); factories[uiType] = new UICardFaceUI.Registration(factory, isReady); reportedTypes.Remove(uiType);
    }
    public bool ReloadConfiguration()
    {
        try { var next = CardFaceStatusTable.ReadPublished(); table = next; Error = null; reportedTypes.Clear(); return true; }
        catch (Exception error) { Error = "卡面状态展示配表读取失败：" + error.Message; Debug.LogError(Error, this); return false; }
    }
    private void OnEnable()
    {
        factories["Targeted"] = new UICardFaceUI.Registration(CreateTargeted,
            () => subscribedAssets != null && subscribedAssets.Ready);
        foreach (string element in UICardMembraneStatusEffect.Elements)
        {
            string selectedElement = element;
            factories["Shield" + element] = new UICardFaceUI.Registration((area, mount) => CreateMembrane(area, mount, selectedElement),
                () => membraneAssets != null && membraneAssets.isActiveAndEnabled && membraneAssets.Ready);
        }
        ReloadConfiguration();
        subscribedAssets = targetedAssets;
        if (subscribedAssets != null)
        {
            subscribedAssets.Invalidated += Clear;
            StartCoroutine(Prepare(subscribedAssets));
        }
        else Debug.LogError("BattleUiCardFaceStatus 需要显式绑定准心共享素材组件。", this);
        subscribedTalisman = talismanAssets;
        if (subscribedTalisman != null)
        {
            factories["QiqiTalisman"] = new UICardFaceUI.Registration(CreateTalisman,
                () => subscribedTalisman != null && subscribedTalisman.Ready && subscribedTalisman.IsTalisman);
            subscribedTalisman.Invalidated += Clear;
            StartCoroutine(PrepareTalisman(subscribedTalisman));
        }
    }
    private IEnumerator PrepareTalisman(UICardConductiveAssets assets)
    {
        if (!assets.IsTalisman) { Debug.LogError("度厄真符资源必须显式选择QiqiTalisman预设。", this); yield break; }
        yield return assets.Load();
        if (isActiveAndEnabled && assets == subscribedTalisman && !assets.Ready) Debug.LogError(assets.Error, this);
    }
    private IUICardFaceEffect CreateTalisman(RectTransform cardArea, RectTransform mount)
    {
        var effect = mount.gameObject.AddComponent<UICardConductive>();
        effect.automaticClock = false; effect.style = talismanLayout.Copy();
        if (!effect.Bind(cardArea, mount, subscribedTalisman)) throw new InvalidOperationException("度厄真符无法绑定卡面。");
        effect.SetCount(1); // Presence is resolved from the real status, not an invented Count or duration.
        return effect;
    }
    private IUICardFaceEffect CreateMembrane(RectTransform cardArea, RectTransform mount, string element)
    {
        RectTransform shieldMount = null;
        if (layout != null)
            foreach (var card in layout.Cards)
                if (card?.art == cardArea) { shieldMount = card.shieldLayer; break; }
        if (shieldMount == null) return null; // The requested card has not produced its authored shield layer yet.
        var effect = mount.gameObject.AddComponent<UICardMembraneStatusEffect>();
        if (!effect.Bind(shieldMount, membraneAssets, element)) throw new InvalidOperationException("状态护盾无法借用公共护盾膜：" + element);
        return effect;
    }
    private IEnumerator Prepare(UICardTargetAssets assets)
    {
        yield return assets.Load();
        if (isActiveAndEnabled && assets == subscribedAssets && !assets.Ready)
            Debug.LogError("卡面状态展示素材未就绪：" + assets.Error, this);
    }
    private IUICardFaceEffect CreateTargeted(RectTransform cardArea, RectTransform mount)
    {
        if (subscribedAssets == null || !subscribedAssets.Ready) return null;
        var effect = mount.gameObject.AddComponent<UICardTargetIndicator>();
        effect.automaticClock = false; effect.style = targetedLayout.Copy();
        if (!effect.Bind(cardArea, subscribedAssets, mount)) throw new InvalidOperationException(effect.Error);
        return effect;
    }
    private void LateUpdate()
    {
        if (table == null || layout == null || displayMount == null || battle == null || !battle.IsBattleRunning || battle.IsBattleOver)
        { Clear(); return; }
        active.Clear();
        foreach (var binding in layout.Cards)
        {
            if (binding?.art == null) continue;
            var position = battle.Field?.GetSlot(binding.side, binding.slot);
            if (position == null) continue;
            active.Add(binding);
            if (entries.TryGetValue(binding, out var entry) && (entry.Position != position || entry.Area != binding.art))
            { entry.UI.Dispose(); entries.Remove(binding); entry = null; }
            // Card-face means display bounds, not a new status host. Position markers must remain
            // visible on empty positions after death/removal. Only personal statuses depend on an occupant.
            statuses.Clear();
            if (position.StatusList != null) statuses.AddRange(position.StatusList);
            var entity = battle.GetEntityByPosition(binding.side, binding.slot);
            if (entity != null && entity.IsAlive && entity.IsOnField) statuses.AddRange(entity.GetStatusList());
            table.Resolve(statuses, desired);
            foreach (string type in desired.Keys)
                if (!factories.ContainsKey(type) && reportedTypes.Add(type)) Debug.LogError("卡面 UI 未注册展示类型：" + type, this);
            if (entry == null && desired.Count == 0) continue;
            if (entry == null)
            {
                entry = new Entry { Position = position, Area = binding.art,
                    UI = new UICardFaceUI(binding.art, displayMount, factories) };
                entries.Add(binding, entry);
            }
            entry.UI.Synchronize(desired);
        }
        removal.Clear();
        foreach (var binding in entries.Keys) if (!active.Contains(binding)) removal.Add(binding);
        foreach (var binding in removal) { entries[binding].UI.Dispose(); entries.Remove(binding); }
        if (automaticClock) Advance(BattlePause.DeltaTime);
    }
    public void Advance(float seconds)
    {
        if (!isActiveAndEnabled || Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)
            || Speed < 0 || float.IsNaN(Speed) || float.IsInfinity(Speed)) return;
        foreach (var entry in entries.Values) entry.UI.Advance(seconds * Speed);
    }
    public void Clear()
    {
        foreach (var entry in entries.Values) entry.UI.Dispose();
        entries.Clear(); desired.Clear(); active.Clear(); removal.Clear(); statuses.Clear();
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (subscribedAssets != null) subscribedAssets.Invalidated -= Clear;
        if (subscribedTalisman != null) subscribedTalisman.Invalidated -= Clear;
        subscribedTalisman = null; factories.Remove("QiqiTalisman");
        foreach (string element in UICardMembraneStatusEffect.Elements) factories.Remove("Shield" + element);
        subscribedAssets = null; Clear();
    }
}
