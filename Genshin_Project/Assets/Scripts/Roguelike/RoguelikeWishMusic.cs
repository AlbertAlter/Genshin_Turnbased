using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Plays the featured character theme while its wish banner is open.</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeWishMusic : MonoBehaviour
{
    public const string AudioFolder = "Audio/WishCharacterThemes";
    private const float RepeatDelay = 10f;
    private const float FadeDuration = .4f;
    private const string VolumeKey = "Wish.ThemeVolume";
    private const string MutedKey = "Wish.ThemeMuted";
    private static readonly string[] Extensions = { ".mp3", ".wav" };
    private static readonly Dictionary<int, Bookmark> Bookmarks = new Dictionary<int, Bookmark>();

    private readonly struct Bookmark
    {
        public readonly int Samples;
        public readonly float RepeatRemaining;

        public Bookmark(int samples, float repeatRemaining)
        {
            Samples = samples;
            RepeatRemaining = repeatRemaining;
        }
    }

    [SerializeField, Range(0f, 1f)] private float volume = .35f;
    private AudioSource channel;
    private AudioClip clip;
    private Coroutine playback, fade;
    private UnityWebRequest request;
    private int? characterId, loadedCharacterId;
    private int lastSamples;
    private float repeatAt;
    private int? featuredId;
    private bool muted;
    private TMP_Text toggleLabel, volumeLabel;
    private Image soundIcon;
    private Sprite soundOn, soundOff;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearBookmarks() => Bookmarks.Clear();

    private void Awake()
    {
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, volume));
        muted = PlayerPrefs.GetInt(MutedKey, 0) != 0;
    }

    public void SetFeatured(RoguelikeWishPrize prize)
    {
        featuredId = prize?.Kind == RoguelikeWishKind.Character && prize.Stars == 5
            ? (int?)prize.Id : null;
        SetTarget(muted ? null : featuredId);
    }

    public void Pause() => SetFeatured(null);

    internal void BuildControls(RectTransform mount, TMP_Text textStyle, RoguelikeWishArt art)
    {
        if (mount == null || textStyle == null || toggleLabel != null) return;
        var toggle = new GameObject("Toggle theme", typeof(RectTransform), typeof(Image), typeof(Button));
        Place((RectTransform)toggle.transform, mount, 0, 48);
        var background = toggle.GetComponent<Image>();
        background.color = RoguelikeWishArt.DarkBrown;
        art?.Apply(background, "WishButtonDark", 96);
        var button = toggle.GetComponent<Button>();
        button.onClick.AddListener(ToggleMuted);
        var toggleRect = (RectTransform)toggle.transform;
        toggleLabel = AddLabel(toggleRect, textStyle, 0, 48);
        toggleLabel.color = RoguelikeWishArt.LightText;
        soundIcon = new GameObject("Speaker", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        soundIcon.rectTransform.SetParent(toggleRect, false);
        soundIcon.rectTransform.sizeDelta = new Vector2(21, 21);
        soundIcon.color = toggleLabel.color;
        soundIcon.raycastTarget = false;
        button.targetGraphic = soundIcon;
        art?.Load("WishSoundOnIcon", 0, sprite => { soundOn = sprite; RefreshControls(); });
        art?.Load("WishSoundOffIcon", 0, sprite => { soundOff = sprite; RefreshControls(); });

        var slider = DefaultControls.CreateSlider(new DefaultControls.Resources()).GetComponent<Slider>();
        Place((RectTransform)slider.transform, mount, 59, 88);
        var track = slider.transform.Find("Background").GetComponent<Image>();
        track.rectTransform.anchorMin = new Vector2(0, .5f);
        track.rectTransform.anchorMax = new Vector2(1, .5f);
        track.rectTransform.sizeDelta = new Vector2(0, 8);
        art?.Apply(track, "WishThemeTrack");
        var sliderFill = slider.fillRect.GetComponent<Image>();
        sliderFill.color = RoguelikeWishArt.DarkBrown;
        var fillArea = (RectTransform)slider.fillRect.parent;
        fillArea.anchorMin = new Vector2(0, .5f);
        fillArea.anchorMax = new Vector2(1, .5f);
        fillArea.offsetMin = new Vector2(0, -4);
        fillArea.offsetMax = new Vector2(-8, 4);
        sliderFill.rectTransform.sizeDelta = Vector2.zero;
        var handle = slider.handleRect.GetComponent<Image>();
        handle.color = sliderFill.color;
        // Slider drives the handle's anchors. Limit the height of the area it stretches across.
        var handleArea = (RectTransform)handle.rectTransform.parent;
        handleArea.anchorMin = new Vector2(0, .5f);
        handleArea.anchorMax = new Vector2(1, .5f);
        handleArea.sizeDelta = new Vector2(-16, 16);
        handle.rectTransform.sizeDelta = new Vector2(16, 0);
        art?.Apply(handle, "WishButtonDark", 96);
        slider.SetValueWithoutNotify(volume);
        slider.onValueChanged.AddListener(SetVolume);
        volumeLabel = AddLabel(mount, textStyle, 155, 43);
        volumeLabel.color = RoguelikeWishArt.LightText;
        volumeLabel.alignment = TextAlignmentOptions.MidlineRight;
        volumeLabel.fontSize = 16;
        RefreshControls();
    }

    private void ToggleMuted()
    {
        muted = !muted;
        PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
        SetTarget(muted ? null : featuredId);
        RefreshControls();
    }

    private void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(VolumeKey, volume);
        if (channel != null && fade == null) channel.volume = volume;
        RefreshControls();
    }

    private void RefreshControls()
    {
        if (toggleLabel != null)
        {
            soundIcon.sprite = muted ? soundOff : soundOn;
            soundIcon.enabled = soundIcon.sprite != null;
            toggleLabel.enabled = !soundIcon.enabled;
            toggleLabel.text = muted ? "×" : "♪";
        }
        if (volumeLabel != null) volumeLabel.text = $"{Mathf.RoundToInt(volume * 100f)}%";
    }

    private static TMP_Text AddLabel(RectTransform parent, TMP_Text style, float x, float width)
    {
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        Place(label.rectTransform, parent, x, width);
        label.font = style.font;
        label.fontSize = 18;
        label.alignment = TextAlignmentOptions.Midline;
        label.color = style.color;
        label.raycastTarget = false;
        return label;
    }

    private static void Place(RectTransform rect, RectTransform parent, float x, float width)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 1);
        rect.offsetMin = new Vector2(x, 0);
        rect.offsetMax = new Vector2(x + width, 0);
    }

    private void SetTarget(int? next)
    {
        if (!isActiveAndEnabled || characterId == next) return;
        characterId = next;
        CancelPlayback();
        if (fade != null) return;
        if (channel != null && channel.isPlaying)
            fade = StartCoroutine(FadeOut());
        else
            SwitchTheme();
    }

    private void CancelPlayback()
    {
        if (playback != null) StopCoroutine(playback);
        playback = null;
        if (request != null)
        {
            request.Abort();
            request.Dispose();
            request = null;
        }
    }

    private IEnumerator FadeOut()
    {
        float volumeAtStart = channel.volume;
        float started = Time.unscaledTime;
        while (channel.isPlaying && Time.unscaledTime - started < FadeDuration)
        {
            lastSamples = channel.timeSamples;
            float remaining = 1f - (Time.unscaledTime - started) / FadeDuration;
            channel.volume = volumeAtStart * remaining * remaining;
            yield return null;
        }
        fade = null;
        SwitchTheme();
    }

    private void SwitchTheme()
    {
        RememberPosition();
        ReleaseClip();
        if (characterId.HasValue) playback = StartCoroutine(PlayTheme(characterId.Value));
    }

    private void RememberPosition()
    {
        if (!loadedCharacterId.HasValue || clip == null || channel == null) return;
        int samples = repeatAt > 0f ? 0 : channel.isPlaying ? channel.timeSamples : lastSamples;
        Bookmarks[loadedCharacterId.Value] = new Bookmark(samples,
            Mathf.Max(0f, repeatAt - Time.realtimeSinceStartup));
    }

    private void ReleaseClip()
    {
        if (channel != null)
        {
            channel.Stop();
            channel.clip = null;
        }
        if (clip != null) Destroy(clip);
        clip = null;
        loadedCharacterId = null;
        lastSamples = 0;
        repeatAt = 0f;
    }

    private IEnumerator PlayTheme(int id)
    {
        yield return null;
        foreach (string extension in Extensions)
        {
            string relativePath = $"{AudioFolder}/{id.ToString(CultureInfo.InvariantCulture)}{extension}";
            string root = Application.streamingAssetsPath;
            if (!root.Contains("://") && !File.Exists(Path.Combine(root, relativePath))) continue;
            if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(relativePath, out string url)) continue;
            AudioType type = extension == ".mp3" ? AudioType.MPEG : AudioType.WAV;
            request = UnityWebRequestMultimedia.GetAudioClip(url, type);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                clip = DownloadHandlerAudioClip.GetContent(request);
            else
                Debug.LogWarning($"祈愿角色曲加载失败：{relativePath}（{request.error}）", this);
            request.Dispose();
            request = null;
            if (clip != null) break;
        }
        if (clip == null)
        {
            playback = null;
            yield break;
        }

        if (channel == null) channel = gameObject.AddComponent<AudioSource>();
        channel.playOnAwake = false;
        channel.loop = false;
        channel.spatialBlend = 0f;
        channel.volume = volume;
        channel.clip = clip;
        loadedCharacterId = id;
        if (Bookmarks.TryGetValue(id, out var bookmark))
        {
            if (bookmark.RepeatRemaining > 0f)
                yield return WaitForReplay(bookmark.RepeatRemaining);
            else
                channel.timeSamples = lastSamples = Mathf.Clamp(bookmark.Samples, 0, Mathf.Max(0, clip.samples - 1));
        }
        while (true)
        {
            while (AudioListener.pause) yield return null;
            channel.Play();
            yield return null;
            while (channel.isPlaying || AudioListener.pause)
            {
                if (channel.isPlaying) lastSamples = channel.timeSamples;
                yield return null;
            }
            lastSamples = 0;
            yield return WaitForReplay(RepeatDelay);
            channel.timeSamples = 0;
        }
    }

    private IEnumerator WaitForReplay(float seconds)
    {
        repeatAt = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < repeatAt) yield return null;
        repeatAt = 0f;
    }

    private void OnDisable()
    {
        CancelPlayback();
        if (fade != null) StopCoroutine(fade);
        fade = null;
        RememberPosition();
        ReleaseClip();
        characterId = null;
        featuredId = null;
    }
}
