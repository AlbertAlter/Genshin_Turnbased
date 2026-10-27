using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3037/3046/3072普通状态的按实例转存；不改成Count、不另发资源声明。</summary>
internal static class WeaponStackTransferHooks
{
    private const string Pending = "ST_Weapon_4303703";
    private sealed class Receipt { internal bool Paid; }
    private static readonly ConditionalWeakTable<StatusInstance, Receipt> Receipts = new ConditionalWeakTable<StatusInstance, Receipt>();
    private static readonly AsyncLocal<Transfer> Current = new AsyncLocal<Transfer>();

    private sealed class Transfer : IDisposable
    {
        internal readonly Transfer Previous;
        internal readonly StatusInstance Source;
        internal readonly BattleEntity Owner;
        internal readonly List<StatusInstance> Marks;
        internal readonly bool IsPayout;
        internal readonly int Layers;
        internal Transfer(StatusInstance source, string markID, bool payout)
        {
            Previous = Current.Value; Source = source; Owner = source.WeaponContext.Equipper;
            IsPayout = payout; Marks = Capture(source, markID);
            foreach (var mark in Marks) Layers = checked(Layers + Math.Max(1, mark.StackCount));
            Current.Value = this;
        }
        public void Dispose() { Current.Value = Previous; }
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(Pending,
            (status, host) => { WeaponCountLifetime.RequireOwner(status, host, 3037); Receipts.Add(status, new Receipt()); },
            (status, host) => Receipts.Remove(status));
        foreach (string id in new[] { "ST_Weapon_4303701", "ST_Weapon_4304603", "ST_Weapon_4307203", Pending })
            BattleSpecialRules.RegisterAtomicStatusAction(id);
        BattleSpecialRules.RegisterStatusActionScope(Begin);
        RegisterTransfer("4303701", "ST_Weapon_4303702", "Burst", "430370103", "430370104");
        RegisterTransfer("4304603", "ST_Weapon_4304602", "Skill;Burst", "430460302", "430460303");
        RegisterTransfer("4307203", "ST_Weapon_4307202", "Skill;Burst", "430720302", "430720303");
        BattleSpecialRules.RegisterNumberedCondition("4303703", (_, c) => c?.Status?.StatusID2 == Pending
            && BattleManager.Instance?.CurrentPhase == TurnPhase.AllyPostTurn
            && Receipts.TryGetValue(c.Status, out var receipt) && !receipt.Paid && !Reserved(c.Status));
    }

    private static void RegisterTransfer(string hook, string mark, string actions, string second, string third)
    {
        BattleSpecialRules.RegisterNumberedCondition(hook, (_, c) => MatchesAction(c?.Status, c, actions)
            && Capture(c.Status, mark).Count > 0);
        BattleSpecialRules.RegisterNumberedCondition(second, (_, c) => Frame(c?.Status)?.Layers >= 2);
        BattleSpecialRules.RegisterNumberedCondition(third, (_, c) => Frame(c?.Status)?.Layers >= 3);
    }

    private static bool MatchesAction(StatusInstance status, ScriptHookContext context, string actions)
    {
        var fact = context?.Declaration?.Get<ActionEventData>();
        return status?.WeaponContext?.Equipper != null && status.Caster == status.WeaponContext.Equipper
            && context?.Declaration?.Kind == BattleEventKind.ActionStarted && fact?.Subject == status.Caster
            && Array.IndexOf(actions.Split(';'), fact.ActionType) >= 0;
    }

    private static IDisposable Begin(StatusInstance source)
    {
        switch (source?.StatusID2)
        {
            case "ST_Weapon_4303701":
                return MatchesAction(source, ScriptHookContext.Current, "Burst") ? new Transfer(source, "ST_Weapon_4303702", false) : null;
            case "ST_Weapon_4304603":
                return MatchesAction(source, ScriptHookContext.Current, "Skill;Burst") ? new Transfer(source, "ST_Weapon_4304602", false) : null;
            case "ST_Weapon_4307203":
                return MatchesAction(source, ScriptHookContext.Current, "Skill;Burst") ? new Transfer(source, "ST_Weapon_4307202", false) : null;
            case Pending:
                return new Transfer(source, Pending, true);
            default: return null;
        }
    }

