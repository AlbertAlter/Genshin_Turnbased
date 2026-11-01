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
    public BattleHitTemplate HitTemplate { get; }
    public BattlePresentationTiming(double interval = 0, double jitter = 0, int x = 2, int y = 2, float mediumScale = .5f,
        BattleHitTemplate hitTemplate = null)
    {
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < 0 ||
            double.IsNaN(jitter) || double.IsInfinity(jitter) || jitter < 0 || x < 0 || x > 2 || y < 0 || y > 2 ||
            float.IsNaN(mediumScale) || mediumScale < 0 || mediumScale > 1)
            throw new ArgumentOutOfRangeException(nameof(interval));
        HitIntervalSeconds = interval; JitterSeconds = jitter; XRandomMode = x; YRandomMode = y;
        MediumRandomScale = mediumScale;
        HitTemplate = hitTemplate;
    }
}

public sealed class BattlePresentationCue
{
    public string CharacterVoicePath { get; internal set; }
    public string CharacterVoiceKey { get; internal set; }
    public BattleSkillMarker SkillMarker { get; internal set; }
    public BattleSkillTracks SkillTracks { get; internal set; }
    public BattlePresentationResult CastStart { get; internal set; }
    public BattlePresentationResult Result { get; }
    public double Time { get; internal set; }
    public BattlePresentationTiming Timing { get; }
    public int Block { get; }
    // Visual grouping only: resolved targets of one logical hit, without changing cue order/times.
    public IReadOnlyList<BattlePresentationResult> HitResults { get; internal set; }
    public bool LeadsHit { get; internal set; }
    public int VisualHitIndex { get; internal set; }
    public int VisualHitCount { get; internal set; } = 1;
    // Cosmetic launch only. Ordinary result views must never consume this stage.
    public bool IsAnticipation { get; internal set; }
    public double ProjectileSeconds { get; internal set; }
    internal BattlePresentationCue(BattlePresentationResult result, double time, BattlePresentationTiming timing, int block = 0)
    { Result = result; Time = time; Timing = timing; Block = block; }
}

