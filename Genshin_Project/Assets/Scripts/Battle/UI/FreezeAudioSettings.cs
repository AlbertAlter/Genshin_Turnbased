using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Per-stage audio controls, loaded only from the published UI workbook.</summary>
public sealed class FreezeAudioSettings
{
    public const string FileName = "FreezeAudio.xlsx";
    public readonly float[] VolumePercent = new float[3];
    public readonly float[] PitchSemitones = new float[3];
    public static string VolumeKey(int index) => $"Click{index + 1}VolumePercent";
    public static string PitchKey(int index) => $"Click{index + 1}PitchSemitones";

    public static FreezeAudioSettings Load() => Parse(BattlefieldDamagePopupStyle.ReadParameters(
        Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName)));

    public static FreezeAudioSettings Parse(IReadOnlyDictionary<string, string> values)
    {
        if (values == null || values.Count != 6) throw new InvalidDataException("解冻音效表需要三段各自的音量和音高，共6项。");
        float Number(string key)
        {
            if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float number) || float.IsNaN(number) || float.IsInfinity(number))
                throw new InvalidDataException("解冻音效参数必须为有限数字：" + key);
            return number;
        }
        var settings = new FreezeAudioSettings();
        for (int i = 0; i < 3; ++i)
        {
            settings.VolumePercent[i] = Number(VolumeKey(i));
            settings.PitchSemitones[i] = Number(PitchKey(i));
        }
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        for (int i = 0; i < 3; ++i)
            if (float.IsNaN(VolumePercent[i]) || float.IsInfinity(VolumePercent[i]) || VolumePercent[i] < 0 || VolumePercent[i] > 200 ||
                float.IsNaN(PitchSemitones[i]) || float.IsInfinity(PitchSemitones[i]) || PitchSemitones[i] < -12 || PitchSemitones[i] > 12)
                throw new InvalidDataException($"第{i + 1}段音效：音量须为0～200%，音高须为-12～12半音。");
    }

    public IReadOnlyDictionary<string, float> Values()
    {
        Validate();
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        for (int i = 0; i < 3; ++i)
        { result.Add(VolumeKey(i), VolumePercent[i]); result.Add(PitchKey(i), PitchSemitones[i]); }
        return result;
    }
}
