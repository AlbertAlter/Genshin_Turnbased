using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>诺艾尔只在这里识别真实hit、护盾和声明；伤害、治疗、授盾、减费、延时仍由配表执行。</summary>
public static class NoelleScriptHooks
{
    public const string Breastplate = "ST_Noelle_Breastplate", SweepingTime = "ST_Noelle_SweepingTime";
    public const string Devotion = "ST_Noelle_Devotion", CleanActions = "ST_Noelle_CleanActions";
    private sealed class HitMemory
    {
        internal long ID;
        internal StatusInstance Shield;
    }
    private sealed class HitFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly StatusInstance Shield;
        internal readonly long ID;
        internal bool Indexed;
        private readonly HitFrame previous = CurrentHit.Value;
        internal HitFrame(long id, BattleEntity owner)
        {
            ID = id; Owner = owner; Shield = owner?.EntityID == 1021 ? owner.GetStatus(Breastplate) : null;
            CurrentHit.Value = this;
        }
        public void Dispose() => CurrentHit.Value = previous;
    }
    private static readonly AsyncLocal<HitFrame> CurrentHit = new AsyncLocal<HitFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitMemory> HealHits
        = new ConditionalWeakTable<BattleDeclaration, HitMemory>();
    private static readonly ConditionalWeakTable<BattleDeclaration, StatusInstance> FourthActions
        = new ConditionalWeakTable<BattleDeclaration, StatusInstance>();
    private static readonly ConditionalWeakTable<StatusInstance, List<IDisposable>> Listeners
        = new ConditionalWeakTable<StatusInstance, List<IDisposable>>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterDeclarationObserver(IndexHit);
        BattleSpecialRules.RegisterHitScope((id, owner, _) => new HitFrame(id, owner));
        BattleSpecialRules.RegisterStatusLifecycle(Breastplate, StartBreastplate, Stop);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Noelle_DevotionShield", StartDevotionShield, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(Devotion, StartDevotion, Stop);
        BattleSpecialRules.RegisterActionCounterGroup(Devotion, _ => "Devotion");
        BattleSpecialRules.RegisterCondition("Noelle_HealingHit", (a, c) => Empty(a) && HealingHit(c));
        BattleSpecialRules.RegisterCondition("Noelle_Devotion", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1021 && c.Caster.IsAlive && LowHPAllies(c.Caster).Count > 0);
        BattleSpecialRules.RegisterCondition("Noelle_FourActions", (a, c) => Empty(a)
            && c?.Declaration != null && FourthActions.TryGetValue(c.Declaration, out var status) && status == c.Status);
        BattleSpecialRules.RegisterCondition("Noelle_ShieldDestroyed", (a, c) => Empty(a)
            && c?.Caster?.IsAlive == true && c.Status?.StatusID2 == Breastplate && !c.Status.IsActive
            && c.Declaration?.Kind == BattleEventKind.StatusRemoved
            && c.Declaration.Get<StatusEventData>() is StatusEventData fact
            && fact.InstanceId == c.Status.ApplyOrder && fact.Host == c.Caster
            && (fact.Reason == "ShieldBroken" || fact.Reason == "Replaced"));
        BattleSpecialRules.RegisterTargets("Noelle_DevotionTarget()", (battle, owner) =>
        {
            var candidates = LowHPAllies(owner);
            if (candidates.Count == 0) return new List<int>();
            float minimum = float.MaxValue;
            foreach (var target in candidates) minimum = Math.Min(minimum, target.CurrentHP);
            candidates.RemoveAll(target => target.CurrentHP != minimum);
            candidates.Sort((a, b) => a.SlotPosition.CompareTo(b.SlotPosition));
            var selected = candidates[candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count)];
            return new List<int> { selected.SlotPosition };
        });
        BattleSpecialRules.RegisterBuffScalar("Noelle_Defense()", ResolveAttackBonus);
    }

    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Active(StatusInstance status, BattleEntity owner)
        => owner?.IsAlive == true && status?.IsActive == true && owner.HasStatusInstance(status);
    internal static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1021 || status.Caster != owner)
            throw new InvalidOperationException("诺艾尔控制状态必须初始化在本人身上。");
        return owner;
    }
    private static bool HasShield(BattleEntity owner, StatusInstance status)
    {
        if (!Active(status, owner)) return false;
        foreach (var shield in owner.Shields)
            if (shield.Value > 0 && ReferenceEquals(shield.SourceStatus, status)) return true;
        return false;
    }

    private static void IndexHit(BattleDeclaration declaration)
    {
        var hit = declaration.Get<DamageEventData>(); var source = hit?.Source; var owner = source?.SourceEntity;
        if (owner?.EntityID != 1021 || !hit.HitLanded || !source.CountsAsHit
            || hit.Subject == null || hit.Subject.Side == owner.Side
            || (hit.DamageType != "Normal" && hit.DamageType != "Heavy")) return;
        var frame = CurrentHit.Value;
        if (frame?.Owner != owner || frame.ID != declaration.Header.HitId || frame.Indexed
            || !HasShield(owner, frame.Shield)) return;
        // 只索引事实；不在Record边界抽概率或施加效果。一个hit的后续目标沿用第一份真实命中。
        frame.Indexed = true;
        HealHits.Add(declaration, new HitMemory { ID = frame.ID, Shield = frame.Shield });
    }

    private static bool HealingHit(ScriptHookContext context)
    {
        var owner = context?.Caster; var declaration = context?.DamageEvent?.Declaration ?? context?.Declaration;
        if (owner?.EntityID != 1021 || declaration == null
            || !HealHits.TryGetValue(declaration, out var memory) || memory.Shield != context.Status
            || !HasShield(owner, memory.Shield)) return false;
        if (owner.ConstellationLevel >= 1 && owner.HasStatus(SweepingTime)) return true;
        if (!owner.CharacterCtrl.TryGetSkillLevelScalar("Noelle_HealingChance", 2, out float chance)
            || chance < 0 || chance > 1) throw new InvalidOperationException("护心铠缺少本战技等级的治疗概率。");
        float roll = BattleRandom.NextFloat01();
        var target = declaration.Get<DamageEventData>()?.Subject;
        LogManager.Log(LogCategory.ScriptHook, $"Noelle_HealingHit hit={memory.ID} 目标={target?.EntityID} 概率={chance:P1} 随机={roll:F4}");
        return roll < chance;
    }

    private static float ResolveAttackBonus(StatusEffectData effect, StatusInstance status)
    {
        var owner = status?.Caster;
        if (status?.StatusID2 != SweepingTime || owner?.EntityID != 1021
            || !owner.CharacterCtrl.TryResolveStatusLevelScalar(effect, out float ratio))
            throw new InvalidOperationException("大扫除加攻必须读取自身爆发等级的防御转化系数。");
        return owner.GetFinalDEF() * (ratio + (owner.ConstellationLevel >= 6 ? .5f : 0f));
    }

    private static List<BattleEntity> LowHPAllies(BattleEntity owner)
    {
        var result = new List<BattleEntity>(); var battle = BattleManager.Instance;
        if (owner?.EntityID != 1021 || !owner.IsAlive || battle == null) return result;
        foreach (var ally in battle.Allies)
        {
            var target = ally?.Entity;
            if (target?.IsAlive == true && target.Side == owner.Side
                && target.CurrentHP < .3f * target.GetFinalHP()) result.Add(target);
        }
        return result;
    }

    private static void StartDevotion(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host);
        status.IsPermanent = true; status.RemainingPhaseCount = 0; status.TriggerPhase = 1;
        var listeners = new List<IDisposable>(); Listeners.Add(status, listeners);
        listeners.Add(BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) =>
        {
            if (fact.Subject?.Side == owner.Side && fact.Before != fact.After)
                Dispatch(status, host, "Noelle_Devotion()", BattleEventHub.Current);
        }, status.ApplyOrder, () => Active(status, owner)));
    }

    private static void StartBreastplate(StatusInstance status, object host)
    {
        StartShield(status, RequireOwner(status, host));
    }
    private static void StartDevotionShield(StatusInstance status, object host)
    {
        if (!(host is BattleEntity target) || status.Caster?.EntityID != 1021 || target.Side != status.Caster.Side)
            throw new InvalidOperationException("全心全意护盾须由诺艾尔施加给同队角色。");
        StartShield(status, target);
    }
    private static void StartShield(StatusInstance status, BattleEntity owner)
    {
        var listeners = new List<IDisposable>(); Listeners.Add(status, listeners);
        listeners.Add(BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged, (_, fact) =>
        {
            if (fact.Subject != owner || fact.Reason != "Absorption") return;
            bool existed = false, remains = false;
            foreach (var before in fact.Before) if (before.SourceInstanceId == status.ApplyOrder && before.Value > 0) existed = true;
            foreach (var after in fact.After) if (after.SourceInstanceId == status.ApplyOrder && after.Value > 0) remains = true;
            if (!existed || remains) return;
            // 原伤害批次已经提交；走正式移除，停止治疗并保留护盾破碎的声明因果。
            owner.RemoveStatusInstance(status, "ShieldBroken");
        }, status.ApplyOrder, () => Active(status, owner)));
    }

    internal static void Dispatch(StatusInstance status, object host, string hook, BattleDeclaration cause)
    {
        var owner = status.Caster; var data = DataManager.Instance;
        if (!Active(status, owner) || data == null || !data.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                { Caster = owner, Status = status, StatusHost = host, Declaration = cause }, () => Active(status, owner));
    }
    internal static void FourthAction(StatusInstance status, BattleDeclaration declaration)
    {
        FourthActions.Add(declaration, status);
        Dispatch(status, status.Host, "Noelle_FourActions()", declaration);
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Listeners.TryGetValue(status, out var listeners)) foreach (var listener in listeners) listener.Dispose();
        Listeners.Remove(status);
    }
}

