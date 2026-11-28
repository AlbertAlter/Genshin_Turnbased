using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One parameter in an effect's flat CardFaceUI workbook; preview scaling never changes stored units.</summary>
public sealed class CardFacePreviewParameter
{
    public readonly string Key, Label;
    public readonly float Min, Max, DisplayScale;
    public readonly bool Toggle;
    public CardFacePreviewParameter(string key, string label, float min, float max, float displayScale = 1, bool toggle = false)
    { Key = key; Label = label; Min = min; Max = max; DisplayScale = displayScale; Toggle = toggle; }
}

/// <summary>
/// Explicit preview plug-in for one CardFaceUI asset folder. A plug-in uses the same public renderer
/// as battle; the scene host only selects a plug-in, supplies cards and drives playback/tuning.
/// </summary>
public abstract class CardFacePreviewEffect : MonoBehaviour
{
    public abstract string FolderName { get; }
    public abstract string DisplayName { get; }
    public abstract string ParameterFile { get; }
    public abstract string Error { get; }
    public virtual int OwnerID => 0;
    public virtual string OwnerName => "公共";
    public virtual string SourceCategory => OwnerID == 0 ? "公共" : "角色";
    public virtual string EffectType => "目标标记";
    public virtual float PreviewDuration => 2.2f;
    public abstract IReadOnlyList<CardFacePreviewParameter> Parameters { get; }
    public abstract IEnumerator Load(bool reloadArt);
    public abstract bool Bind(IReadOnlyList<UICardFrameView> cards);
    public abstract Dictionary<string, float> ReadParameters();
    public abstract void ApplyParameters(IReadOnlyDictionary<string, float> values);
    public abstract bool ReloadConfiguration();
    public abstract string WriteParameters();
    public abstract void Show(int index);
    public abstract void Hide(int index, bool immediate);
    public abstract void Advance(float seconds);
    public abstract void Clear();
    // Effect-specific sample input (for example stacks) belongs to preview, never to the parameter workbook.
    public virtual void DrawSampleControls(Rect bounds, GUIStyle label, GUIStyle button) { }
}
