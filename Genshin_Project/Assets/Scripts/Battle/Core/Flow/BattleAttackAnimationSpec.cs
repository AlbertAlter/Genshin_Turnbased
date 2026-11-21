using System;
using System.Collections.Generic;

/// <summary>Presentation only. Self: 1 none, 2 lean, 3 heavy release, 4 fixed lunge, 5 casting lift with triple halo.</summary>
public sealed class BattleAttackAnimationSpec
{
    public readonly string EffectID;
    public readonly int AttackType, SelfAnimation, HitAnimation;
    public readonly float Duration, ContactRatio, TiltDegrees, Strength;
    public readonly string ScreenShakeAxes, HitEffectPreset, AnimationHook;
    private readonly Func<float, float> strengthByProgress;
    public readonly float ScreenShakeAmplitude;
    public bool HasScreenShake => ScreenShakeAxes.Length > 0 && ScreenShakeAmplitude > 0;
    public float Windup => SelfAnimation == 1 ? 0 : Duration * ContactRatio;
    public BattleAttackAnimationSpec(string effect, int type, int self, int hit, float duration, float contact, float tilt, float strength,
        string screenShakeAxes = "", float screenShakeAmplitude = 0, string hitEffectPreset = "", string animationHook = "",
        Func<float, float> strengthByProgress = null)
    {
        bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        if (type < 1 || type > 3 || self < 1 || self > 5 || hit < 1 || hit > 3 || !Finite(duration) || duration <= 0 ||
            !Finite(contact) || contact <= 0 || contact >= 1 || !Finite(tilt) || tilt < 0 || tilt > 45 || !Finite(strength) || strength < 0 || strength > 3)
            throw new ArgumentException("自身动画编号须为1..5，攻击类型与受击动画须为1..3；时长为正，命中比例在0与1之间，角度0..45，幅度0..3：" + effect);
        EffectID = effect; AttackType = type; SelfAnimation = self; HitAnimation = hit;
        Duration = duration; ContactRatio = contact; TiltDegrees = tilt; Strength = strength;
        string axes = (screenShakeAxes ?? "").Trim().ToUpperInvariant();
        var selectedAxes = new HashSet<string>(StringComparer.Ordinal);
        if (axes.Length > 0) foreach (string raw in axes.Split(';'))
        {
            string axis = raw.Trim();
            if ((axis != "X" && axis != "Y" && axis != "Z") || !selectedAxes.Add(axis))
                throw new ArgumentException("震屏方向须为X、Y、Z，以英文分号组合且不重复：" + effect);
        }
        var orderedAxes = new List<string>(); foreach (string axis in new[] { "X", "Y", "Z" }) if (selectedAxes.Contains(axis)) orderedAxes.Add(axis);
        ScreenShakeAxes = string.Join(";", orderedAxes);
        if (!Finite(screenShakeAmplitude) || screenShakeAmplitude < 0 || screenShakeAmplitude > 0.1f)
            throw new ArgumentException("震屏幅度须为0..0.1的比例：" + effect);
        ScreenShakeAmplitude = screenShakeAmplitude;
        HitEffectPreset = (hitEffectPreset ?? "").Trim();
        if (HitEffectPreset.Length > 0 && !TryGetHitEffectPreset(HitEffectPreset, out _, out _))
            throw new ArgumentException("未知受击刀光预设：" + effect + "/" + HitEffectPreset);
        AnimationHook = (animationHook ?? "").Trim(); this.strengthByProgress = strengthByProgress;
    }
    public float StrengthAt(float progress)
    {
        float value = strengthByProgress == null ? Strength : strengthByProgress(Math.Max(0, Math.Min(1, progress)));
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 3)
            throw new InvalidOperationException("动画钩子的进度幅度须为0..3：" + EffectID);
        return value;
    }
    public static bool TryGetHitEffectPreset(string id, out int weapon, out bool heavy)
    {
        weapon = -1; heavy = false;
        if (string.IsNullOrEmpty(id)) return false;
        string[] weapons = { "Sword", "Claymore", "Bow", "Polearm", "Catalyst" };
        for (int i = 0; i < weapons.Length; i++) foreach (bool h in new[] { false, true })
            if (id == weapons[i] + (h ? "_Heavy" : "_Normal")) { weapon = i; heavy = h; return true; }
        return false;
    }
}

