using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone visual-only dissolve for one UI Image/RawImage. No gameplay random draws.
/// Keep this visual object alive after removing the battle entity, then call Play when its effect ends.
/// Does not destroy/disable the Image, remove the entity, or register with the battle player automatically.
/// The material replaces this Image's current shader while active; ResetEffect restores that material.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class UIGoldenDissolve : MonoBehaviour
{
    private const string ResourcePath = "UI/GoldenDissolve";
    [SerializeField] private Shader shader;

    private Graphic image;
    private Material ownedMaterial, previousMaterial;
    private Action completed;
    private float elapsed, playbackDuration;
    private Vector4 noiseOffset;
    private static uint visualSequence;
    private UIGoldenDissolveStyle configuration, playbackStyle;
    private Rect? sourceBounds;
    public bool InputIsPremultiplied { get; set; }

    public bool IsPlaying { get; private set; }
    public float Progress { get; private set; }
    public int Seed { get; private set; }

    /// <summary>Inject an already-loaded runtime settings snapshot, e.g. from the full-card player.</summary>
    public void Configure(UIGoldenDissolveStyle style) => configuration = style ?? throw new ArgumentNullException(nameof(style));

    /// <summary>Original card bounds inside an expanded RawImage; null restores ordinary Image coordinates.</summary>
    public void SetSourceBounds(Rect? bounds) => sourceBounds = bounds;

    /// <summary>New visual seed on each call, independent of BattleRandom. Returns false if unable to start.</summary>
    public bool Play(Action onCompleted = null)
    {
        uint value;
        unchecked { value = ++visualSequence ^ (uint)GetInstanceID() * 0x9E3779B9u; }
        return Play(unchecked((int)Mix(value)), onCompleted);
    }

    /// <summary>Same seed/settings give the same pattern. Replaying replaces the old callback without firing it.</summary>
    public bool Play(int seed, Action onCompleted = null)
    {
        if (!PrepareMaterial()) return false;
        playbackStyle = configuration;
        completed = onCompleted;
        Seed = seed; noiseOffset = OffsetForSeed(seed);
        elapsed = 0;
        playbackDuration = playbackStyle.DurationSeconds;
        IsPlaying = true;
        ApplyProgress(0);
        if (playbackDuration == 0) Finish();
        return true;
    }

    /// <summary>Manual progress for an external animator/scrubber. Stops automatic playback; does not fire completion.</summary>
    public bool SetProgress(float progress, int seed)
    {
        if (float.IsNaN(progress) || float.IsInfinity(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        if (!PrepareMaterial()) return false;
        playbackStyle = configuration;
        IsPlaying = false; completed = null;
        Seed = seed; noiseOffset = OffsetForSeed(seed);
        ApplyProgress(Mathf.Clamp01(progress));
        return true;
    }

    private void Update()
    {
        if (!IsPlaying) return;
        if (image == null || image.material != ownedMaterial) { ResetEffect(); return; }
        elapsed += Mathf.Max(0, playbackStyle.UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        if (elapsed >= playbackDuration) Finish();
        else ApplyProgress(elapsed / playbackDuration);
    }

    private void Finish()
    {
        ApplyProgress(1);
        IsPlaying = false;
        var callback = completed; completed = null;
        // Leave the Image fully dissolved until the owner resets/recycles/removes the visual object.
        callback?.Invoke();
    }

    /// <summary>Cancel without invoking completion; restore the prior material. Safe for pooled Images.</summary>
    public void ResetEffect()
    {
        IsPlaying = false; completed = null; Progress = 0; elapsed = 0;
        ReleaseMaterial();
        playbackStyle = null;
    }

    /// <summary>Reload the runtime table for the next playback; an active playback keeps its settings snapshot.</summary>
    public bool ReloadConfiguration()
    {
        try { configuration = UIGoldenDissolveStyle.Load(); return true; }
        catch (Exception error)
        {
            Debug.LogError("Golden dissolve configuration could not be loaded: " + error.Message, this);
            return false;
        }
    }

    private bool PrepareMaterial()
    {
        if (!isActiveAndEnabled) return false;
        if (configuration == null && !ReloadConfiguration()) return false;
        if (image == null) image = GetComponent<Graphic>();
        if (image == null) return false;
        if (ownedMaterial != null && image.material == ownedMaterial) return true;
        ReleaseMaterial();
        if (shader == null) shader = Resources.Load<Shader>(ResourcePath);
        if (shader == null)
        {
            Debug.LogError("UI golden dissolve shader is missing: Resources/" + ResourcePath, this);
            return false;
        }
        previousMaterial = image.material;
        ownedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        image.material = ownedMaterial;
        image.SetMaterialDirty();
        return true;
    }

    private void ApplyProgress(float value)
    {
        Progress = value;
        if (image == null || ownedMaterial == null) return;
        Rect rect = sourceBounds ?? image.GetPixelAdjustedRect();
        Vector4 bounds = new Vector4(rect.xMin, rect.yMin, Mathf.Max(.001f, rect.width), Mathf.Max(.001f, rect.height));
        var dir = playbackStyle.Direction;
        void Apply(Material material)
        {
            material.SetFloat("_Progress", value);
            material.SetFloat("_InputPremultiplied", InputIsPremultiplied ? 1 : 0);
            material.SetFloat("_UseSourceBounds", sourceBounds.HasValue ? 1 : 0);
            material.SetVector("_EffectRect", bounds);
            material.SetVector("_NoiseOffset", noiseOffset);
            material.SetFloat("_NoiseScale", playbackStyle.NoiseScale);
            material.SetFloat("_EdgeWidth", playbackStyle.EdgeWidth);
            material.SetFloat("_EdgeSoftness", playbackStyle.EdgeSoftness);
            material.SetColor("_EdgeColor", playbackStyle.EdgeColor);
            material.SetColor("_EdgeHighlight", playbackStyle.EdgeHighlight);
            material.SetVector("_Direction", new Vector4(dir.x, dir.y, 0, 0));
            material.SetFloat("_DirectionWeight", dir.sqrMagnitude > 0 ? playbackStyle.DirectionWeight : 0);
        }
        Apply(ownedMaterial);
        // MaskableGraphic can render a stencil-derived material rather than the assigned instance.
        var rendering = image.materialForRendering;
        if (rendering != null && rendering != ownedMaterial && rendering.shader == ownedMaterial.shader) Apply(rendering);
    }

    private void ReleaseMaterial()
    {
        if (image != null && ownedMaterial != null && image.material == ownedMaterial)
        {
            image.material = previousMaterial;
            image.SetMaterialDirty();
        }
        if (ownedMaterial != null)
        {
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
        ownedMaterial = null; previousMaterial = null;
    }

    private void OnDisable() => ResetEffect();
    private void OnDestroy() => ReleaseMaterial();

    // Pure visual seed expansion. Never calls a gameplay random source or creates a texture.
    private static Vector4 OffsetForSeed(int seed)
    {
        return GoldenDissolveMath.OffsetForSeed(seed);
    }

    private static uint Mix(uint value)
    {
        unchecked { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; return value ^ (value >> 16); }
    }
}
