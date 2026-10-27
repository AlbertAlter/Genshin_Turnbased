using System;
using System.Collections.Generic;

/// <summary>只影响表现，不抽取BattleRandom。0/1/2的位置范围由UI层解释。</summary>
public sealed class BattlePresentationTiming
{
    public double HitIntervalSeconds { get; }
    public double JitterSeconds { get; }
    public int XRandomMode { get; }
    public int YRandomMode { get; }
    public float MediumRandomScale { get; }
    public BattlePresentationTiming(double interval = 0, double jitter = 0, int x = 2, int y = 2, float mediumScale = .5f)
    {
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < 0 ||
            double.IsNaN(jitter) || double.IsInfinity(jitter) || jitter < 0 || x < 0 || x > 2 || y < 0 || y > 2 ||
            float.IsNaN(mediumScale) || mediumScale < 0 || mediumScale > 1)
            throw new ArgumentOutOfRangeException(nameof(interval));
        HitIntervalSeconds = interval; JitterSeconds = jitter; XRandomMode = x; YRandomMode = y;
        MediumRandomScale = mediumScale;
    }
}

public sealed class BattlePresentationCue
{
    public BattlePresentationResult Result { get; }
    public double Time { get; internal set; }
    public BattlePresentationTiming Timing { get; }
    public int Block { get; }
    internal BattlePresentationCue(BattlePresentationResult result, double time, BattlePresentationTiming timing, int block = 0)
    { Result = result; Time = time; Timing = timing; Block = block; }
}

/// <summary>
/// 沿真实提交轨迹建立时间轴。仅连续的同一次Damage输出可交错；子效果、状态、反应、死亡等形成屏障。
/// Damage自己的HPChanged跟随对应伤害，而非提前展示；所有相同时间的项按真实Order稳定排序。
/// </summary>
public static class BattlePresentationTimeline
{
    private sealed class Group
    {
        public long Session, Execution, Request;
        public string Effect;
        public readonly List<BattlePresentationResult> Damage = new List<BattlePresentationResult>();
        public readonly List<BattlePresentationResult> AttachedHP = new List<BattlePresentationResult>();
    }

    public static IReadOnlyList<BattlePresentationCue> Build(BattlePresentationSequence sequence,
        Func<BattlePresentationResult, BattlePresentationTiming> settings = null)
    {
        if (sequence == null || !sequence.IsSealed) throw new InvalidOperationException("仅播放已关闭的正式结果序列。");
        var cues = new List<BattlePresentationCue>();
        var times = new Dictionary<(long, long), double>();
        var seen = new HashSet<(long, long)>();
        double cursor = 0;
        int block = 0;
        Group group = null;
        BattlePresentationTiming Settings(BattlePresentationResult result) => settings?.Invoke(result) ?? new BattlePresentationTiming();
        double CauseFloor(BattlePresentationResult result, double time)
        {
            foreach (long cause in result.Header.CauseEventIds)
                if (times.TryGetValue((result.Header.SessionId, cause), out double prior)) time = Math.Max(time, prior);
            return time;
        }
        void Add(BattlePresentationResult result, double time, BattlePresentationTiming timing)
        {
            time = CauseFloor(result, time);
            cues.Add(new BattlePresentationCue(result, time, timing, block));
            times[(result.Header.SessionId, result.Header.EventId)] = time;
            cursor = Math.Max(cursor, time);
        }
        void Flush()
        {
            if (group == null) return;
            double start = cursor;
            var hits = new Dictionary<long, int>();
            var targetTimes = new Dictionary<(BattleSide?, int), double>();
            foreach (var result in group.Damage)
            {
                long hit = result.Header.HitId != 0 ? result.Header.HitId : result.RequestID != 0 ? result.RequestID : result.Header.EventId;
                if (!hits.TryGetValue(hit, out int index)) { index = hits.Count; hits.Add(hit, index); }
                var timing = Settings(result);
                // 时间偏移围绕命中基准前后对称，但不得提前到本效果/因果屏障之前。
                double time = Math.Max(start, start + index * timing.HitIntervalSeconds + (Unit(result) * 2 - 1) * timing.JitterSeconds);
                var target = (result.TargetSide, result.TargetSlot);
                if (targetTimes.TryGetValue(target, out double previous)) time = Math.Max(time, previous);
                time = CauseFloor(result, time);
                targetTimes[target] = time;
                Add(result, time, timing);
            }
            foreach (var hp in group.AttachedHP)
            {
                double time = start;
                foreach (long cause in hp.Header.CauseEventIds)
                    if (times.TryGetValue((hp.Header.SessionId, cause), out double value)) time = Math.Max(time, value);
                Add(hp, time, Settings(hp));
            }
            group = null;
            block++;
        }
        foreach (var result in sequence.Results)
        {
            if (!seen.Add((result.Header.SessionId, result.Header.EventId))) continue;
            if (result.Data is DamageEventData)
            {
                long fallback = result.RequestID != 0 ? result.RequestID : result.Header.HitId != 0 ? result.Header.HitId : result.Header.EventId;
                if (group == null || group.Session != result.Header.SessionId || group.Execution != result.Header.EffectExecutionId || group.Effect != result.EffectID ||
                    (group.Execution == 0 && group.Request != fallback))
                {
                    Flush();
                    group = new Group { Session = result.Header.SessionId, Execution = result.Header.EffectExecutionId, Request = fallback, Effect = result.EffectID };
                }
                group.Damage.Add(result);
            }
            else if (result.Data is HPChangedEventData && group != null && HasDamageCause(result, group.Damage))
                group.AttachedHP.Add(result);
            else { Flush(); Add(result, cursor, Settings(result)); block++; }
        }
        Flush();
        // 先按实际提交轨迹补全直接因果约束（HP附属项是在完整Damage组计算后才取得命中时间）。
        cues.Sort((left, right) => left.Result.Order.CompareTo(right.Result.Order));
        times.Clear();
        foreach (var cue in cues)
        {
            cue.Time = CauseFloor(cue.Result, cue.Time);
            times[(cue.Result.Header.SessionId, cue.Result.Header.EventId)] = cue.Time;
        }
        cues.Sort((left, right) => left.Block != right.Block ? left.Block.CompareTo(right.Block)
            : left.Time != right.Time ? left.Time.CompareTo(right.Time) : left.Result.Order.CompareTo(right.Result.Order));
        return cues.AsReadOnly();
    }

    private static bool HasDamageCause(BattlePresentationResult hp, List<BattlePresentationResult> damage)
    {
        foreach (long cause in hp.Header.CauseEventIds)
            foreach (var item in damage)
                if (hp.Header.SessionId == item.Header.SessionId && cause == item.Header.EventId) return true;
        return false;
    }

    private static double Unit(BattlePresentationResult result)
    {
        uint hash = 2166136261;
        unchecked
        {
            foreach (long value in new[] { result.Header.SessionId, result.Header.EventId, result.Header.HitId, result.Header.EffectExecutionId })
            { hash = (hash ^ (uint)value) * 16777619; hash = (hash ^ (uint)(value >> 32)) * 16777619; }
            hash ^= hash >> 16; hash *= 0x7FEB352D; hash ^= hash >> 15; hash *= 0x846CA68B; hash ^= hash >> 16;
        }
        return (hash >> 8) / 16777216.0;
    }
}
