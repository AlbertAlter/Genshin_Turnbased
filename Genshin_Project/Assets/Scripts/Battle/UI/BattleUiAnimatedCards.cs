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
    // Migration-only references. Preserve old editor layouts without drawing a second skin in Play.
    public GameObject[] legacySkinObjects = new GameObject[0];
    private readonly Dictionary<GameObject, bool> legacyActive = new Dictionary<GameObject, bool>();
    private sealed class Card
    {
        public int ID; public BattleEntity Entity; public Image Proxy; public bool WasEnabled; public UICardView View;
        public BattleUiCardBinding Binding; public RectTransform OwnedBehind, OwnedFront;
    }
    private readonly Dictionary<BattleUiCardBinding, Card> playing = new Dictionary<BattleUiCardBinding, Card>();
    public bool TryGetCard(BattleUiCardBinding binding, out UICardView view)
    {
        view = playing.TryGetValue(binding, out var card) ? card.View : null;
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
            if (playing.TryGetValue(binding, out var card) && (card.ID != id || card.Entity != entity || card.View == null || card.View.Released))
            { Release(card); playing.Remove(binding); card = null; }
            if (id == 0) continue;
            if (card == null)
            {
                var proxy = binding.art.GetComponent<Image>();
                card = new Card { ID = id, Entity = entity, Proxy = proxy, Binding = binding, WasEnabled = proxy != null && proxy.enabled,
                    View = factory.Request(binding.art, binding.side, id) };
                playing.Add(binding, card);
            }
            card.View.SetAnimationFrozen(this, FrozenReactionHandler.IsFrozen(entity));
            if (card.View.Ready)
            {
                if (binding.hitsBehind == null) binding.hitsBehind = card.OwnedBehind = card.View.Appearance.BackEffects;
                if (binding.hitsFront == null) binding.hitsFront = card.OwnedFront = card.View.Appearance.FrontEffects;
            }
            if (card.Proxy != null)
            {
                card.View.SetTint(card.Proxy.color);
                card.Proxy.enabled = false;
            }
        }
    }
    private void Release(Card card)
    {
        if (card.Binding.hitsBehind == card.OwnedBehind) card.Binding.hitsBehind = null;
        if (card.Binding.hitsFront == card.OwnedFront) card.Binding.hitsFront = null;
        if (factory != null) factory.Release(card.View);
        if (card.Proxy != null) card.Proxy.enabled = card.WasEnabled;
    }
    private void OnDisable()
    {
        foreach (var card in playing.Values) Release(card);
        playing.Clear();
        foreach (var pair in legacyActive) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        legacyActive.Clear();
    }
}