/// <summary>干净利落：本人永久整数Count，初值0、范围0～3；完成一次Normal/Heavy行为加1，第四次消费4并请求配表减冷却。</summary>
public sealed class NoelleCleanActionCount : CountStatusHandler
{
    private sealed class Memory { internal IDisposable Listener; internal long LastAction; }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = NoelleScriptHooks.RequireOwner(status, host); var memory = new Memory();
        status.RuntimeIntValue = 0; status.StackCount = 1; status.IsPermanent = true; status.RemainingPhaseCount = 0;
        status.CountRuntimeState = memory;
        memory.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, fact) =>
        {
            if (fact.Subject != owner || (fact.ActionType != "Normal" && fact.ActionType != "Heavy")
                || header.ActionExecutionId == 0 || memory.LastAction == header.ActionExecutionId) return;
            memory.LastAction = header.ActionExecutionId;
            var declaration = BattleEventHub.Current;
            int before = status.RuntimeIntValue; status.RuntimeIntValue = before + 1;
            BattleEventHub.Publish(new ResourceEventData("Count", status.StatusID2, owner, before, status.RuntimeIntValue, 1, ResourceChangeReason.Gain, true));
            if (status.RuntimeIntValue < 4) return;
            status.RuntimeIntValue = 0;
            BattleEventHub.Publish(new ResourceEventData("Count", status.StatusID2, owner, 4, 0, -4, ResourceChangeReason.Spend, true));
            NoelleScriptHooks.FourthAction(status, declaration);
        }, status.ApplyOrder, () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status));
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        NoelleScriptHooks.RequireOwner(status, host);
        if (application?.Caster != host) throw new InvalidOperationException("干净利落不能从其他角色合并计数。");
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory memory) memory.Listener?.Dispose();
        int before = status.RuntimeIntValue;
        status.CountRuntimeState = null; status.RuntimeIntValue = 0;
        if (before > 0) BattleEventHub.Publish(new ResourceEventData("Count", status.StatusID2,
            host as BattleEntity, before, 0, -before, ResourceChangeReason.Clear, true));
    }
}
