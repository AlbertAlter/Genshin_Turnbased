using System.Collections.Generic;
using UnityEngine;

/// <summary>Instantiates an explicitly selected freeze prefab and binds each view to its actual card entity.</summary>
[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class BattleUiFrozenCards : MonoBehaviour
{
    public BattleUiAnimatedCards cards;
    public BattleInputController input;
    public UICardFreeze resources;
    public UIHitShakeController shake;
    public BattleUiFreezeInteraction cardPrefab;

    private sealed class Entry
    {
        public BattleEntity Entity;
        public UICardView Card;
        public BattleUiFreezeInteraction Interaction;
    }
    private readonly Dictionary<BattleUiCardBinding, Entry> entries = new Dictionary<BattleUiCardBinding, Entry>();
    private readonly HashSet<BattleUiCardBinding> active = new HashSet<BattleUiCardBinding>();
    private readonly List<BattleUiCardBinding> stale = new List<BattleUiCardBinding>();
    private bool reportedMissing;

    private void LateUpdate()
    {
        if (cards == null || input == null || resources == null || shake == null || cardPrefab == null)
        {
            if (!reportedMissing)
            { Debug.LogError("战斗冻结模块需要显式绑定卡牌、输入、冰层资源、公共震动和卡牌冻结Prefab。", this); reportedMissing = true; }
            Clear(); return;
        }
        reportedMissing = false;
        if (!cards.isActiveAndEnabled || cards.layout == null || cards.battle == null || !cards.battle.IsBattleRunning ||
            cards.factory == null || !cards.factory.isActiveAndEnabled || !resources.isActiveAndEnabled)
        { Clear(); return; }

        active.Clear();
        foreach (var binding in cards.layout.Cards)
        {
            if (binding?.root == null || binding.art == null) continue;
            var entity = cards.battle.GetEntityByPosition(binding.side, binding.slot);
            if (entity == null || !entity.IsAlive || !cards.TryGetCard(binding, out var card) || !card.Ready) continue;
            active.Add(binding);
            if (entries.TryGetValue(binding, out var entry) &&
                (entry.Entity != entity || entry.Card != card || entry.Interaction == null))
            { Release(entry); entries.Remove(binding); entry = null; }
            if (entry == null)
            {
                if (!resources.Ready || !FrozenReactionHandler.IsFrozen(entity)) continue;
                var instance = Instantiate(cardPrefab, card.Appearance.FreezeLayer);
                var source = instance.dataSource as BattleUiFreezeSource;
                if (source == null || instance.effect == null || instance.clickAudio == null || instance.hitArea == null)
                {
                    Debug.LogError("卡牌冻结Prefab缺少共享视图、真实数据适配器、点击命中区域或音效绑定。", this);
                    instance.gameObject.SetActive(false); Destroy(instance.gameObject); enabled = false; return;
                }
                try
                {
                    source.Bind(entity, cards.battle, input);
                    instance.shake = shake; instance.shakeRoot = binding.root;
                    instance.effect.UseResourcesFrom(resources); instance.effect.Bind(card);
                }
                catch (System.Exception error)
                {
                    instance.gameObject.SetActive(false); Destroy(instance.gameObject);
                    Debug.LogError("战斗冻结卡牌绑定失败：" + error.Message, this); enabled = false; return;
                }
                entry = new Entry { Entity = entity, Card = card, Interaction = instance };
                entries.Add(binding, entry);
            }
            // Keep the view after thaw so the final sound can finish and repeat freezes reuse the same module.
            // Only an entity/card replacement, death, battle end or module disable releases it.
            if (resources.Ready && !entry.Interaction.effect.SharesResourcesWith(resources))
                entry.Interaction.effect.UseResourcesFrom(resources);
            entry.Interaction.Refresh();
        }
        stale.Clear();
        foreach (var binding in entries.Keys) if (!active.Contains(binding)) stale.Add(binding);
        foreach (var binding in stale) { Release(entries[binding]); entries.Remove(binding); }
    }

    private void Release(Entry entry)
    {
        if (entry.Interaction == null) return;
        entry.Interaction.gameObject.SetActive(false);
        Destroy(entry.Interaction.gameObject);
    }
    private void Clear()
    { foreach (var entry in entries.Values) Release(entry); entries.Clear(); active.Clear(); stale.Clear(); }
    private void OnDisable() => Clear();
}
