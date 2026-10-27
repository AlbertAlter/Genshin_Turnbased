using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3041/3068/3144周期精通快照、3044实际消契合计；只读Buff不在查询时再换算。</summary>
internal static class WeaponSnapshotHooks
{
    private const string Cycle = "ST_Weapon_4304102", BondListener = "ST_Weapon_4304403", Scalar = "WeaponFixedScalar";
    private const string MakhairaCycle = "ST_Weapon_4306802", MakhairaParent = "ST_Weapon_4306803", MakhairaChild = "ST_Weapon_4306804";
    private const string EveningCycle = "ST_Weapon_4314402", EveningParent = "ST_Weapon_4314403", EveningChild = "ST_Weapon_4314404";
    private sealed class Amount { internal readonly float Value; internal Amount(float value) { Value = value; } }
    private sealed class BondState
    {
        internal float Removed;
        internal IDisposable Subscription;
        internal readonly ConditionalWeakTable<BattleDeclaration, Amount> Settlements = new ConditionalWeakTable<BattleDeclaration, Amount>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, BondState> Bonds = new ConditionalWeakTable<StatusInstance, BondState>();
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private sealed class Frame : IDisposable
    {
        internal readonly Frame Previous;
        internal readonly StatusInstance Source;
        internal readonly float Value;
        internal readonly bool Binding;
        private readonly BondState bond;
        private readonly BattleDeclaration declaration;
        internal Frame(StatusInstance source, float value, BondState state = null, BattleDeclaration fact = null, bool binding = false)
        { Previous = Current.Value; Source = source; Value = value; bond = state; declaration = fact; Binding = binding; Current.Value = this; }
        public void Dispose()
        {
            if (bond != null && declaration != null) bond.Settlements.Remove(declaration);
            Current.Value = Previous;
        }
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusActionScope(Begin);
        BattleSpecialRules.RegisterAtomicStatusAction(Cycle);
        BattleSpecialRules.RegisterAtomicStatusAction(MakhairaCycle);
        BattleSpecialRules.RegisterAtomicStatusAction(EveningCycle);
        BattleSpecialRules.RegisterStatusLifecycle(BondListener, StartBond, StopBond);
        BattleSpecialRules.RegisterNumberedCondition("4304403", (hook, c) => c?.Status?.StatusID2 == BondListener
            && c.Declaration?.Get<ResourceEventData>() is ResourceEventData change && change.Resource == "BondOfLife"
            && change.Before > 0d && change.After == 0d && Bonds.TryGetValue(c.Status, out var state)
            && state.Settlements.TryGetValue(c.Declaration, out _));
        BattleSpecialRules.RegisterTargets("Hook(430410202)", (battle, owner) =>
        {
            var source = ScriptHookContext.Current?.Status;
            if (source?.StatusID2 != Cycle || source.WeaponContext?.Equipper != owner || battle == null)
                throw new InvalidOperationException("西福斯队友目标缺少本次周期来源。");
            var positions = new List<int>();
            foreach (var ally in battle.Allies)
                if (ally?.Entity != null && ally.Entity != owner && ally.Entity.IsAlive && ally.Entity.Side == owner.Side)
                    positions.Add(ally.Entity.SlotPosition);
            return positions;
        });
        foreach (var id in new[] { "ST_Weapon_4304103", "ST_Weapon_4304104", "ST_Weapon_4304404" })
            BattleSpecialRules.RegisterStatusLifecycle(id, SaveReward, null);
        BattleSpecialRules.RegisterStatusLifecycle(MakhairaParent, SaveAttackShare, null);
        BattleSpecialRules.RegisterStatusLifecycle(MakhairaChild, SaveAttackShare, null);
        BattleSpecialRules.RegisterStatusLifecycle(EveningParent, SaveAttackShare, null);
        BattleSpecialRules.RegisterStatusLifecycle(EveningChild, SaveAttackShare, null);
    }

