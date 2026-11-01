using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Behavior parameters only. Never contains layout, RGB colors, fonts or hierarchy.</summary>
public sealed class BattlefieldAnimationParameters
{
    public const string FileName = "BattleField1_animation.csv";
    private readonly Dictionary<string,float> values = new Dictionary<string,float>();
    private static readonly HashSet<string> Allowed = new HashSet<string>
    {
        "AP.CountDuration","AP.FloatDuration","AP.FloatDistance","AP.PulsePeriod","AP.PulseMinAlpha",
        "Health.TransitionDuration","Selection.DimmerFadeInDuration","Selection.DimmerFadeOutDuration","Selection.TargetTransitionDuration",
        "ActionIndicator.FadeInDuration","ActionIndicator.FadeOutDuration",
        "Normal.IconOpacity","Normal.SkillBackgroundOpacity","Normal.BurstBackgroundOpacity",
        "Freeze.IconOpacity","Freeze.SkillBackgroundOpacity","Freeze.BurstBackgroundOpacity",
        "Cooldown.IconOpacity","Cooldown.SkillBackgroundOpacity","Cooldown.BurstBackgroundOpacity"
    };
    public float Get(string key, float fallback, float min = 0, float max = float.MaxValue) =>
        values.TryGetValue(key, out float value) ? Mathf.Clamp(value,min,max) : fallback;

    public static BattlefieldAnimationParameters Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath,"Charts","UI",FileName);
        try { return ReadFile(path); }
        catch (Exception error)
        {
            Debug.LogWarning("UI 动画参数未加载，使用默认值。请用更新 UI 配置发布参数表：" + error.Message);
            return new BattlefieldAnimationParameters();
        }
    }

    // Also used to validate a source file by the publication tool before it is copied.
    public static BattlefieldAnimationParameters ReadFile(string path)
    {
        var result = new BattlefieldAnimationParameters();
        var lines = File.ReadAllLines(path);
        if(lines.Length==0 || lines[0].Trim().TrimStart('\uFEFF')!="Key,Value")throw new InvalidDataException("动画参数表头必须为 Key,Value");
        for (int index=1;index<lines.Length;index++)
        {
            if(string.IsNullOrWhiteSpace(lines[index]))continue;
            var cells=lines[index].Split(',');
            if(cells.Length!=2 || !float.TryParse(cells[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value<0)
                throw new InvalidDataException("动画参数格式错误，行 " + (index+1));
            string key=cells[0].Trim();
            if(!Allowed.Contains(key))throw new InvalidDataException("非动画参数或未知键："+key);
            if(result.values.ContainsKey(key))throw new InvalidDataException("动画参数重复："+key);
            result.values.Add(key,value);
        }
        return result;
    }
}