/// <summary>Explicit, presentation-only hooks. Read committed result snapshots; never call combat ScriptHookEvaluator or RNG.</summary>
public static class BattleAttackAnimationHooks
{
    private static readonly Dictionary<string, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec>> hooks
        = new Dictionary<string, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec>>(StringComparer.Ordinal);
    public static void Register(string id, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec> handler)
    {
        if (string.IsNullOrWhiteSpace(id) || handler == null) throw new ArgumentException("动画钩子需要编号和处理函数。");
        if (hooks.ContainsKey(id)) throw new InvalidOperationException("重复注册动画钩子：" + id);
        hooks.Add(id, handler);
    }
    public static void Unregister(string id, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec> handler)
    { if (id != null && hooks.TryGetValue(id, out var current) && current == handler) hooks.Remove(id); }
    public static BattleAttackAnimationSpec Resolve(BattlePresentationResult result, BattleAttackAnimationSpec spec)
    {
        if (spec == null || spec.AnimationHook.Length == 0) return spec;
        if (!hooks.TryGetValue(spec.AnimationHook, out var handler)) throw new InvalidOperationException("未注册动画钩子：" + spec.AnimationHook);
        return handler(result, spec);
    }
}

/// <summary>Add one cosmetic self-motion cue per real effect execution, retaining all original result order and blocks.</summary>
public static class BattleAttackAnimationTimeline
{
    public static (long, long, string) ExecutionKey(BattlePresentationResult r)
        => (r.Header.SessionId, r.Header.EffectExecutionId != 0 ? r.Header.EffectExecutionId : r.RequestID != 0 ? r.RequestID : r.Header.HitId != 0 ? r.Header.HitId : -r.Header.EventId, r.EffectID);
    public static IReadOnlyList<BattlePresentationCue> Build(IReadOnlyList<BattlePresentationCue> cues,
        Func<string, BattleAttackAnimationSpec> resolve)
    {
        var starts = new Dictionary<int, List<BattlePresentationCue>>();
        var seen = new HashSet<(long, long, string)>();
        var floors = new Dictionary<int, double>();
        double previousTime = 0;
        foreach (var cue in cues)
        {
            if (!floors.ContainsKey(cue.Block)) floors.Add(cue.Block, previousTime);
            previousTime = Math.Max(previousTime, cue.Time);
        }
        foreach (var cue in cues)
        {
            var r = cue.Result;
            if (cue.IsAnticipation || cue.IsEnemyExit ||
                (!(r.Data is DamageEventData) && r.Data.Kind != BattleEventKind.StatusAdded && !(r.Data is HealEventData))) continue;
            var spec = cue.Timing != null && cue.Timing.HasAttackAnimationSnapshot ? cue.Timing.AttackAnimation
                : BattleAttackAnimationHooks.Resolve(r, resolve(r.EffectID));
            if (spec == null) continue;
            cue.AttackAnimation = spec;
            if (spec.SelfAnimation == 1 || !r.SourceSide.HasValue || r.SourceSlot <= 0 || !seen.Add(ExecutionKey(r))) continue;
            double start = r.Data is DamageEventData ? Math.Max(floors[cue.Block], cue.Time - spec.Windup) : cue.Time;
            var marker = new BattlePresentationCue(r, start, cue.Timing, cue.Block) {
                IsAttackStart = true, AttackAnimation = spec,
                AttackWindupSeconds = r.Data is DamageEventData ? Math.Max(0, cue.Time - start) : spec.Windup
            };
            if (!starts.TryGetValue(cue.Block, out var list)) starts.Add(cue.Block, list = new List<BattlePresentationCue>());
            list.Add(marker);
        }
        foreach (var list in starts.Values) list.Sort((a, b) => { int time = a.Time.CompareTo(b.Time); return time != 0 ? time : a.Result.Order.CompareTo(b.Result.Order); });
        var result = new List<BattlePresentationCue>(cues.Count + seen.Count);
        for (int index = 0; index < cues.Count;)
        {
            int block = cues[index].Block, next = 0;
            starts.TryGetValue(block, out var list);
            while (index < cues.Count && cues[index].Block == block)
            {
                var cue = cues[index++];
                while (list != null && next < list.Count && list[next].Time <= cue.Time) result.Add(list[next++]);
                result.Add(cue);
            }
            while (list != null && next < list.Count) result.Add(list[next++]);
        }
        return result;
    }
}
