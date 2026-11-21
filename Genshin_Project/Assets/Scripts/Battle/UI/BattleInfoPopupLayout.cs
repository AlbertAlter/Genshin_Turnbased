using System;

/// <summary>Canvas 本地坐标中的双窗排布；不读取战斗、不改场景静态布局。</summary>
public static class BattleInfoPopupLayout
{
    public readonly struct Pair
    {
        public readonly float SkillLeft, StatusLeft, Top;
        public Pair(float skillLeft, float statusLeft, float top)
        { SkillLeft = skillLeft; StatusLeft = statusLeft; Top = top; }
    }

    public static void FitWidths(float skillWidth, float statusWidth, float availableWidth, float gap,
        bool hasStatus, out float skill, out float status)
    {
        availableWidth = Math.Max(1, availableWidth);
        gap = Math.Min(Math.Max(0, gap), Math.Max(0, availableWidth - 2));
        float scale = Math.Min(1, availableWidth / Math.Max(1, skillWidth));
        if (hasStatus) scale = Math.Min(1, Math.Max(1, availableWidth - gap) / Math.Max(1, skillWidth + statusWidth));
        skill = skillWidth * scale;
        status = statusWidth * scale;
    }

    public static Pair Place(float minX, float maxX, float minY, float maxY,
        float desiredSkillLeft, float desiredTop, float skillWidth, float skillHeight,
        float statusWidth, float statusHeight, float gap, bool hasStatus, bool preferStatusRight)
    {
        float left = Clamp(desiredSkillLeft, minX, maxX - skillWidth);
        if (!hasStatus)
            return new Pair(left, left, Clamp(desiredTop, minY + skillHeight, maxY));

        gap = Math.Min(Math.Max(0, gap), Math.Max(0, maxX - minX - skillWidth - statusWidth));
        bool rightFits = left + skillWidth + gap + statusWidth <= maxX;
        bool leftFits = left - gap - statusWidth >= minX;
        bool right = preferStatusRight ? rightFits || !leftFits : !leftFits && rightFits;
        if (!leftFits && !rightFits) right = preferStatusRight;
        float groupWidth = skillWidth + gap + statusWidth;
        float groupLeft = right ? left : left - gap - statusWidth;
        groupLeft = Clamp(groupLeft, minX, maxX - groupWidth);
        float skillLeft = right ? groupLeft : groupLeft + statusWidth + gap;
        float statusLeft = right ? skillLeft + skillWidth + gap : groupLeft;
        return new Pair(skillLeft, statusLeft, Clamp(desiredTop, minY + Math.Max(skillHeight, statusHeight), maxY));
    }

    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(Math.Max(min, max), value));
}
