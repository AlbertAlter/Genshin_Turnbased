using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Optional video source. Owns its decoder/texture; borrows explicitly assigned UI targets.</summary>
[DisallowMultipleComponent]
public sealed class UICardVideoPlayer : MonoBehaviour
{
    public static string VideoPathForCharacter(int characterId) => CharacterAssetPath(characterId, "idle_60fps.mp4");
    public static string PosterPathForCharacter(int characterId) => CharacterAssetPath(characterId, "poster.png");
    private static string CharacterAssetPath(int characterId, string file)
    {
        if (characterId <= 0) throw new System.ArgumentOutOfRangeException(nameof(characterId));
        return "art_assets/Character_Animated/" + characterId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + file;
    }

    public string videoPath;
    public RawImage[] targets = new RawImage[0];
    public bool playOnStart = true;
    public bool paused;
    public bool Frozen { get; private set; }
    [Tooltip("Keep decoding while a live exit effect draws the video through a separate snapshot.")]
    public bool keepDecodingWhenHidden;
    [Range(.25f, 2)] public float speed = 1;
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public long Frame => decoder != null ? decoder.frame : -1;
    public ulong FrameCount => decoder != null ? decoder.frameCount : 0;
    public float FrameRate => decoder != null ? decoder.frameRate : 0;
    public double Duration => decoder != null ? decoder.length : 0;
    public int Loops { get; private set; }
    public long TextureBytes => texture != null ? (long)texture.width * texture.height * 4 : 0;
    private VideoPlayer decoder;
    private RenderTexture texture;
    private RawImage[] bound;
    private Texture[] previous;
    private bool started, loaded;

    private void Start() { started = true; if (playOnStart && !loaded) Load(); }
    public void Load()
    {
        Unload();
        if (!isActiveAndEnabled) return;
        loaded = true; Error = null; Loops = 0;
        StartCoroutine(Prepare());
    }
    private IEnumerator Prepare()
    {
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(videoPath, out string url))
        { Fail("无效视频路径：" + videoPath); yield break; }
        var owner = new GameObject("CardVideoDecoder"); owner.transform.SetParent(transform, false);
        decoder = owner.AddComponent<VideoPlayer>();
        decoder.playOnAwake = false; decoder.source = VideoSource.Url; decoder.url = url;
        decoder.audioOutputMode = VideoAudioOutputMode.None;
        decoder.renderMode = VideoRenderMode.RenderTexture;
        decoder.isLooping = true; decoder.waitForFirstFrame = true; decoder.skipOnDrop = true;
        decoder.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        decoder.errorReceived += OnError; decoder.loopPointReached += OnLoop;
        decoder.Prepare();
        float deadline = Time.realtimeSinceStartup + 20;
        while (!decoder.isPrepared && Error == null && Time.realtimeSinceStartup < deadline) yield return null;
        if (Error != null) yield break;
        if (!decoder.isPrepared) { Fail("视频准备超时：" + videoPath); yield break; }
        if (decoder.width == 0 || decoder.height == 0) { Fail("视频尺寸无效。"); yield break; }
        texture = new RenderTexture((int)decoder.width, (int)decoder.height, 0, RenderTextureFormat.ARGB32)
        { name = "CardVideoFrame", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (!texture.Create()) { Fail("无法创建视频纹理。"); yield break; }
        bound = (RawImage[])targets.Clone(); previous = new Texture[bound.Length];
        for (int i = 0; i < bound.Length; i++) if (bound[i] != null) previous[i] = bound[i].texture;
        decoder.targetTexture = texture;
        decoder.sendFrameReadyEvents = true; decoder.frameReady += OnFirstFrame;
        decoder.Play(); // Produce one frame before replacing the poster, even when initially paused.
        deadline = Time.realtimeSinceStartup + 10;
        while (!Ready && Error == null && Time.realtimeSinceStartup < deadline) yield return null;
        if (!Ready && Error == null) Fail("视频首帧超时：" + videoPath);
    }
    private void OnFirstFrame(VideoPlayer sender, long frame)
    {
        if (sender != decoder || texture == null) return;
        for (int i = 0; i < bound.Length; i++) if (bound[i] != null) bound[i].texture = texture;
        Ready = true;
        decoder.sendFrameReadyEvents = false; decoder.frameReady -= OnFirstFrame;
        ApplyPlaybackState();
    }
    private void OnError(VideoPlayer sender, string message) { if (sender == decoder) Fail(message); }
    private void OnLoop(VideoPlayer sender) { Loops++; }
    private void Fail(string message)
    {
        Error = message; Ready = false;
        if (decoder != null) decoder.Stop();
        RestoreTargets();
        Debug.LogError("UICardVideoPlayer: " + message, this);
    }
    private void Update()
    {
        ApplyPlaybackState();
        if (Ready && decoder.canSetPlaybackSpeed) decoder.playbackSpeed = Mathf.Clamp(speed, .25f, 2);
    }
    public void SetFrozen(bool value)
    {
        Frozen = value;
        ApplyPlaybackState();
    }
    private void ApplyPlaybackState()
    {
        if (!Ready) return;
        bool visible = false;
        if (bound != null) foreach (var target in bound) if (target != null && target.isActiveAndEnabled) { visible = true; break; }
        if (paused || Frozen || (!visible && !keepDecodingWhenHidden)) { if (decoder.isPlaying) decoder.Pause(); }
        else if (!decoder.isPlaying) decoder.Play();
    }
    public void Step()
    {
        if (Frozen) return;
        paused = true;
        if (!Ready) return;
        decoder.Pause();
        if (decoder.canStep) decoder.StepForward();
    }
    public void Restart()
    {
        if (!Ready || Frozen) return;
        paused = false; decoder.time = 0; ApplyPlaybackState();
    }
    private void RestoreTargets()
    {
        if (bound == null) return;
        for (int i = 0; i < bound.Length; i++)
            if (bound[i] != null && bound[i].texture == texture) bound[i].texture = previous[i];
    }
    public void Unload()
    {
        StopAllCoroutines(); Ready = false; loaded = false; Error = null;
        RestoreTargets(); bound = null; previous = null;
        if (decoder != null)
        {
            decoder.errorReceived -= OnError; decoder.loopPointReached -= OnLoop; decoder.frameReady -= OnFirstFrame;
            decoder.Stop(); decoder.targetTexture = null; Destroy(decoder.gameObject); decoder = null;
        }
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }
    private void OnDisable() { bool resume = loaded; Unload(); loaded = resume; }
    private void OnEnable() { if (started && loaded) Load(); }
    private void OnDestroy() => Unload();
}