    private static Transfer Frame(StatusInstance source)
        => Current.Value != null && ReferenceEquals(Current.Value.Source, source) ? Current.Value : null;

    private static bool Reserved(StatusInstance mark)
    {
        for (var frame = Current.Value; frame != null; frame = frame.Previous)
            if (frame.Marks.Contains(mark)) return true;
        return false;
    }

    private static List<StatusInstance> Capture(StatusInstance source, string id)
    {
        var result = new List<StatusInstance>();
        var owner = source?.WeaponContext?.Equipper;
        if (owner == null || source.Caster != owner) return result;
        foreach (var mark in owner.GetStatusInstances(id))
            if (mark.IsActive && mark.Caster == owner && mark.WeaponContext?.WeaponID == source.WeaponContext.WeaponID
                && mark.WeaponContext?.SourceInstanceID == source.WeaponContext.SourceInstanceID
                && EquipmentStatusLifetime.CanExecute(mark) && !Reserved(mark)
                && (id != Pending || Receipts.TryGetValue(mark, out var receipt) && !receipt.Paid)) result.Add(mark);
        return result;
    }

    internal static bool HandlesEnergy(StatusEffectData effect)
        => effect?.EffectType == "GainEnergy" && effect.DamageHook == "Hook(" + effect.StatusEffectID + ")"
            && (effect.StatusEffectID == 430370301 || effect.StatusEffectID == 430460304 || effect.StatusEffectID == 430720304);

    internal static bool TryResolveEnergy(StatusEffectData effect, StatusInstance source, out float value)
    {
        value = 0f;
        if (!HandlesEnergy(effect)) return false;
        var frame = Frame(source);
        int weapon = effect.StatusEffectID / 10000 - 40000;
        if (frame == null || source.WeaponContext.WeaponID != weapon
            || frame.IsPayout != (weapon == 3037) || source.MainData?.StatusID != effect.StatusEffectID / 100)
            throw new InvalidOperationException("转层回能必须使用本次本来源的标记实例快照。");
        value = frame.Layers * WeaponHooks.Param(source, weapon == 3037 ? 1 : 2);
        return true;
    }

    /// <summary>统一能量入口实际提交后记录收据；参数查询本身不能把待回能标记成已支付。</summary>
    internal static void EnergyCommitted(StatusEffectData effect, StatusInstance source, BattleEntity target)
    {
        if (effect?.StatusEffectID != 430370301) return;
        var frame = Frame(source);
        if (frame == null || !frame.IsPayout || target != frame.Owner)
            throw new InvalidOperationException("天目待回能缺少本次统一GainEnergy提交。");
        foreach (var mark in frame.Marks)
            if (Receipts.TryGetValue(mark, out var receipt)) receipt.Paid = true;
    }

    internal static bool CanExecute(StatusEffectData effect, StatusInstance source)
    {
        int minimum;
        switch (effect?.StatusEffectID)
        {
            case 430370102: case 430460301: case 430720301: minimum = 1; break;
            case 430370103: case 430460302: case 430720302: minimum = 2; break;
            case 430370104: case 430460303: case 430720303: minimum = 3; break;
            case 430370301: case 430460304: case 430720304: minimum = 1; break;
            default: return true;
        }
        return Frame(source)?.Layers >= minimum;
    }

    internal static bool TryRemove(StatusEffectData effect, StatusInstance source, BattleEntity target)
    {
        if (effect?.StatusEffectID != 430370105 && effect?.StatusEffectID != 430460305 && effect?.StatusEffectID != 430720305) return false;
        var frame = Frame(source);
        if (frame == null || frame.IsPayout || target != frame.Owner)
            throw new InvalidOperationException("消耗标记必须使用本次转层的实例快照。");
        foreach (var mark in frame.Marks) target.RemoveStatusInstance(mark, "WeaponMarkConsumed");
        return true;
    }
}
