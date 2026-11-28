using System;
using UnityEngine;

/// <summary>Player preferences shared by scene-independent controls. Per-effect authored volumes remain separate.</summary>
public static class ProjectUserSettings
{
    private const string Prefix = "Project.Settings.";
    public static event Action Changed;
    public static float Master => Volume("Master");
    public static float Music => Volume("Music");
    public static float Voice => Volume("Voice");
    public static float Effects => Volume("Effects");
    private static float Volume(string key) => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + key, 1));
    public static bool English(bool fallback = false) => PlayerPrefs.HasKey(Prefix + "English") ? PlayerPrefs.GetInt(Prefix + "English") == 1 : fallback;
    public static bool Fullscreen => PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
    public static int Width => PlayerPrefs.GetInt(Prefix + "Width", Screen.width);
    public static int Height => PlayerPrefs.GetInt(Prefix + "Height", Screen.height);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        AudioListener.volume = Master;
        if (PlayerPrefs.HasKey(Prefix + "Width")) Screen.SetResolution(Width, Height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }
    public static void SetVolume(string channel, float value)
    {
        if (channel != "Master" && channel != "Music" && channel != "Voice" && channel != "Effects") throw new ArgumentException("Unknown audio channel.");
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        PlayerPrefs.SetFloat(Prefix + channel, Mathf.Clamp01(value)); Save();
    }
    public static void SetEnglish(bool value) { PlayerPrefs.SetInt(Prefix + "English", value ? 1 : 0); Save(); }
    public static void SetDisplay(int width, int height, bool fullscreen)
    {
        if (width < 640 || height < 360) throw new ArgumentOutOfRangeException(nameof(width));
        PlayerPrefs.SetInt(Prefix + "Width", width); PlayerPrefs.SetInt(Prefix + "Height", height);
        PlayerPrefs.SetInt(Prefix + "Fullscreen", fullscreen ? 1 : 0);
        Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed); Save();
    }
    public static void ResetAudio() { foreach (string key in new[] { "Master", "Music", "Voice", "Effects" }) PlayerPrefs.DeleteKey(Prefix + key); Save(); }
    private static void Save() { PlayerPrefs.Save(); AudioListener.volume = Master; Changed?.Invoke(); }
}
