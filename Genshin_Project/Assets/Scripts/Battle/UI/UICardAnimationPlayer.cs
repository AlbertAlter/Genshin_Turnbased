using UnityEngine;
using UnityEngine.UI;

/// <summary>Optional animation on an existing Image. Never changes layout, material, mask, colour or visibility.</summary>
[DisallowMultipleComponent]
public sealed class UICardAnimationPlayer : MonoBehaviour
{
    public Image target;
    public bool paused;
    [Min(0)] public float speed = 1;
    [Tooltip("0 preserves original timing; a positive limit samples without speeding up the clip.")]
    public int sampleFps;
    public int FrameIndex { get; private set; }
    public StreamingCardAnimationClip Clip { get; private set; }
    private double clock;
    private Sprite previous;
    private Sprite assigned;
    public void Bind(StreamingCardAnimationClip clip, double offsetSeconds = 0)
    {
        Unbind();
        if (target == null || clip == null || !clip.Ready) return;
        previous = target.sprite; Clip = clip; clock = offsetSeconds; Present();
    }
    public void Unbind()
    {
        if (target != null && target.sprite == assigned) target.sprite = previous;
        Clip = null; previous = assigned = null;
    }
    public void Step()
    {
        if (Clip == null || !Clip.Ready) return;
        paused = true; clock = Clip.Timeline.Start((FrameIndex + 1) % Clip.Timeline.Count); Present(false);
    }
    private void Update()
    {
        if (paused || target == null || !target.isActiveAndEnabled || Clip == null || !Clip.Ready) return;
        clock += Time.unscaledDeltaTime * Mathf.Max(0, speed);
        clock %= Clip.Timeline.Duration;
        Present();
    }
    private void Present(bool sample = true)
    {
        double time = sample && sampleFps > 0 ? System.Math.Floor(clock * sampleFps) / sampleFps : clock;
        FrameIndex = Clip.Timeline.FrameAt(time);
        assigned = Clip.Frames[FrameIndex];
        if (target.sprite != assigned) target.sprite = assigned;
    }
    private void OnDestroy() => Unbind();
}
