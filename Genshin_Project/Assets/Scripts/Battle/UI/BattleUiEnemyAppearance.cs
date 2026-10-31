using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional battle adapter: reads committed AI mode; asks shared card service to crossfade. Never shakes cards.</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class BattleUiEnemyAppearance : MonoBehaviour
{
    public BattleUiAnimatedCards cards;
    private EnemyAppearanceTable table;
    private UICardVariantStyle style;
    private sealed class Entry
    {
        public BattleEntity Entity; public UICardView Card; public UICardVariantSwitcher Switcher;
        public CanvasGroup Visibility; public float Alpha; public bool OwnVisibility, Loaded, Initialized, Failed;
        public Coroutine Loading; public string LastError;
    }
    private readonly Dictionary<BattleUiCardBinding, Entry> entries = new Dictionary<BattleUiCardBinding, Entry>();
    private void OnEnable()
    {
        try { table = EnemyAppearanceTable.Load(); style = UICardVariantStyle.Load(); }
        catch (Exception error) { table = null; Debug.LogError("BattleUiEnemyAppearance: " + error.Message, this); }
    }
    private void LateUpdate()
    {
        if (table == null || cards == null || !cards.isActiveAndEnabled || cards.layout == null ||
            cards.battle == null || cards.factory == null || !cards.factory.isActiveAndEnabled)
        { Clear(); return; }
        var active = new HashSet<BattleUiCardBinding>();
        foreach (var binding in cards.layout.Cards)
        {
            if (binding == null || binding.side != BattleSide.Enemy) continue;
            var entity = cards.battle.GetEntityByPosition(binding.side, binding.slot);
            if (entity == null || !entity.IsAlive || entity.EnemyCtrl == null || !table.Contains(entity.EntityID) ||
                !cards.TryGetCard(binding, out var card)) continue;
            active.Add(binding);
            if (entries.TryGetValue(binding, out var entry) && (entry.Entity != entity || entry.Card != card))
            { Release(entry); entries.Remove(binding); entry = null; }
            if (entry == null)
            {
                entry = new Entry { Entity = entity, Card = card };
                // Hide only during initial preload, so a non-default incoming mode never flashes variant 01.
                entry.Visibility = card.GetComponent<CanvasGroup>();
                entry.OwnVisibility = entry.Visibility == null;
                if (entry.OwnVisibility) entry.Visibility = card.gameObject.AddComponent<CanvasGroup>();
                entry.Alpha = entry.Visibility.alpha; entry.Visibility.alpha = 0;
                entries.Add(binding, entry); entry.Loading = StartCoroutine(Prepare(entry));
            }
            if (!entry.Loaded || entry.Failed) continue;
            int pattern = entity.EnemyCtrl.CurrentPatternID;
            if (pattern <= 0) continue; // Controller has not finished initialization.
            if (!table.TryResolve(entity.EntityID, pattern, out int target))
            { RestoreVisibility(entry); Report(entry, $"敌人{entity.EntityID}模式{pattern}没有差分映射，保留原卡。"); continue; }
            if (!entry.Initialized)
            {
                if (entry.Switcher.SetImmediate(target))
                { entry.Initialized = true; RestoreVisibility(entry); entry.LastError = null; }
                else { RestoreVisibility(entry); Report(entry, entry.Switcher.Error); }
            }
            // Always re-resolve the latest mode after a fade; requests during the fade are not dropped.
            else if (!entry.Switcher.Pending && entry.Switcher.CurrentVariant != target)
            {
                if (!entry.Switcher.FadeTo(target, style)) Report(entry, entry.Switcher.Error);
                else entry.LastError = null;
            }
        }
        foreach (var binding in new List<BattleUiCardBinding>(entries.Keys))
            if (!active.Contains(binding)) { Release(entries[binding]); entries.Remove(binding); }
    }
    private IEnumerator Prepare(Entry entry)
    {
        while (entry.Card != null && !entry.Card.Released && !entry.Card.Ready && entry.Card.Error == null) yield return null;
        if (entry.Card == null || entry.Card.Released) yield break;
        if (entry.Card.Error != null) { Fail(entry, entry.Card.Error); yield break; }
        foreach (int variant in table.Variants(entry.Entity.EntityID))
        {
            yield return cards.factory.PreloadVariant(entry.Card, variant);
            if (!cards.factory.HasVariant(entry.Card, variant))
            { Fail(entry, $"敌人{entry.Entity.EntityID}差分{variant}加载失败。"); yield break; }
        }
        entry.Switcher = entry.Card.gameObject.AddComponent<UICardVariantSwitcher>();
        entry.Switcher.Bind(cards.factory, entry.Card, 1); entry.Loaded = true;
    }
    private void Fail(Entry entry, string message) { entry.Failed = true; RestoreVisibility(entry); Report(entry, message); }
    private void Report(Entry entry, string message)
    {
        if (entry.LastError == message) return;
        entry.LastError = message; Debug.LogError("BattleUiEnemyAppearance: " + message, this);
    }
    private void RestoreVisibility(Entry entry)
    {
        if (entry.Visibility == null) return;
        entry.Visibility.alpha = entry.Alpha;
        if (entry.OwnVisibility) Destroy(entry.Visibility);
        entry.Visibility = null;
    }
    private void Release(Entry entry)
    {
        if (entry.Loading != null) StopCoroutine(entry.Loading);
        if (entry.Switcher != null) { entry.Switcher.Cancel(); entry.Switcher.enabled = false; Destroy(entry.Switcher); }
        RestoreVisibility(entry);
    }
    private void Clear() { foreach (var entry in entries.Values) Release(entry); entries.Clear(); }
    private void OnDisable() { Clear(); table = null; }
}
