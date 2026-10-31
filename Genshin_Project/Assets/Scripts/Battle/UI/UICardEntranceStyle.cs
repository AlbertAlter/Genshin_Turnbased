using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class UICardEntranceStyle
{
    public const string FileName = "CardEntrance.xlsx";
    public float Duration, OffsetWidthRatio, StartScale, StartAlpha, Stagger, EasePower, PreviewHold;
    public bool Unscaled;
    public static UICardEntranceStyle Load() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    public static UICardEntranceStyle ReadFile(string path)
    {
        var values = BattlefieldDamagePopupStyle.ReadParameters(path);
        float Read(string key, float min, float max)
        {
            if (!values.TryGetValue(key, out string text) || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new InvalidDataException("入场参数无效：" + key);
            return value;
        }
        float clock = Read("UseUnscaledTime", 0, 1);
        if (clock != 0 && clock != 1) throw new InvalidDataException("UseUnscaledTime 必须为 0 或 1。");
        return new UICardEntranceStyle {
            Duration = Read("DurationSeconds", 0, 20), OffsetWidthRatio = Read("StartOffsetWidthRatio", 0, 3),
            StartScale = Read("StartScale", .01f, 3), StartAlpha = Read("StartAlpha", 0, 1),
            Stagger = Read("StaggerSeconds", 0, 5), EasePower = Read("EasePower", 1, 8),
            PreviewHold = Read("PreviewHoldSeconds", 0, 30), Unscaled = clock == 1 };
    }
}
