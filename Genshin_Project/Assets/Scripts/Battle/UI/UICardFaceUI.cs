using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A card-face visual implements only its own presentation and resource cleanup.</summary>
public interface IUICardFaceEffect
{
    bool Ready { get; }
    bool Visible { get; }
    bool Show();
    void Hide(bool immediate = false);
    void Advance(float seconds);
    void Clear();
}

/// <summary>
/// Independent card-face display host, reusable outside battles. Its caller registers visual factories,
/// supplies the desired UI types and drives the clock. It never knows about status bars or battle rules.
/// </summary>
public sealed class UICardFaceUI : IDisposable
{
    public delegate IUICardFaceEffect Factory(RectTransform cardArea, RectTransform mount);
    public sealed class Registration
    {
        public readonly Factory Create;
        public readonly Func<bool> IsReady;
        public Registration(Factory create, Func<bool> isReady = null)
        { Create = create ?? throw new ArgumentNullException(nameof(create)); IsReady = isReady; }
    }
    private sealed class Entry
    {
        public RectTransform Root;
        public IUICardFaceEffect Effect;
        public int Layer;
    }
    private readonly RectTransform cardArea, mount;
    private readonly Dictionary<string, Registration> factories;
    private readonly Dictionary<string, Entry> effects = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private readonly List<string> removal = new List<string>();
    private RectTransform root;
    public UICardFaceUI(RectTransform cardArea, RectTransform mount, Dictionary<string, Registration> factories)
    { this.cardArea = cardArea; this.mount = mount; this.factories = factories ?? throw new ArgumentNullException(nameof(factories)); }

    public void Synchronize(IDictionary<string, CardFaceStatusTable.Rule> desired)
    {
        if (cardArea == null || mount == null) { Dispose(); return; }
        bool reorder = false;
        foreach (var pair in desired)
        {
            if (!effects.TryGetValue(pair.Key, out var entry))
            {
                if (!factories.TryGetValue(pair.Key, out var registration)
                    || (registration.IsReady != null && !registration.IsReady())) continue;
                if (root == null) root = CreateMount("CardFaceUI", mount);
                AlignArea();
                var effectMount = CreateMount(pair.Key, root);
                IUICardFaceEffect visual;
                try { visual = registration.Create(cardArea, effectMount); }
                catch { Destroy(effectMount); throw; }
                if (visual == null) { Destroy(effectMount); continue; }
                entry = new Entry { Root = effectMount, Effect = visual, Layer = pair.Value.Layer };
                effects.Add(pair.Key, entry); reorder = true;
            }
            if (entry.Layer != pair.Value.Layer) { entry.Layer = pair.Value.Layer; reorder = true; }
            entry.Effect.Show(); // Idempotent: refreshing a status does not restart its enter animation.
        }
        foreach (var pair in effects)
            if (!desired.ContainsKey(pair.Key)) pair.Value.Effect.Hide();
        AlignArea();
        if (reorder)
        {
            var order = new List<KeyValuePair<string, Entry>>(effects);
            order.Sort((a, b) => a.Value.Layer != b.Value.Layer ? a.Value.Layer.CompareTo(b.Value.Layer)
                : string.CompareOrdinal(a.Key, b.Key));
            foreach (var pair in order) pair.Value.Root.SetAsLastSibling();
        }
    }

    public void Advance(float seconds)
    {
        AlignArea();
        removal.Clear();
        foreach (var pair in effects)
        {
            pair.Value.Effect.Advance(seconds);
            if (!pair.Value.Effect.Ready || !pair.Value.Effect.Visible) removal.Add(pair.Key);
        }
        foreach (string key in removal) { Release(effects[key]); effects.Remove(key); }
    }
    public void Dispose()
    {
        foreach (var entry in effects.Values) Release(entry);
        effects.Clear(); removal.Clear(); Destroy(root); root = null;
    }
    private static void Release(Entry entry) { entry.Effect.Clear(); Destroy(entry.Root); }
    private void AlignArea()
    {
        if (root == null || cardArea == null || mount == null) return;
        // The visual layer can live outside a hidden/empty card group. Only its display geometry
        // follows the authored card area; no scene layout transform or active flag is changed.
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        root.position = cardArea.TransformPoint(cardArea.rect.center);
        root.rotation = cardArea.rotation;
        root.sizeDelta = cardArea.rect.size;
        Vector3 source = cardArea.lossyScale, parent = mount.lossyScale;
        root.localScale = new Vector3(parent.x == 0 ? 1 : source.x / parent.x,
            parent.y == 0 ? 1 : source.y / parent.y, parent.z == 0 ? 1 : source.z / parent.z);
    }
    private static RectTransform CreateMount(string name, RectTransform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var group = rt.GetComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
        return rt;
    }
    private static void Destroy(RectTransform owned)
    {
        if (owned == null) return;
        owned.gameObject.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(owned.gameObject);
        else UnityEngine.Object.DestroyImmediate(owned.gameObject);
    }
}
