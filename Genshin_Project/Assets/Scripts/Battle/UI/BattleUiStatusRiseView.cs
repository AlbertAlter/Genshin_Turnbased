using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Visible, committed status grants only. A pooled graphic draws all streaks on one card.</summary>
public sealed class BattleUiStatusRiseView : MonoBehaviour, IBattlePresentationView
{
    public BattlefieldPresentationPlayer player;
    public BattleUiLayout layout;
    public BattleManager battle;
    private sealed class Burst
    {
        public long Playback;
        public float Age, Duration;
        public Action<Exception> Done;
        public readonly List<StatusRiseGraphic> Graphics = new List<StatusRiseGraphic>();
    }
    private readonly List<Burst> active = new List<Burst>();
    private readonly Stack<StatusRiseGraphic> pool = new Stack<StatusRiseGraphic>();
    private void OnEnable() { if (player != null) player.RegisterView(this); }
    public void Present(long id, BattlePresentationCue cue, Action<Exception> done)
    {
        if (!(cue.Result.Data is StatusEventData status) || status.Kind != BattleEventKind.StatusAdded ||
            status.Display != 1 || status.Phase <= 0 || layout == null || battle == null || !battle.IsBattleRunning ||
            player.ActionStyle == null || player.ActionStyle.ParticleSeconds <= 0) { done(null); return; }
        var burst = new Burst { Playback = id, Duration = player.ActionStyle.ParticleSeconds, Done = done };
        void Add(BattleUiCardBinding card)
        {
            var root = layout.HitLayer(card, false);
            var graphic = pool.Count > 0 ? pool.Pop() : new GameObject("StatusRise", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<StatusRiseGraphic>();
            var rect = graphic.rectTransform;
            rect.SetParent(root, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            graphic.raycastTarget = false; graphic.gameObject.SetActive(true);
            graphic.Seed = unchecked((uint)(cue.Result.Header.EventId * 31 + card.slot)); graphic.SetProgress(0);
            burst.Graphics.Add(graphic);
        }
        if (status.HostSide.HasValue && layout.TryGetCard(status.HostSide.Value, status.HostSlot, out var target)) Add(target);
        else if (status.Host is BattleTeamStatusHost team)
            foreach (var card in layout.Cards)
                if (card?.art != null && card.side == team.Side && battle.GetEntityByPosition(card.side, card.slot)?.IsOnField == true) Add(card);
        if (burst.Graphics.Count == 0) done(null); else active.Add(burst);
    }
    private void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var burst = active[i]; burst.Age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(burst.Age / burst.Duration);
            foreach (var graphic in burst.Graphics) if (graphic != null) graphic.SetProgress(t);
            if (t >= 1) Finish(i);
        }
    }
    private void Finish(int i)
    {
        var burst = active[i]; active.RemoveAt(i);
        foreach (var graphic in burst.Graphics)
        {
            if (graphic == null) continue;
            graphic.gameObject.SetActive(false); graphic.transform.SetParent(transform, false); pool.Push(graphic);
        }
        burst.Done(null);
    }
    public void Cancel(long id)
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (id < 0 || active[i].Playback == id) Finish(i);
    }
    private void OnDisable() { if (player != null) player.UnregisterView(this); Cancel(-1); }
    private void OnDestroy()
    {
        foreach (var graphic in pool) if (graphic != null) Destroy(graphic.gameObject);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class StatusRiseGraphic : MaskableGraphic
{
    internal uint Seed;
    private float progress;
    public void SetProgress(float t) { progress = t; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        for (int i = 0; i < 10; i++)
        {
            uint bits = HitEffectMath.Mix(Seed + (uint)i);
            float t = Mathf.Clamp01((progress - .19f * ((bits & 255) / 255f)) / .81f);
            float alpha = Mathf.Sin(Mathf.PI * t);
            if (alpha < .001f) continue;
            float x = rect.xMin + rect.width * (.08f + .84f * ((bits >> 8 & 255) / 255f));
            float y = rect.yMin + rect.height * (.04f + .48f * ((bits >> 16 & 255) / 255f) + .32f * t);
            float half = rect.width * .005f, length = rect.height * (.04f + .09f * ((bits >> 24) / 255f));
            Color tail = new Color(1, .88f, .65f, 0), tip = new Color(1, .96f, .83f, .8f * alpha);
            int v = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x-half,y), tail, Vector2.zero); mesh.AddVert(new Vector3(x+half,y), tail, Vector2.zero);
            mesh.AddVert(new Vector3(x+half,y+length), tip, Vector2.zero); mesh.AddVert(new Vector3(x-half,y+length), tip, Vector2.zero);
            mesh.AddTriangle(v,v+1,v+2); mesh.AddTriangle(v,v+2,v+3);
        }
    }
}
