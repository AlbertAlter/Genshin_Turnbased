using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Loads the shared artwork used by a wish view and its child pages.</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeWishArt : MonoBehaviour
{
    internal static readonly Color32 DarkBrown = new Color32(113, 103, 88, 255);
    internal static readonly Color32 LightText = new Color32(250, 240, 219, 255);

    [Serializable]
    private struct Binding
    {
        public Image image;
        public string file;
        public int border;
        public bool keepTint;
    }

    [SerializeField] private Binding[] images = Array.Empty<Binding>();
    private readonly Dictionary<(string, int), Sprite> sprites = new Dictionary<(string, int), Sprite>();
    private readonly Dictionary<(string, int), List<Action<Sprite>>> loading = new Dictionary<(string, int), List<Action<Sprite>>>();

    private void OnEnable()
    {
        foreach (var binding in images)
            Apply(binding.image, binding.file, binding.border, binding.keepTint);
    }

    internal void Apply(Image image, string file, int border = 0, bool keepTint = false)
    {
        if (image == null) return;
        Load(file, border, sprite =>
        {
            if (image == null) return;
            image.sprite = sprite;
            image.type = border > 0 ? Image.Type.Sliced : Image.Type.Simple;
            if (!keepTint) image.color = Color.white;
            image.enabled = true;
        });
    }

    internal void Load(string file, int border, Action<Sprite> apply)
    {
        var key = (file, border);
        if (sprites.TryGetValue(key, out var sprite))
        {
            apply(sprite);
            return;
        }
        if (loading.TryGetValue(key, out var callbacks))
        {
            callbacks.Add(apply);
            return;
        }
        loading.Add(key, new List<Action<Sprite>> { apply });
        StartCoroutine(StreamingAssetSpriteLoader.Load("art_assets/UI/Wish/" + file + ".png", source =>
        {
            if (border > 0)
            {
                var margin = Vector4.one * border;
                sprite = Sprite.Create(source.texture, source.rect, new Vector2(.5f, .5f),
                    400f, 0u, SpriteMeshType.FullRect, margin);
                Destroy(source);
            }
            else
            {
                sprite = source;
            }
            sprites.Add(key, sprite);
            var ready = loading[key];
            loading.Remove(key);
            foreach (var callback in ready) callback(sprite);
        }, _ => loading.Remove(key)));
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        loading.Clear();
    }

    private void OnDestroy()
    {
        foreach (var sprite in sprites.Values)
        {
            Destroy(sprite.texture);
            Destroy(sprite);
        }
    }
}
