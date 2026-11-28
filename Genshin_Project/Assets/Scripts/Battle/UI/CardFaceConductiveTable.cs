using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class CardFaceConductiveStyle
{
    public Vector2 referenceSize = new Vector2(76, 76), centerOffset = Vector2.zero;
    public float referenceCardWidth = 197;
    public float enterSeconds = .12f, exitSeconds = .1f, changeSeconds = .22f;
    public float startScale = .8f, changeScaleAmount = .15f, sizeMultiplier = 1;
    public float coreOpacity = 1, glowOpacity = .7f;
    public CardFaceConductiveStyle Copy() => (CardFaceConductiveStyle)MemberwiseClone();
}

/// <summary>Eight motion/appearance parameters; flat file alongside Targeted.xlsx, never under an effect subfolder.</summary>
public sealed class CardFaceConductiveTable
{
    public const string FileName = "LisaConductive.xlsx";
    private CardFaceConductiveStyle values;
    public static CardFaceConductiveTable ReadPublished(string fileName = FileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".xlsx", StringComparison.Ordinal))
            throw new ArgumentException("卡面符印参数需要单个xlsx文件名。");
        return ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", CardTargetIndicatorTable.Folder, fileName));
    }
    public static CardFaceConductiveTable ReadFile(string path) => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));
    public CardFaceConductiveStyle ApplyTo(CardFaceConductiveStyle layout)
    {
        var next = values.Copy();
        next.referenceSize = layout.referenceSize; next.centerOffset = layout.centerOffset;
        next.referenceCardWidth = layout.referenceCardWidth; return next;
    }
    public static Dictionary<string, float> Values(CardFaceConductiveStyle s) => new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["EnterSeconds"] = s.enterSeconds, ["ExitSeconds"] = s.exitSeconds, ["ChangeSeconds"] = s.changeSeconds,
        ["StartScale"] = s.startScale, ["ChangeScaleAmount"] = s.changeScaleAmount, ["SizeMultiplier"] = s.sizeMultiplier,
        ["CoreOpacity"] = s.coreOpacity, ["GlowOpacity"] = s.glowOpacity
    };
    public static CardFaceConductiveTable Parse(IReadOnlyDictionary<string, string> raw)
    {
        if (raw == null || raw.Count != 8) throw new InvalidDataException("卡面符印参数表需要8项参数。");
        float N(string key, float min, float max)
        {
            if (!raw.TryGetValue(key, out string text) || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)
                || float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max) throw new InvalidDataException("卡面符印参数无效：" + key);
            return n;
        }
        return new CardFaceConductiveTable { values = new CardFaceConductiveStyle
        {
            enterSeconds = N("EnterSeconds", 0, 10), exitSeconds = N("ExitSeconds", 0, 10), changeSeconds = N("ChangeSeconds", 0, 10),
            startScale = N("StartScale", .1f, 2), changeScaleAmount = N("ChangeScaleAmount", 0, 1),
            sizeMultiplier = N("SizeMultiplier", .25f, 3), coreOpacity = N("CoreOpacity", 0, 1), glowOpacity = N("GlowOpacity", 0, 1)
        }};
    }
}
