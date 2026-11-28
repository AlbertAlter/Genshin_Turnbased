using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit shared resource owner for the card-face targeting overlay.</summary>
[DisallowMultipleComponent]
public sealed class UICardTargetAssets : MonoBehaviour
{
    public const string ArtFolder = "art_assets/UI/CardFaceUI/Targeted/";
    public static readonly string[] TextureFiles =
        { "Ring.png", "Top.png", "Right.png", "Bottom.png", "Left.png", "Silhouette.png" };
    private readonly Sprite[] sprites = new Sprite[TextureFiles.Length];
    private int generation;
    private bool loading;
    private byte[] sourceAlpha;
    private Material glowMaterial;
    private readonly Dictionary<long, GlowEntry> glowCache = new Dictionary<long, GlowEntry>();
    internal sealed class GlowEntry
    {
        public long Key;
        public Texture2D Texture;
        public int References;
    }
    public sealed class GlowHandle : IDisposable
    {
        private UICardTargetAssets owner;
        private GlowEntry entry;
        public Texture2D Texture => entry?.Texture;
        public Material Material => owner != null ? owner.glowMaterial : null;
        internal GlowHandle(UICardTargetAssets owner, GlowEntry entry) { this.owner = owner; this.entry = entry; }
        public void Dispose()
        {
            if (entry == null) return;
            if (owner != null) owner.ReleaseGlow(entry);
            entry = null; owner = null;
        }
    }
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public CardTargetIndicatorTable Parameters { get; private set; }
    public event Action Invalidated;
    public event Action ParametersChanged;
    public Sprite GetSprite(int index) => Ready && index >= 0 && index < sprites.Length ? sprites[index] : null;

    public bool ReloadParameters()
    {
        try { Parameters = CardTargetIndicatorTable.ReadPublished(); Error = null; }
        catch (Exception error)
        {
            Error = "卡面准心配表读取失败：" + error.Message;
            Debug.LogError(Error, this); return false;
        }
        ParametersChanged?.Invoke();
        return true;
    }

    public GlowHandle AcquireGlow(int size, int spreadPercent)
    {
        if (!Ready) throw new InvalidOperationException("准心 Alpha 资源尚未就绪。");
        size = Math.Max(0, size); spreadPercent = Math.Max(0, Math.Min(100, spreadPercent));
        long key = ((long)size << 32) | (uint)spreadPercent;
        if (!glowCache.TryGetValue(key, out var entry))
        {
            if (glowMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("UI/AlphaGlow");
                if (shader == null) throw new InvalidOperationException("缺少公共 UI AlphaGlow 着色器。");
                glowMaterial = new Material(shader) { name = "CardTargetAlphaGlow", hideFlags = HideFlags.HideAndDontSave };
            }
            Texture2D source = sprites[5].texture;
            if (sourceAlpha == null)
            {
                Color32[] pixels = source.GetPixels32(); sourceAlpha = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) sourceAlpha[i] = pixels[i].a;
            }
            var mask = UIAlphaGlowMask.Build(sourceAlpha, source.width, source.height, size, spreadPercent);
            // One coverage byte per texel, with no sprite RGB, alpha splitting or color-space conversion.
            var texture = new Texture2D(mask.Width, mask.Height, TextureFormat.R8, false, true)
            { name = "CardTargetGlowAlpha", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.LoadRawTextureData(mask.Alpha); texture.Apply(false, true);
            entry = new GlowEntry { Key = key, Texture = texture }; glowCache.Add(key, entry);
        }
        entry.References++;
        return new GlowHandle(this, entry);
    }
    private void ReleaseGlow(GlowEntry entry)
    {
        if (--entry.References > 0) return;
        if (glowCache.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) glowCache.Remove(entry.Key);
        DisposeObject(entry.Texture); entry.Texture = null;
    }

    public IEnumerator Load(bool reload = false)
    {
        if (!isActiveAndEnabled) yield break;
        if (loading && !reload)
        {
            while (loading && isActiveAndEnabled) yield return null;
            yield break;
        }
        if (Ready && !reload) yield break;
        int request = ++generation;
        ReleaseAssets(); Error = null; loading = true;
        if (!ReloadParameters()) { loading = false; yield break; }
        for (int i = 0; i < sprites.Length; i++)
        {
            int index = i;
            yield return StreamingAssetSpriteLoader.Load(ArtFolder + TextureFiles[i], sprite =>
            {
                if (this == null || !isActiveAndEnabled || request != generation) Dispose(sprite);
                else sprites[index] = sprite;
            }, error => { if (request == generation) Error = error; });
            if (!isActiveAndEnabled || request != generation) yield break;
            if (Error != null)
            {
                loading = false; ReleaseAssets();
                Debug.LogError("卡面准心资源加载失败：" + Error, this); yield break;
            }
        }
        loading = false; Ready = true;
    }

    private void ReleaseAssets()
    {
        Ready = false;
        Invalidated?.Invoke(); // Views stop borrowing before textures are released.
        foreach (var entry in glowCache.Values) { DisposeObject(entry.Texture); entry.Texture = null; }
        glowCache.Clear(); sourceAlpha = null;
        Parameters = null;
        DisposeObject(glowMaterial); glowMaterial = null;
        for (int i = 0; i < sprites.Length; i++) { Dispose(sprites[i]); sprites[i] = null; }
    }
    private static void Dispose(Sprite sprite)
    {
        if (sprite == null) return;
        Texture2D texture = sprite.texture;
        if (Application.isPlaying) { Destroy(sprite); if (texture != null) Destroy(texture); }
        else { DestroyImmediate(sprite); if (texture != null) DestroyImmediate(texture); }
    }
    private static void DisposeObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
    private void OnDisable() { generation++; loading = false; ReleaseAssets(); }
}
