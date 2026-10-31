using UnityEngine;
using System.Collections.Generic;

/// <summary>One card's public visual handle. Appearance, masking and video fallback are shared by every scene.</summary>
public sealed class UICardView : MonoBehaviour
{
    public UICardFrameView Appearance { get; private set; }
    public UICardVideoPlayer Video { get; private set; }
    public bool Ready => Appearance != null && Appearance.Surface != null;
    public bool Released { get; private set; }
    public bool MediaResolved { get; internal set; }
    public string Error { get; private set; }
    private Color tint = Color.white;
    private readonly HashSet<object> freezeOwners = new HashSet<object>();
    public bool AnimationFrozen => freezeOwners.Count != 0;
    public void SetAnimationFrozen(object owner, bool frozen)
    {
        if (owner == null) throw new System.ArgumentNullException(nameof(owner));
        if (Released) return;
        bool changed = frozen ? freezeOwners.Add(owner) : freezeOwners.Remove(owner);
        if (changed && Video != null) Video.SetFrozen(AnimationFrozen);
    }
    internal void Initialize(Sprite frame, Sprite inner, Sprite shadow, Sprite glow, Texture texture)
    {
        Appearance = gameObject.AddComponent<UICardFrameView>();
        Appearance.Initialize(frame, inner, shadow, glow, texture, false);
        Appearance.SetTint(tint);
    }
    internal void PlayVideo(string path)
    {
        Video = gameObject.AddComponent<UICardVideoPlayer>(); Video.playOnStart = true;
        Video.videoPath = path; Video.targets = new[] { Appearance.Surface };
        Video.SetFrozen(AnimationFrozen);
        if (Video.isActiveAndEnabled) Video.Load();
    }
    public void SetTint(Color color) { tint = color; if (Appearance != null) Appearance.SetTint(color); }
    internal void SetStaticVariant(Texture texture)
    {
        if (Video != null) { Video.Unload(); Video.enabled = false; }
        Appearance.SetSurfaceTexture(texture);
    }
    internal void Fail(string message) { Error = message; Debug.LogError(message, this); }
    public void Clear()
    {
        Released = true;
        if (Video != null) Video.Unload();
        freezeOwners.Clear();
        if (Appearance != null) Appearance.Clear();
    }
    private void OnDestroy() => Clear();
}