/// <summary>
/// 沿真实提交轨迹建立时间轴。仅连续的同一次Damage输出可交错；子效果、状态、反应、死亡等形成屏障。
/// Damage自己的HPChanged跟随对应伤害，而非提前展示；所有相同时间的项按真实Order稳定排序。
/// </summary>
public static class BattlePresentationTimeline
{
    private sealed class TemplatePlan
    {
        public BattleHitTemplate Template;
        public double? Start;
        public readonly Dictionary<long, int> Hits = new Dictionary<long, int>();
    }
    private static long HitKey(BattlePresentationResult r) => r.Header.HitId != 0 ? r.Header.HitId
        : r.RequestID != 0 ? r.RequestID : r.Header.EventId;
    private static (long, long, string) ExecutionKey(BattlePresentationResult r) =>
        (r.Header.SessionId, r.Header.EffectExecutionId != 0 ? r.Header.EffectExecutionId : HitKey(r), r.EffectID);
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
        var settingsCache = new Dictionary<(long, long), BattlePresentationTiming>();
        var plans = new Dictionary<(long, long, string), TemplatePlan>();
        var linkedPlans = new Dictionary<(long, long, long), TemplatePlan>();
        foreach (var r in sequence.Results)
        {
            var eventKey = (r.Header.SessionId, r.Header.EventId);
            if (settingsCache.ContainsKey(eventKey)) continue;
            var timing = settings?.Invoke(r) ?? new BattlePresentationTiming();
            settingsCache.Add(eventKey, timing);
            if (!(r.Data is DamageEventData) || timing.HitTemplate == null) continue;
            var key = ExecutionKey(r);
            if (!plans.TryGetValue(key, out var plan))
                plans.Add(key, plan = new TemplatePlan { Template = timing.HitTemplate });
            long hit = HitKey(r);
            if (!plan.Hits.ContainsKey(hit)) plan.Hits.Add(hit, plan.Hits.Count);
            if (r.Header.EffectExecutionId != 0 && r.Header.HitId != 0)
                linkedPlans[(r.Header.SessionId, r.Header.EffectExecutionId, r.Header.HitId)] = plan;
        }
        BattlePresentationTiming Settings(BattlePresentationResult result) => settingsCache[(result.Header.SessionId, result.Header.EventId)];
        TemplatePlan Plan(BattlePresentationResult r)
        {
            if (r.Data is DamageEventData && Settings(r).HitTemplate != null)
                return plans[ExecutionKey(r)];
            return linkedPlans.TryGetValue((r.Header.SessionId, r.Header.EffectExecutionId, r.Header.HitId), out var p) ? p : null;
        }
        double Marker(BattlePresentationResult r, double floor)
        {
            var plan = Plan(r);
            if (plan == null) return floor;
            if (!plan.Start.HasValue) plan.Start = floor;
            return Math.Max(floor, plan.Start.Value + plan.Template.HitTime(plan.Hits[HitKey(r)]));
        }
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
                double time = timing.HitTemplate != null ? Marker(result, start)
                    : Math.Max(start, start + index * timing.HitIntervalSeconds + (Unit(result) * 2 - 1) * timing.JitterSeconds);
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
            else { Flush(); Add(result, Marker(result, cursor), Settings(result)); block++; }
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
        AttachHitGroups(cues);
        // Launch before the first related record, including a reaction recorded before its damage.
        // Causal barriers remain intact: shorten a flight if an earlier block leaves less room.
        var launches = new List<BattlePresentationCue>();
        foreach (var cue in cues)
        {
            if (!(cue.Result.Data is DamageEventData damage) || !damage.HitLanded ||
                cue.Timing.HitTemplate == null || cue.Timing.HitTemplate.ProjectileSeconds <= 0) continue;
            var plan = Plan(cue.Result); long hit = HitKey(cue.Result);
            int firstBlock = cue.Block;
            foreach (var related in cues)
                if (Plan(related.Result) == plan && HitKey(related.Result) == hit) firstBlock = Math.Min(firstBlock, related.Block);
            double floor = plan.Start ?? 0;
            foreach (var previous in cues) if (previous.Block < firstBlock) floor = Math.Max(floor, previous.Time);
            double launchTime = Math.Max(floor, cue.Time - cue.Timing.HitTemplate.ProjectileSeconds);
            if (launchTime >= cue.Time) continue;
            launches.Add(new BattlePresentationCue(cue.Result, launchTime, cue.Timing, firstBlock) {
                IsAnticipation = true, ProjectileSeconds = cue.Time - launchTime,
                HitResults = cue.HitResults, LeadsHit = cue.LeadsHit,
                VisualHitIndex = cue.VisualHitIndex, VisualHitCount = cue.VisualHitCount });
        }
        cues.AddRange(launches);
        cues.Sort((a, b) => a.Block != b.Block ? a.Block.CompareTo(b.Block) : a.Time != b.Time ? a.Time.CompareTo(b.Time)
            : a.IsAnticipation != b.IsAnticipation ? (a.IsAnticipation ? -1 : 1) : a.Result.Order.CompareTo(b.Result.Order));
        return cues.AsReadOnly();
    }

    private static void AttachHitGroups(List<BattlePresentationCue> cues)
    {
        var groups = new Dictionary<(long, long, long, string), List<BattlePresentationCue>>();
        var effects = new Dictionary<(long, long, string), List<List<BattlePresentationCue>>>();
        foreach (var cue in cues)
        {
            if (!(cue.Result.Data is DamageEventData damage) || !damage.HitLanded) continue;
            var r = cue.Result; var h = r.Header;
            long hit = h.HitId != 0 ? h.HitId : h.EventId;
            long execution = h.EffectExecutionId != 0 ? h.EffectExecutionId : r.RequestID;
            var key = (h.SessionId, execution, hit, r.EffectID);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<BattlePresentationCue>(); groups.Add(key, group);
                var effectKey = (h.SessionId, execution, r.EffectID);
                if (!effects.TryGetValue(effectKey, out var effectHits))
                    effects.Add(effectKey, effectHits = new List<List<BattlePresentationCue>>());
                effectHits.Add(group);
            }
            group.Add(cue);
        }
        foreach (var effect in effects.Values)
            for (int i = 0; i < effect.Count; i++)
            {
                var group = effect[i]; var results = new List<BattlePresentationResult>();
                foreach (var cue in group) results.Add(cue.Result);
                var shared = results.AsReadOnly();
                for (int j = 0; j < group.Count; j++)
                {
                    group[j].HitResults = shared; group[j].LeadsHit = j == 0;
                    group[j].VisualHitIndex = i; group[j].VisualHitCount = effect.Count;
                }
            }
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
