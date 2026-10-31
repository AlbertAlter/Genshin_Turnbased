using System.Collections.Generic;
using UnityEngine;

/// <summary>Splash仅扩展同一逻辑hit的目标；所有目标仍走原伤害、附着和反应入口。</summary>
internal static class DamageHitTargets
{
    internal readonly struct Entry
    {
        internal readonly BattleEntity Target;
        internal readonly float Rate;
        internal readonly bool IsSplash;
        internal Entry(BattleEntity target, float rate, bool isSplash)
        { Target = target; Rate = rate; IsSplash = isSplash; }
        // 只缩放本技能倍率；本目标的基础伤害加算仍由CountDamagePlan独立提供。
        internal HitData Scale(HitData hit) => new HitData(hit.Multiplier * Rate, hit.ElementAura);
    }

    internal static List<Entry> Expand(List<BattleEntity> primary, string splash, string element, BattleEntity boundStatusHost = null)
    {
        var result = new List<Entry>();
        var seen = new HashSet<BattleEntity>();
        foreach (BattleEntity target in primary)
            if (target != null && target.IsAlive && (!target.IsOffField || target == boundStatusHost) && seen.Add(target))
                result.Add(new Entry(target, 1f, false));
        bool hasSplash = TryParse(splash, out float rate, out int left, out int right);
        var battle = BattleManager.Instance;
        if (hasSplash) foreach (BattleEntity target in primary)
        {
            if (target == null || !target.IsAlive || (target.IsOffField && target != boundStatusHost)) continue;
            int origin = target.Position != null ? target.Position.SlotIndex : -1;
            if (origin < 1) continue;
            for (int pos = Mathf.Max(1, origin - left);
                pos <= Mathf.Min(BattlePositionSystem.GetSideCount(target.Side), origin + right); pos++)
            {
                if (pos == origin) continue;
                var slot = battle?.Field?.GetSlot(target.Side, pos);
                BattleEntity extra = slot != null && slot.IsOccupied ? slot.Occupant : null;
                if (extra != null && extra.IsOnField && seen.Add(extra)) result.Add(new Entry(extra, rate, true));
            }
        }
        // 主目标优先，同hit重叠范围只受击一次；岩伤按实际位置稳定决定最终结晶盾。
        if (element == "Geo") result.Sort((a, b) => a.Target.SlotPosition.CompareTo(b.Target.SlotPosition));
        return result; // 反应前一次建立实际目标与资格快照，不重新选取或抽随机。
    }

    internal static bool TryParse(string param2, out float rate, out int left, out int right)
    {
        rate = 0f; left = 0; right = 0;
        if (string.IsNullOrEmpty(param2)) return false;
        int p = param2.IndexOf("Splash(", System.StringComparison.Ordinal);
        if (p < 0) return false;
        string inner = param2.Substring(p + 7);
        int close = inner.IndexOf(')');
        if (close < 0) return false;
        var segments = inner.Substring(0, close).Split(';');
        if (segments.Length != 2 || !float.TryParse(segments[0].Trim(),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rate)
            || float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0f) return false;
        var range = segments[1].Trim().Split(',');
        return range.Length == 2 && int.TryParse(range[0].Trim(), out left) && left >= 0
            && int.TryParse(range[1].Trim(), out right) && right >= 0;
    }
}
