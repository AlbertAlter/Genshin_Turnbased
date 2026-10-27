using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-owned, cached runtime textures. Reads StreamingAssets only, never source artwork.</summary>
public sealed class HitTextureLibrary : IDisposable
{
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    public Material BodyMaterial { get; private set; }
    public Material LightMaterial { get; private set; }
    private bool disposed;
    public Sprite Get(string name) => string.IsNullOrEmpty(name) ? null : sprites[name];
    public IEnumerator Load(HitEffectTextureStyle style, Action<string> onError)
    {
        foreach (string file in style.Files)
        {
            if (disposed) yield break;
            if (sprites.ContainsKey(file)) continue;
            Sprite loaded = null; string failure = null;
            yield return StreamingAssetSpriteLoader.Load(HitEffectTextureStyle.ArtRoot + file, s => loaded = s, e => failure = e);
            if (failure != null) { onError?.Invoke(failure); yield break; }
            if (loaded == null) { onError?.Invoke("命中贴图为空：" + file); yield break; }
            if (disposed) { Destroy(loaded.texture); Destroy(loaded); yield break; }
            var texture = loaded.texture;
            RectInt bounds = HitTextureMath.AlphaBounds(texture.GetPixels32(), texture.width, texture.height);
            var cropped = Sprite.Create(texture, new Rect(bounds.x, bounds.y, bounds.width, bounds.height), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
            texture.Apply(false, true); // Alpha bounds are cached in Sprite.rect; release the large CPU pixel copy.
            cropped.name = file; Destroy(loaded); sprites.Add(file, cropped);
        }
        if (disposed || BodyMaterial != null) yield break;
        var shader = Resources.Load<Shader>("UI/HitTexture");
        if (shader == null) { onError?.Invoke("缺少 Resources/UI/HitTexture.shader。"); yield break; }
        BodyMaterial = new Material(shader) { name = "HitTexture_Alpha", hideFlags = HideFlags.HideAndDontSave };
        BodyMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        LightMaterial = new Material(shader) { name = "HitTexture_Additive", hideFlags = HideFlags.HideAndDontSave };
        LightMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var sprite in sprites.Values) { Destroy(sprite.texture); Destroy(sprite); }
        sprites.Clear(); Destroy(BodyMaterial); Destroy(LightMaterial); BodyMaterial = LightMaterial = null;
    }
    private static void Destroy(UnityEngine.Object item)
    { if (item != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item); } }
}