    private static IDisposable Begin(StatusInstance source)
    {
        if (source?.StatusID2 == Cycle || source?.StatusID2 == MakhairaCycle || source?.StatusID2 == EveningCycle)
        {
            int weapon = source.StatusID2 == Cycle ? 3041 : source.StatusID2 == MakhairaCycle ? 3068 : 3144;
            var owner = WeaponCountLifetime.RequireOwner(source, source.Host, weapon);
            return new Frame(source, owner.GetFinalEM() * WeaponHooks.Param(source, 1));
        }
        if (source?.StatusID2 != BondListener) return null;
        var declaration = ScriptHookContext.Current?.Declaration;
        if (declaration == null || !Bonds.TryGetValue(source, out var bond) || !bond.Settlements.TryGetValue(declaration, out var amount))
            throw new InvalidOperationException("海渊增攻缺少本次实际消契合计。");
        return new Frame(source, Math.Min(amount.Value * WeaponHooks.Param(source, 2), WeaponHooks.Param(source, 3)), bond, declaration);
    }

    private static void SaveReward(StatusInstance status, object host)
    {
        var frame = Current.Value;
        var context = ScriptHookContext.Current;
        int expectedEffect = status.StatusID2 == "ST_Weapon_4304103" ? 430410201
            : status.StatusID2 == "ST_Weapon_4304104" ? 430410202 : 430440301;
        int weapon = expectedEffect / 10000 - 40000;
        if (!(host is BattleEntity recipient) || frame == null || context?.Status != frame.Source
            || context.ExecutingStatusEffectID != expectedEffect || status.WeaponContext?.WeaponID != weapon
            || status.Caster != frame.Source.Caster || status.WeaponContext.SourceInstanceID != frame.Source.WeaponContext.SourceInstanceID
            || (weapon == 3044 && recipient != status.Caster))
            throw new InvalidOperationException("武器快照状态只能由本次对应的ApplyStatus生成：" + status.StatusID2);
        float value = frame.Value * (expectedEffect == 430410202 ? .3f : 1f);
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("武器固定快照必须为有限数值。");
        if (status.Snapshot == null) status.Snapshot = new Dictionary<string, float>();
        status.Snapshot[Scalar] = value;
    }

    internal static bool HandlesApply(StatusEffectData effect)
        => effect?.EffectType == "ApplyStatus" && effect.DamageHook == "Hook(" + effect.StatusEffectID + ")"
            && (effect.StatusEffectID == 430410201 || effect.StatusEffectID == 430410202 || effect.StatusEffectID == 430440301
                || effect.StatusEffectID == 430680201 || effect.StatusEffectID == 431440201);

    internal static bool TryResolve(StatusEffectData effect, StatusInstance source, out float value)
    {
        value = 0f;
        if (effect?.EffectType != "Buff" || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")"
            || (effect.StatusEffectID != 430410301 && effect.StatusEffectID != 430410401 && effect.StatusEffectID != 430440401
                && effect.StatusEffectID != 430680301 && effect.StatusEffectID != 430680401
                && effect.StatusEffectID != 431440301 && effect.StatusEffectID != 431440401)) return false;
        if (source?.MainData?.StatusID != effect.StatusEffectID / 100 || source.Snapshot == null || !source.Snapshot.TryGetValue(Scalar, out value))
            throw new InvalidOperationException("武器属性聚合缺少施加时快照：" + effect.StatusEffectID);
        return true; // 合法0也是已处理，不退回原Index常量。
    }

    private static bool IsAttackShareBinding(StatusInstance parent, StatusEffectData effect)
        => (parent?.StatusID2 == MakhairaParent && effect?.StatusEffectID == 430680302)
            || (parent?.StatusID2 == EveningParent && effect?.StatusEffectID == 431440302);

