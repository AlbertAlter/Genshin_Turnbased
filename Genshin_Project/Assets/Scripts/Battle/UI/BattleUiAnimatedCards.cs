using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Battle identity adapter only. All card appearance and media are owned by UICardFactory.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Card Requests")]
public sealed class BattleUiAnimatedCards : MonoBehaviour
{
    public BattleUiLayout layout;
    public BattleManager battle;
    public UICardFactory factory;
    public bool playEnemyDeath = true;
    private readonly Dictionary<Image, bool> proxyStates = new Dictionary<Image, bool>();
    // Migration-only references. Preserve old editor layouts without drawing a second skin in Play.
    public GameObject[] legacySkinObjects = new GameObject[0];
    private readonly Dictionary<GameObject, bool> legacyActive = new Dictionary<GameObject, bool>();
    private sealed class Card
    {
        public int ID; public BattleEntity Entity; public Image Proxy; public UICardView View;
        public UIGoldenDissolveCard Exit;
        public BattleUiCardBinding Binding; public RectTransform OwnedBehind, OwnedFront;
    }
    private readonly Dictionary<BattleUiCardBinding, Card> playing = new Dictionary<BattleUiCardBinding, Card>();
    public bool TryGetCard(BattleUiCardBinding binding, out UICardView view)
    {
        view = playing.TryGetValue(binding, out var card) && card.Exit == null ? card.View : null;
        return view != null && !view.Released;
    }
    private void OnEnable()
    {
        foreach (var node in legacySkinObjects)
            if (node != null && !legacyActive.ContainsKey(node))
            { legacyActive.Add(node, node.activeSelf); node.SetActive(false); }
    }
    private void LateUpdate()
    {
        if (layout == null || battle == null || factory == null || !factory.isActiveAndEnabled) return;
        foreach (var binding in layout.Cards)
        {
            if (binding?.art == null) continue;
            var entity = battle.GetEntityByPosition(binding.side, binding.slot);
            int id = entity != null && entity.IsAlive ? entity.EntityID : 0;
            playing.TryGetValue(binding, out var card);
            var proxy = card != null ? card.Proxy : binding.art.GetComponent<Image>();
            if (proxy != null)
            {
                if (!proxyStates.ContainsKey(proxy)) proxyStates.Add(proxy, proxy.enabled);
                proxy.enabled = false;
            }
            if (card != null && id == 0 && playEnemyDeath && binding.side == BattleSide.Enemy &&
                card.Entity != null && !card.Entity.IsAlive && card.View != null && card.View.Ready && !card.View.Released)
            {
                if (card.Exit == null)
                {
                    var appearance = card.View.Appearance;
                    card.Exit = card.View.gameObject.AddComponent<UIGoldenDissolveCard>();
                    card.Exit.cardRoot = appearance.Root;
                    card.Exit.framedView = appearance;
                    card.Exit.outerShadows = new[] { appearance.OuterShadow };
                    card.Exit.Play();
                }
                if (card.Exit.IsPlaying) continue;
            }
            if (card != null && (card.Exit != null || card.ID != id || card.Entity != entity || card.View == null || card.View.Released))
            { Release(card); playing.Remove(binding); card = null; }
            if (id == 0) continue;
            if (card == null)
            {
                card = new Card { ID = id, Entity = entity, Proxy = proxy, Binding = binding,
                    View = factory.Request(binding.art, binding.side, id) };
                playing.Add(binding, card);
            }
            card.View.SetAnimationFrozen(this, FrozenReactionHandler.IsFrozen(entity));
            if (card.View.Ready)
            {
                binding.shieldLayer = card.View.Appearance.ShieldLayer;
                if (binding.hitsBehind == null) binding.hitsBehind = card.OwnedBehind = card.View.Appearance.BackEffects;
                if (binding.hitsFront == null) binding.hitsFront = card.OwnedFront = card.View.Appearance.FrontEffects;
            }
            if (card.Proxy != null)
            {
                card.View.SetTint(card.Proxy.color);
            }
        }
    }
    private void Release(Card card)
    {
        card.Binding.shieldLayer = null;
        if (card.Binding.hitsBehind == card.OwnedBehind) card.Binding.hitsBehind = null;
        if (card.Binding.hitsFront == card.OwnedFront) card.Binding.hitsFront = null;
        if (factory != null) factory.Release(card.View);
    }
    private void OnDisable()
    {
        foreach (var card in playing.Values) Release(card);
        playing.Clear();
        foreach (var pair in proxyStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        proxyStates.Clear();
        foreach (var pair in legacyActive) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        legacyActive.Clear();
    }
}
