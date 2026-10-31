using System.Collections;
using UnityEngine;

/// <summary>Serialized independent PSD layers; supports preview and runtime without a battle.</summary>
[ExecuteAlways]
public sealed class PsdUiLayerStack : MonoBehaviour
{
    public PsdHealthLayer[] layers = new PsdHealthLayer[0];
    public int baseIndex;
    private Sprite[] sprites;
    private Coroutine loading;
    public void SetBaseColor(Color color)
    {
        foreach (var layer in layers) if (layer != null) layer.SetBaseColor(color);
    }
    private void OnEnable()
    {
        if (layers.Length == 0 || baseIndex < 0 || baseIndex >= layers.Length) return;
        sprites = new Sprite[layers.Length];
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null || !StreamingAssetSpriteLoader.TryBuildRequestUrl(layers[i].relativePath, out string url)) return;
                var uri = new System.Uri(url);
                if (!uri.IsFile || !System.IO.File.Exists(uri.LocalPath))
                { Debug.LogError("Missing PSD layer: " + layers[i].relativePath, this); return; }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(System.IO.File.ReadAllBytes(uri.LocalPath))) { DestroyImmediate(texture); return; }
                texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
                sprites[i] = Sprite.Create(texture, new Rect(0,0,texture.width,texture.height), new Vector2(.5f,.5f));
                sprites[i].hideFlags = HideFlags.HideAndDontSave;
            }
            BindAll(); return;
        }
#endif
        loading = StartCoroutine(Load());
    }
    private IEnumerator Load()
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) yield break;
            int index = i;
            yield return StreamingAssetSpriteLoader.Load(layers[i].relativePath, s => sprites[index] = s,
                error => Debug.LogError(error, this));
            if (sprites[i] == null) yield break;
        }
        BindAll(); loading = null;
    }
    private void BindAll()
    {
        var shader = Resources.Load<Shader>("UI/PsdHealthLayer");
        if (shader == null) { Debug.LogError("Missing UI/PsdHealthLayer shader.", this); return; }
        // Shield soft lights precede its inner shadow, so their backdrop is the base alone.
        for (int i = 0; i < layers.Length; i++)
            layers[i].Bind(sprites[i], sprites[baseIndex].texture, shader, null, null, Vector4.zero);
    }
    private void OnDisable()
    {
        if (loading != null) StopCoroutine(loading); loading = null;
        if (sprites != null) foreach (var sprite in sprites)
        {
            if (sprite == null) continue;
            if (Application.isPlaying) { Destroy(sprite.texture); Destroy(sprite); }
            else { DestroyImmediate(sprite.texture); DestroyImmediate(sprite); }
        }
        sprites = null;
    }
}
