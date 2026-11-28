using System;
using System.Collections.Generic;

/// <summary>元素栏纯表现采样。输入是已提交的快照；不读实时战斗对象、不计算反应。</summary>
public static class ElementAuraMotion
{
    public sealed class Style
    {
        public float Approach = .18f, Gather = .06f, Flash = .09f, Hold = .06f, Settle = .22f;
        public float Refresh = .22f, Travel = 48, ContactX = 13, ResidentX = 5;
        public float RefreshScale = .14f, Brightness = .35f, Burst = .72f;
        public float Impact => Approach + Gather;
        public float Release => Impact + Flash + Hold;
        public float Duration => Release + Settle;
    }

    public sealed class Clip
    {
        public readonly string[] Before, After, Involved;
        public readonly string Incoming;
        public readonly bool IsRefresh, HasContact;
        public bool HasReaction => Involved.Length > 0;
        public Clip(IReadOnlyList<string> before, IReadOnlyList<string> after, string incoming,
            IEnumerable<string> involved, bool refresh, bool hasContact)
        {
            Before = Copy(before); After = Copy(after); Incoming = incoming;
            var unique = new List<string>();
            if (involved != null) foreach (string e in involved) if (!unique.Contains(e)) unique.Add(e);
            Involved = unique.ToArray(); IsRefresh = refresh; HasContact = hasContact;
        }
        public float Duration(Style style) => IsRefresh ? style.Refresh : !HasContact && !HasReaction ? 0 : style.Duration;
    }

    public struct Icon
    {
        public string Key, Element;
        public float X, Y, Scale, Opacity, Glow;
        public Icon(string key, string element, float x, float y, float scale = 1, float opacity = 1, float glow = 0)
        { Key = key; Element = element; X = x; Y = y; Scale = scale; Opacity = opacity; Glow = glow; }
    }

