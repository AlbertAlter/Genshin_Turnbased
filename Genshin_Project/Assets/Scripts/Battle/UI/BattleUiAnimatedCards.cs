using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Battle identity adapter only. All card appearance and media are owned by UICardFactory.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Card Requests")]
public sealed class BattleUiAnimatedCards : MonoBehaviour, IBattleEnemyExitView, IBattleVisualCompletion
{
    public BattleUiLayout layout;
    public BattleManager battle;
    public UICardFactory factory;
    public bool playEnemyDeath = true;
    private BattlefieldPresentationPlayer player;
    private readonly Dictionary<Image, bool> proxyStates = new Dictionary<Image, bool>();
    // Migration-only references. Preserve old editor layouts without drawing a second skin in Play.
    public GameObject[] legacySkinObjects = new GameObject[0];
    private readonly Dictionary<GameObject, bool> legacyActive = new Dictionary<GameObject, bool>();
    private sealed class Card
    {
        public int ID; public BattleEntity Entity; public Image Proxy; public UICardView View;
        public UIGoldenDissolveCard Exit;
        public long ExitPlayback;
        public Action<Exception> ExitCompleted;
        public BattleUiCardBinding Binding; public RectTransform OwnedBehind, OwnedFront;
    }
    private readonly Dictionary<BattleUiCardBinding, Card> playing = new Dictionary<BattleUiCardBinding, Card>();
    public bool HasPendingVisuals
    {
        get { foreach (var card in playing.Values) if (card.ExitCompleted != null ||
            (card.Exit != null && card.Exit.isActiveAndEnabled && card.Exit.IsPlaying)) return true; return false; }
    }
    public bool TryGetCard(BattleUiCardBinding binding, out UICardView view)
    {
        view = playing.TryGetValue(binding, out var card) && card.Exit == null ? card.View : null;
        return view != null && !view.Released;
    }
    public bool TryGetCard(BattleUiCardBinding binding, BattleEntity expected, out UICardView view)
    {
        if (expected != null && (!playing.TryGetValue(binding, out var card) || card.Entity != expected))
        { view = null; return false; }
        return TryGetCard(binding, out view);
    }
    private void OnEnable()
    {
        player = GetComponent<BattlefieldPresentationPlayer>();
        player?.RegisterView(this);
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
                card.Entity != null && !card.Entity.IsAlive && (entity == null || entity == card.Entity))
            {
                if (card.Exit == null && card.View != null && !card.View.Released)
                {
                    if (player != null && player.isActiveAndEnabled)
                    {
                        if (BattleExecution.IsBusy || player.HasPendingResults) continue;
                    }
                    else if (card.View.Ready) StartExit(card);
                }
                if (card.Exit != null && card.Exit.IsPlaying && card.Exit.isActiveAndEnabled) continue;
            }
            if (card != null && (card.Exit != null || card.ID != id || card.Entity != entity || card.View == null || card.View.Released))
            { playing.Remove(binding); Release(card); card = null; }
            if (id == 0) continue;
            if (card == null)
            {
                card = new Card { ID = id, Entity = entity, Proxy = proxy, Binding = binding,
                    View = factory.Request(binding.art, binding.side, id) };
                playing.Add(binding, card);
            }
            // Actual control owns the pause, independently of the currently visible ice/stars.
            card.View.SetAnimationFrozen(this, FrozenReactionHandler.IsFrozen(entity)
                || AllyControlRelease.GetOldest(entity).Exists || BattleUiFreezeSource.IsStunned(entity));
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
    private static bool StartExit(Card card)
    {
        var appearance = card.View.Appearance;
        card.Exit = card.View.gameObject.AddComponent<UIGoldenDissolveCard>();
        card.Exit.cardRoot = appearance.Root;
        card.Exit.framedView = appearance;
        card.Exit.outerShadows = new[] { appearance.OuterShadow };
        return card.Exit.Play();
    }
    public bool CanPresentExit(BattlePresentationCue cue) => TryGetExitCard(cue, out _);
    private bool TryGetExitCard(BattlePresentationCue cue, out Card card)
    {
        card = null;
        var death = cue.Result;
        if (!isActiveAndEnabled || !playEnemyDeath || battle == null || layout == null || factory == null ||
            !factory.isActiveAndEnabled || !layout.TryGetCard(BattleSide.Enemy, death.TargetSlot, out var binding) ||
            !playing.TryGetValue(binding, out card) || card.Entity == null || card.Entity != death.Data.Subject ||
            card.Entity.IsAlive || card.View == null || !card.View.Ready || card.View.Released ||
            !card.View.gameObject.activeInHierarchy || card.Exit != null) return false;
        var occupant = battle.GetEntityByPosition(BattleSide.Enemy, death.TargetSlot);
        return occupant == null || occupant == card.Entity;
    }
    public void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        if (TryGetExitCard(cue, out var card) && StartExit(card))
        {
            card.ExitPlayback = playbackID;
            card.ExitCompleted = completed;
            StartCoroutine(WaitForExit(card));
            return;
        }
        completed(null);
    }
    private IEnumerator WaitForExit(Card card)
    {
        while (card.ExitCompleted != null && card.Exit != null && card.Exit.isActiveAndEnabled && card.Exit.IsPlaying &&
            playEnemyDeath && layout != null && battle != null && factory != null && factory.isActiveAndEnabled)
            yield return null;
        CompleteExit(card);
    }
    private static void CompleteExit(Card card)
    {
        var done = card.ExitCompleted;
        card.ExitCompleted = null;
        done?.Invoke(null);
    }
    public void Cancel(long playbackID)
    {
        foreach (var card in playing.Values)
            if (card.Exit != null && (playbackID < 0 || card.ExitPlayback == playbackID))
            {
                card.Exit.ResetEffect();
                card.ExitCompleted = null; // The player closes this ticket on cancellation.
            }
    }
    private void Release(Card card)
    {
        card.Binding.shieldLayer = null;
        if (card.Binding.hitsBehind == card.OwnedBehind) card.Binding.hitsBehind = null;
        if (card.Binding.hitsFront == card.OwnedFront) card.Binding.hitsFront = null;
        if (factory != null) factory.Release(card.View);
        CompleteExit(card);
    }
    private void OnDisable()
    {
        player?.UnregisterView(this);
        foreach (var card in playing.Values) Release(card);
        playing.Clear();
        foreach (var pair in proxyStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        proxyStates.Clear();
        foreach (var pair in legacyActive) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        legacyActive.Clear();
    }
}
