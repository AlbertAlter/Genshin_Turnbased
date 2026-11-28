using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

/// <summary>One card's public visual handle. Appearance, masking and video fallback are shared by every scene.</summary>
public sealed class UICardView : MonoBehaviour
{
    public UICardFrameView Appearance { get; private set; }
    public UICardVideoPlayer Video { get; private set; }
    public bool Ready => Appearance != null && Appearance.Surface != null;
    public bool Released { get; private set; }
    public bool MediaResolved { get; internal set; }
    public string Error { get; private set; }
    public int FrameLevel { get; internal set; }
    public bool BackReady => backSprite != null;
    public Sprite CardBackSprite => backSprite;
    private Sprite backSprite;
    private Image backFace;
    private object turnoverOwner;
    private Vector3 facePosition;
    private Vector3 faceScale;
    private Quaternion faceRotation;
    private Color tint = Color.white;
    internal void SetArtHiddenBack(float amount)
    {if(backFace!=null)backFace.canvasRenderer.SetAlpha(1-Mathf.Clamp01(amount));}
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
    internal void SetCardBack(Sprite sprite)
    {
        if (sprite == null || Released) return;
        backSprite = sprite;
        if (backFace != null) { backFace.sprite = sprite; backFace.color = tint; }
    }
    private void EnsureBackFace()
    {
        if (backFace == null)
        {
            var go = new GameObject("CardBack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false); backFace = go.GetComponent<Image>();
            var rect = backFace.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; backFace.raycastTarget = false; backFace.enabled = false; backFace.preserveAspect = true;
        }
        backFace.sprite = backSprite; backFace.color = tint;
        SetArtHiddenBack(Appearance!=null?Appearance.ArtHiddenAmount:0);
    }
    public bool BeginTurnover(object owner)
    {
        if (owner == null || !Ready || !BackReady || Released || (turnoverOwner != null && !ReferenceEquals(owner, turnoverOwner))) return false;
        EnsureBackFace();
        if (turnoverOwner == null)
        { facePosition = Appearance.VisualRoot.localPosition; faceRotation = Appearance.VisualRoot.localRotation; faceScale = Appearance.VisualRoot.localScale; turnoverOwner = owner; }
        return true;
    }
    internal bool IsTurnoverOwnedBy(object owner) => turnoverOwner != null && ReferenceEquals(turnoverOwner, owner);
    public void SampleTurnover(object owner, float liftPixels, float degrees)
    {
        if (!ReferenceEquals(turnoverOwner, owner) || Appearance == null || backFace == null) return;
        var rotation = Quaternion.Euler(0, degrees, 0); var offset = Vector3.back * liftPixels;
        float scale = CardAirborneFlipMotion.ProjectionScale(liftPixels, Appearance.Root.rect.height);
        Appearance.VisualRoot.localPosition = facePosition + offset;
        Appearance.VisualRoot.localRotation = faceRotation * rotation;
        Appearance.VisualRoot.localScale = faceScale * scale;
        bool reverse = CardAirborneFlipMotion.BackVisible(degrees);
        Appearance.SetFaceVisible(!reverse); backFace.enabled = reverse;
        backFace.rectTransform.localPosition = facePosition + offset;
        backFace.rectTransform.localRotation = faceRotation * rotation * Quaternion.Euler(0, 180, 0);
        backFace.rectTransform.localScale = Appearance.VisualRoot.localScale;
    }
    public void EndTurnover(object owner)
    {
        if (turnoverOwner == null || !ReferenceEquals(turnoverOwner, owner)) return;
        if (Appearance != null && Appearance.VisualRoot != null)
        { Appearance.VisualRoot.localPosition = facePosition; Appearance.VisualRoot.localRotation = faceRotation; Appearance.VisualRoot.localScale = faceScale; Appearance.SetFaceVisible(true); }
        if (backFace != null) { backFace.enabled = false; backFace.rectTransform.localPosition = Vector3.zero; backFace.rectTransform.localRotation = Quaternion.identity; backFace.rectTransform.localScale = Vector3.one; }
        turnoverOwner = null;
    }
    internal void PlayVideo(string path)
    {
        if (Video == null) Video = gameObject.AddComponent<UICardVideoPlayer>();
        else Video.Unload();
        Video.playOnStart = false;
        Video.videoPath = path; Video.targets = new[] { Appearance.Surface };
        Video.SetFrozen(AnimationFrozen);
        Video.enabled = true;
        Video.Load();
    }
    public void SetTint(Color color) { tint = color; if (Appearance != null) Appearance.SetTint(color); if (backFace != null) backFace.color = color; }
    internal void SetStaticVariant(Texture texture)
    {
        if (Video != null) { Video.Unload(); Video.enabled = false; }
        Appearance.SetSurfaceTexture(texture);
    }
    internal void Fail(string message) { Error = message; Debug.LogError(message, this); }
    public void Clear()
    {
        if (turnoverOwner != null) EndTurnover(turnoverOwner);
        Released = true;
        if (Video != null) Video.Unload();
        freezeOwners.Clear();
        if (Appearance != null) Appearance.Clear();
    }
    private void OnDestroy() => Clear();
    private void OnDisable() { if (turnoverOwner != null) EndTurnover(turnoverOwner); }
}