    public static string[] Copy(IReadOnlyList<string> values)
    {
        if (values == null) return Array.Empty<string>();
        var copy = new string[values.Count]; for (int i = 0; i < copy.Length; i++) copy[i] = values[i]; return copy;
    }
    public static string[] Order(IReadOnlyList<string> previous, IReadOnlyList<ElementPoolSnapshot> pools,
        IReadOnlyList<string> refreshed = null)
    {
        var present = new List<string>();
        if (pools != null)
            foreach (var pool in pools)
                if ((pool.Kind == ReactionPoolKind.NormalAura || pool.Kind == ReactionPoolKind.Intrinsic) &&
                    (pool.IsIntrinsic || pool.Amount > 0) && !present.Contains(pool.Element)) present.Add(pool.Element);
        var result = new List<string>();
        if (previous != null) foreach (string e in previous) if (present.Contains(e) && !result.Contains(e)) result.Add(e);
        if (refreshed != null && pools != null)
            foreach (string key in refreshed)
                foreach (var pool in pools)
                    if (key == pool.Kind + ":" + pool.Element && present.Contains(pool.Element) && !result.Contains(pool.Element)) result.Add(pool.Element);
        foreach (string e in present) if (!result.Contains(e)) result.Add(e);
        return result.ToArray();
    }
    public static void Still(IReadOnlyList<string> order, float step, List<Icon> output)
    {
        output.Clear();
        for (int i = 0; i < order.Count; i++) output.Add(new Icon("aura:" + order[i], order[i], 0, i * step));
    }
    private static float Clamp(float v) => Math.Max(0, Math.Min(1, v));
    private static float Ratio(float value, float duration) => duration <= 0 ? (value >= 0 ? 1 : 0) : Clamp(value / duration);
    private static float Smooth(float v) { v = Clamp(v); return v * v * (3 - 2 * v); }
    private static float Mix(float a, float b, float t) => a + (b - a) * t;
    private static float ContactEase(float t) => t < .5f ? 16 * t * t * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 5) / 2;
    public static float RefreshPulse(float time, Style style)
    {
        if (time < 0 || time >= style.Refresh) return 0;
        float p = Ratio(time, style.Refresh);
        return p < .28f ? Smooth(p / .28f) : 1 - Smooth((p - .28f) / .72f);
    }
    public static void OverlayRefresh(Clip clip, float time, Style style, List<Icon> output)
    {
        float pulse = RefreshPulse(time, style);
        for (int i = 0; i < output.Count; i++)
        {
            var icon = output[i];
            // 只增强仍存在的原图标，不重新生成已消耗的元素。
            if (icon.Key != "aura:" + clip.Incoming) continue;
            icon.Scale = Math.Max(icon.Scale, 1 + style.RefreshScale * pulse);
            icon.Glow = Math.Max(icon.Glow, pulse); output[i] = icon;
        }
    }
    /// <summary>抽象坐标：X朝卡外，Y按栏位生长方向；场景仅转换已有锚点与尺寸。</summary>
    public static float Sample(Clip clip, float time, Style s, float step, List<Icon> output, out float collisionY)
    {
        collisionY = 0;
        if (time >= clip.Duration(s)) { Still(clip.After, step, output); return 0; }
        Still(clip.Before, step, output);
        if (clip.IsRefresh) { OverlayRefresh(clip, time, s, output); return 0; }
        float gather = ContactEase(Ratio(time, s.Impact));
        float settleTime = Ratio(time - s.Release, s.Settle), settle = Smooth(settleTime);
        float returnX = Smooth(settleTime / .6f), reflowY = Smooth((settleTime - .6f) / .4f);
        float fade = Smooth(Ratio(time - s.Impact, s.Flash + s.Hold));
        int resident = -1;
        for (int i = 0; i < clip.Before.Length; i++)
            if (Array.IndexOf(clip.Involved, clip.Before[i]) >= 0) { resident = i; break; }
        if (resident < 0) resident = Array.IndexOf(clip.Before, clip.Incoming);
        collisionY = Math.Max(0, resident >= 0 ? resident : Array.IndexOf(clip.After, clip.Incoming)) * step;
        for (int i = 0; i < output.Count; i++)
        {
            var icon = output[i]; int next = Array.IndexOf(clip.After, icon.Element);
            bool joins = clip.HasReaction && Array.IndexOf(clip.Involved, icon.Element) >= 0;
            icon.X = joins ? Mix(-s.ResidentX * gather, 0, returnX) : 0;
            icon.Y = Mix(i * step, next < 0 ? i * step : next * step, reflowY);
            icon.Scale = next >= 0 ? 1 + (joins ? .1f * gather * (1 - settle) : 0) : Mix(1, .45f, fade);
            icon.Opacity = next >= 0 ? 1 : 1 - fade;
            icon.Glow = joins ? (1 - fade) * gather : 0; output[i] = icon;
        }
        foreach (string e in clip.Involved)
            if (e != clip.Incoming && Array.IndexOf(clip.Before, e) < 0)
                output.Add(new Icon("participant:" + e, e, -s.ResidentX, collisionY, .9f + .1f * gather,
                    gather * (1 - fade), gather * (1 - fade)));
        if (!string.IsNullOrEmpty(clip.Incoming) && clip.Incoming != "Physical" && clip.Incoming != "None")
        {
            int next = Array.IndexOf(clip.After, clip.Incoming);
            bool becomesAura = Array.IndexOf(clip.Before, clip.Incoming) < 0 && next >= 0;
            output.Add(new Icon("incoming:" + clip.Incoming, clip.Incoming,
                Mix(Mix(s.Travel, s.ContactX, gather), 0, returnX),
                Mix(collisionY, next < 0 ? collisionY : next * step, reflowY), Mix(.85f, 1, gather),
                Ratio(time, .08f) * (becomesAura ? 1 : 1 - fade), gather * (1 - fade)));
        }
        for (int i = 0; i < clip.After.Length; i++)
            if (clip.After[i] != clip.Incoming && Array.IndexOf(clip.Before, clip.After[i]) < 0)
                output.Add(new Icon("new:" + clip.After[i], clip.After[i], 0, i * step, Mix(.7f, 1, settle), settle));
        return clip.HasReaction && time >= s.Impact ? (1 - Ratio(time - s.Impact, .32f)) * s.Burst : 0;
    }
}
