using System;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3038：按完整Damage效果占一次额度，不按hit/人数消费；主动和状态Skill伤害共用边界。</summary>
internal static class CinnabarWeaponHooks
{
    internal const string ID = "ST_Weapon_4303801";
    private const string Used = "ST_Weapon_4303802";
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Frame> Completed
        = new ConditionalWeakTable<BattleDeclaration, Frame>();
    private sealed class Frame : IDisposable
    {
        internal Frame Previous;
        internal BattleEntity Owner;
        internal string Effect;
        internal long Execution;
        internal StatusInstance Status;
        internal bool Prepared, Eligible, Closed, Invocation;
        internal DamageResolvedEvent Last;
        public void Dispose()
        {
            try { Complete(this); }
            finally { Current.Value = Previous; }
        }
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterPreparedBaseDamageEffect(ID, 430380101);
        BattleSpecialRules.RegisterEffectInvocationScope((owner, effect, execution) =>
            Begin(owner, effect, execution, true));
        BattleSpecialRules.RegisterDamageEffectScope((owner, effect, execution) =>
        {
            var frame = Current.Value;
            return frame != null && frame.Invocation && frame.Owner == owner && frame.Effect == effect
                && frame.Execution == execution ? null : Begin(owner, effect, execution, false);
        }, (owner, effect) =>
        {
            var frame = Current.Value;
            if (frame?.Owner == owner && frame.Effect == effect && !frame.Invocation) Complete(frame);
        });
        BattleSpecialRules.RegisterDamagePreparation(ID, Prepare);
        BattleSpecialRules.RegisterNumberedCondition("4303801", (_, c) =>
        {
            var frame = Current.Value;
            return frame != null && !frame.Closed && frame.Eligible && c?.Status == frame.Status
                && c.DamageType == "Skill" && c.HitEffectID == frame.Effect
                && BattleEventHub.CurrentEffectExecutionId == frame.Execution && Valid(frame);
        });
        BattleSpecialRules.RegisterNumberedCondition("4303802", (_, c) => c?.Declaration != null
            && Completed.TryGetValue(c.Declaration, out var frame) && c.Status == frame.Status
            && c.EventHookName == "PostSelfDamage" && frame.Eligible && Valid(frame));
    }
    private static Frame Begin(BattleEntity owner, string effect, long execution, bool invocation)
    {
        var frame = new Frame { Previous = Current.Value, Owner = owner, Effect = effect,
            Execution = execution, Invocation = invocation };
        Current.Value = frame;
        return frame;
    }
    private static bool Valid(Frame frame)
        => frame.Status != null && frame.Status.IsActive && frame.Owner?.IsAlive == true
            && frame.Owner.HasStatusInstance(frame.Status) && EquipmentStatusLifetime.CanExecute(frame.Status);

    private static int UsedCount(StatusInstance source, Frame current)
    {
        int count = 0;
        foreach (var status in source.WeaponContext.Equipper.StatusDict.GetAll(Used))
            if (status.IsActive && status.Caster == source.Caster
                && status.WeaponContext?.SourceInstanceID == source.WeaponContext.SourceInstanceID)
                count += Math.Max(1, status.StackCount); // 普通状态是多实例，不是把StackCount写成2。
        // 父效果还没结束时触发嵌套Skill伤害，也不能重复占尚未落表的同一额度。
        for (var parent = current.Previous; parent != null; parent = parent.Previous)
            if (!parent.Closed && parent.Eligible && parent.Owner == current.Owner
                && parent.Status?.WeaponContext?.SourceInstanceID == source.WeaponContext.SourceInstanceID)
                count++;
        return count;
    }

    private static CountDamageContribution Prepare(StatusInstance status, object host, CountDamageContext c)
    {
        var frame = Current.Value;
        var owner = status?.WeaponContext?.Equipper;
        if (frame == null || frame.Closed || owner == null || owner != host || owner != status.Caster
            || status.WeaponContext.WeaponID != 3038 || c.Attacker != owner || c.Target.Side == owner.Side
            || c.DamageType != "Skill" || c.EffectID != frame.Effect || c.EffectExecutionID != frame.Execution
            || (c.SourceKind != ReactionSourceKind.CharacterSkill && c.SourceKind != ReactionSourceKind.StatusEffect))
            return null;
        if (!frame.Prepared)
        {
            frame.Prepared = true; frame.Status = status;
            frame.Eligible = UsedCount(status, frame) < 2;
        }
        if (frame.Status != status || !frame.Eligible || !Valid(frame)) return null;
        if (!DataManager.Instance.StatusEffectDict.TryGetValue("STE_Weapon_430380101", out var effect)
            || effect.EffectType != "Buff" || effect.Param1 != "Index(1)*TotalDEF"
            || effect.DamageHook != "Hook(430380101)")
            throw new InvalidOperationException("辰砂基础加算缺少Index(1)*TotalDEF的专用Buff配置。");
        float bonus = WeaponHooks.Param(status, 1) * owner.GetFinalDEF(c.EffectID, c.SkillID, c.SkillType, c.Element);
        // 与余响等复用唯一基础伤害加算入口，不再把这个Buff重复加入DMGBonus或另发一段伤害。
        return new CountDamageContribution(bonus, result => frame.Last = result, null);
    }
    private static void Complete(Frame frame)
    {
        if (frame.Closed) return;
        frame.Closed = true;
        if (!frame.Eligible || frame.Last?.Declaration == null || !Valid(frame)) return;
        Completed.Add(frame.Last.Declaration, frame);
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(ID, out var actions)) return;
        using var collection = BattleResolutionContext.BeginCollection();
        foreach (var action in actions)
        {
            if (action.ActionType != "OnTrigger" || !ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out string argument)) continue;
            var context = new ScriptHookContext { Caster = frame.Owner, Status = frame.Status, StatusHost = frame.Owner,
                Target = frame.Last.Target, DamageEvent = frame.Last, Declaration = frame.Last.Declaration,
                HitEffectID = frame.Effect, EventHookName = "PostSelfDamage", EventHookArgument = argument };
            frame.Owner.CharacterCtrl.TryExecuteEventStatusAction(frame.Status, action, frame.Owner, context, () => Valid(frame));
        }
    }
}
