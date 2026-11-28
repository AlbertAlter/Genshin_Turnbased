using System;
using System.Collections;
using UnityEngine;

/// <summary>One immutable original packed atlas and shared configuration; graphics only borrow them.</summary>
public sealed class UICardConductiveAssets : MonoBehaviour
{
    // Explicit serialized presets keep Lisa's existing atlas/UV behavior and share the same
    // resource lifetime, shader and parameters with a non-counted card-face talisman.
    public enum GlyphPreset { LisaConductive, QiqiTalisman }
    public GlyphPreset preset;
    public const string Folder = "LisaConductive";
    public const string AtlasPath = "art_assets/UI/CardFaceUI/LisaConductive/Eff_Symbol_11.png";
    public bool IsTalisman => preset == GlyphPreset.QiqiTalisman;
    public string FolderName => IsTalisman ? "QiqiTalisman" : Folder;
    public string ParameterFile => FolderName + ".xlsx";
    public string DisplayName => IsTalisman ? "七七 · 度厄真符" : "丽莎 · 引雷层数";
    public string SelectedAtlasPath => IsTalisman ? "art_assets/UI/CardFaceUI/QiqiTalisman/Talisman.png" : AtlasPath;
    public Sprite Atlas { get; private set; }
    public Shader Shader { get; private set; }
    public Color Tint { get; private set; }
    public Color CoreColor { get; private set; }
    public CardFaceConductiveTable Parameters { get; private set; }
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public event Action Invalidated, ParametersChanged;
    private int generation;
    private bool loading;
    public bool ReloadParameters()
    {
        try { var next = CardFaceConductiveTable.ReadPublished(ParameterFile); Parameters = next; Error = null; }
        catch (Exception failure) { Error = failure.Message; Debug.LogError(DisplayName + "配表：" + Error, this); return false; }
        ParametersChanged?.Invoke(); return true;
    }
    public IEnumerator Load(bool reload = false)
    {
        if (!isActiveAndEnabled) yield break;
        if (loading && !reload) { while (loading && isActiveAndEnabled) yield return null; yield break; }
        if (Ready && !reload) yield break;
        int stamp = ++generation; Release(); loading = true; Error = null;
        if (!ReloadParameters()) { loading = false; yield break; }
        try
        {
            var palette = ElementColorPalette.Load(); Tint = palette.GetColor(IsTalisman ? "Cryo" : "Electro"); CoreColor = palette.ProjectWhite;
            Shader = Resources.Load<Shader>("UI/CardFaceGlyph");
            if (Shader == null) throw new InvalidOperationException("缺少 UI/CardFaceGlyph Shader。");
        }
        catch (Exception failure) { Error = failure.Message; loading = false; Debug.LogError(Error, this); yield break; }
        yield return StreamingAssetSpriteLoader.Load(SelectedAtlasPath, sprite =>
        {
            if (this == null || !isActiveAndEnabled || stamp != generation) Dispose(sprite);
            else Atlas = sprite;
        }, error => { if (stamp == generation) Error = error; });
        if (!isActiveAndEnabled || stamp != generation) yield break;
        loading = false; Ready = Atlas != null && Error == null;
        if (!Ready) { Release(); Debug.LogError(DisplayName + "素材加载失败：" + Error, this); }
    }
    private void Release()
    {
        Ready = false; Invalidated?.Invoke(); Dispose(Atlas); Atlas = null; Shader = null; Parameters = null;
    }
    private static void Dispose(Sprite sprite)
    {
        if (sprite == null) return;
        var texture = sprite.texture;
        if (Application.isPlaying) { Destroy(sprite); if (texture != null) Destroy(texture); }
        else { DestroyImmediate(sprite); if (texture != null) DestroyImmediate(texture); }
    }
    private void OnDisable() { generation++; loading = false; Release(); }
}
