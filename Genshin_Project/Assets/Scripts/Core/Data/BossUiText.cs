using System;
using System.Collections.Generic;

/// <summary>Boss栏名称与副标题；Phase=0表示配表没有独立生命阶段。</summary>
public readonly struct BossUiText
{
    public readonly string Name;
    public readonly string Subtitle;

    public BossUiText(string name, string subtitle)
    {
        Name = name ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
    }

    public static BossUiText Resolve(IReadOnlyDictionary<string, LangEntry> lang, int enemyID,
        int phase, string fallbackName, bool english)
    {
        string root = FormattableString.Invariant($"boss_{enemyID}");
        string phaseRoot = phase > 0 ? FormattableString.Invariant($"{root}_phase_{phase}") : null;
        return new BossUiText(Read("name", fallbackName), Read("subtitle", string.Empty));

        string Read(string field, string fallback)
        {
            LangEntry entry;
            if (lang != null && (phaseRoot != null && lang.TryGetValue(phaseRoot + "_" + field, out entry)
                || lang.TryGetValue(root + "_" + field, out entry)))
                return (english ? entry.English : entry.Chinese) ?? string.Empty;
            return fallback ?? string.Empty;
        }
    }
}
