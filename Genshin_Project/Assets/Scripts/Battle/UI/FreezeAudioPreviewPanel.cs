using System;
using System.Globalization;
using UnityEngine;

/// <summary>Optional reusable tuning panel; uses the same audio module as actual thaw clicks.</summary>
public sealed class FreezeAudioPreviewPanel : MonoBehaviour
{
    public UICardFreezeAudio audioModule;
    public Font controlsFont;
    public Rect area = new Rect(60, 245, 600, 700);
#if UNITY_EDITOR
    public static Func<FreezeAudioSettings, string> WriteTable;
#endif
    private FreezeAudioSettings shown;
    private readonly string[] volumeText = new string[3], pitchText = new string[3];
    private string message;
    private static readonly string[] Names = { "第一下 · 闷裂", "第二下 · 开裂", "第三下 · 碎裂" };

    private void OnGUI()
    {
        var oldMatrix = GUI.matrix; var oldFont = GUI.skin.font; bool oldEnabled = GUI.enabled;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = false };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        var field = new GUIStyle(GUI.skin.textField) { fontSize = 22 };
        GUILayout.BeginArea(area, GUI.skin.box);
        try
        {
            GUILayout.Label("解冻音效调试", label);
            GUILayout.Label("音量100%=原始，0%=静音；音高0=原调", label);
            GUILayout.Label("调节即时生效；点击卡牌也使用当前值。", label);
            var settings = audioModule != null ? audioModule.Settings : null;
            if (settings != null && !ReferenceEquals(settings, shown))
            {
                shown = settings;
                for (int i = 0; i < 3; ++i)
                { volumeText[i] = Format(settings.VolumePercent[i]); pitchText[i] = Format(settings.PitchSemitones[i]); }
            }
            if (settings == null)
                GUILayout.Label(audioModule != null ? audioModule.LastError ?? "音效参数尚未加载。" : "未绑定公共音效模块。", label);
            else
            {
                for (int i = 0; i < 3; ++i)
                {
                    GUILayout.Space(12);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Names[i], label, GUILayout.Width(260));
                    GUI.enabled = oldEnabled && ValidInput(i);
                    if (GUILayout.Button("试听", button, GUILayout.Width(130)))
                        message = audioModule.PlayClick(i + 1)
                            ? settings.VolumePercent[i] == 0 ? "本段音量为0，已静音。" : $"已提交第{i + 1}段试听。"
                            : audioModule.LastError ?? "音效模块未就绪。";
                    GUILayout.EndHorizontal();
                    GUI.enabled = oldEnabled;
                    bool changed = Number("音量 (%)", ref settings.VolumePercent[i], ref volumeText[i], 0, 200, label, field);
                    changed |= Number("音高 (半音)", ref settings.PitchSemitones[i], ref pitchText[i], -12, 12, label, field);
                    if (changed) audioModule.ApplyToPlayingVoice(i + 1);
                }
            }
            GUILayout.Space(18);
            GUILayout.BeginHorizontal();
            GUI.enabled = oldEnabled && audioModule != null;
            if (GUILayout.Button("重读配表", button))
                message = audioModule.ReloadSettings() ? "已从运行时配表重新加载。" : audioModule.LastError;
#if UNITY_EDITOR
            GUI.enabled = oldEnabled && settings != null && WriteTable != null && ValidInput(0) && ValidInput(1) && ValidInput(2);
            if (GUILayout.Button("写回配表", button))
            {
                try { message = WriteTable(settings); }
                catch (Exception ex) { message = "写回失败：" + ex.Message; Debug.LogError(message, this); }
            }
#else
            GUI.enabled = false;
            GUILayout.Button("写回配表（仅编辑器）", button);
#endif
            GUILayout.EndHorizontal();
            GUI.enabled = oldEnabled;
            GUILayout.Label(message ?? "未写回的临时调整在退出后丢弃。", label);
            GUILayout.Label("范围：音量0～200%；音高-12～12半音。", label);
            GUILayout.Label("三段仅变音高；播放速度和各自长度保持不变。", label);
            if (settings != null && (!ValidInput(0) || !ValidInput(1) || !ValidInput(2)))
                GUILayout.Label("输入无效，尚未应用；请按上述范围填写。", label);
            if (audioModule != null && !string.IsNullOrEmpty(audioModule.LastError))
                GUILayout.Label(audioModule.LastError, label);
        }
        finally
        { GUILayout.EndArea(); GUI.enabled = oldEnabled; GUI.skin.font = oldFont; GUI.matrix = oldMatrix; }
    }

    private static string Format(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    private bool ValidInput(int index) => Valid(volumeText[index], 0, 200) && Valid(pitchText[index], -12, 12);
    private static bool Valid(string text, float min, float max) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) &&
        !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max;
    private static bool Number(string title, ref float value, ref string text, float min, float max, GUIStyle label, GUIStyle field)
    {
        bool changed = false;
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, label, GUILayout.Width(155));
        float slide = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(270));
        if (slide != value) { value = Mathf.Round(slide * 10) / 10; text = Format(value); changed = true; }
        string typed = GUILayout.TextField(text, field, GUILayout.Width(95));
        if (typed != text)
        {
            text = typed;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) &&
                !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max)
            { changed |= value != n; value = n; }
        }
        GUILayout.EndHorizontal();
        return changed;
    }
}