    /// <summary>首次绑定与以后补建都从所属父实例取A，不依赖周期行动仍在执行。</summary>
    internal static IDisposable BeginBinding(StatusInstance parent, StatusEffectData effect)
    {
        if (!IsAttackShareBinding(parent, effect)) return null;
        bool evening = parent.StatusID2 == EveningParent;
        if (effect.EffectType != "BindStatus" || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")"
            || effect.Param1 != (evening ? EveningChild : MakhairaChild)
            || parent.WeaponContext?.WeaponID != (evening ? 3144 : 3068) || parent.Caster != parent.WeaponContext.Equipper
            || parent.Snapshot == null || !parent.Snapshot.TryGetValue(Scalar, out float value))
            throw new InvalidOperationException("精通转固定攻击的绑定缺少本父实例快照。");
        return new Frame(parent, value, binding: true);
    }

    internal static bool HasExistingBinding(StatusInstance parent, StatusEffectData effect, BattleEntity target)
    {
        if (!IsAttackShareBinding(parent, effect)) return false;
        string childID = parent.StatusID2 == EveningParent ? EveningChild : MakhairaChild;
        foreach (var child in parent.BoundStatuses)
            if (child.IsActive && child.StatusID2 == childID && ReferenceEquals(child.Host, target)
                && target.HasStatusInstance(child)) return true;
        return false;
    }

    private static void SaveAttackShare(StatusInstance status, object host)
    {
        var frame = Current.Value;
        bool evening = status.StatusID2 == EveningParent || status.StatusID2 == EveningChild;
        bool child = status.StatusID2 == MakhairaChild || status.StatusID2 == EveningChild;
        string parentID = evening ? EveningParent : MakhairaParent, cycleID = evening ? EveningCycle : MakhairaCycle;
        var owner = status.WeaponContext?.Equipper;
        if (!(host is BattleEntity recipient) || frame == null || status.WeaponContext?.WeaponID != (evening ? 3144 : 3068)
            || owner == null || status.Caster != owner || frame.Source.Caster != owner
            || status.WeaponContext.SourceInstanceID != frame.Source.WeaponContext.SourceInstanceID
            || frame.Binding != child || frame.Source.StatusID2 != (child ? parentID : cycleID)
            || (child ? recipient == owner : recipient != owner) || recipient.Side != owner.Side
            || (!child && ScriptHookContext.Current?.ExecutingStatusEffectID != (evening ? 431440201 : 430680201)))
            throw new InvalidOperationException("精通转固定攻击的快照必须保留本周期或所属父实例的来源。");
        float value = frame.Value * (child ? .3f : 1f);
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("精通转固定攻击的快照必须为有限数值。");
        if (status.Snapshot == null) status.Snapshot = new Dictionary<string, float>();
        status.Snapshot[Scalar] = value;
    }

    private static bool Live(StatusInstance status, BattleEntity owner)
        => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status)
            && BattleManager.Instance?.IsBattleRunning == true && !BattleManager.Instance.IsBattleOver;

    private static void StartBond(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3044);
        var state = new BondState(); Bonds.Add(status, state);
        state.Subscription = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, change) =>
        {
            if (change.Resource != "BondOfLife" || change.PoolId != BondOfLifeStatusHandler.StatusID || change.Subject != owner || change.Removed <= 0d) return;
            state.Removed += (float)change.Removed;
            if (change.Before <= 0d || change.After != 0d) return;
            var declaration = BattleEventHub.Current;
            state.Settlements.Add(declaration, new Amount(state.Removed)); state.Removed = 0f;
            bool submitted = false;
            if (DataManager.Instance.StatusActionDict.TryGetValue(BondListener, out var actions))
                foreach (var action in actions)
                    if (action.ActionType == "OnTrigger" && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out var hook) && hook == "4304403")
                        submitted |= owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                        { Caster = owner, Status = status, StatusHost = owner, Target = owner, Declaration = declaration }, () => Live(status, owner));
            if (!submitted) state.Settlements.Remove(declaration);
        }, status.ApplyOrder, () => Live(status, owner));
    }

    private static void StopBond(StatusInstance status, object host)
    {
        if (!Bonds.TryGetValue(status, out var state)) return;
        state.Subscription?.Dispose(); Bonds.Remove(status);
    }
}
