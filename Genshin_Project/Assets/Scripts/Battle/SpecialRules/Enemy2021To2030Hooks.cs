using System;
using System.Collections.Generic;

/// <summary>第三批敌人的资格、固定目标与Count规则；数值效果仍由正式技能/状态表执行。</summary>
public static class Enemy2021To2030Hooks
{
    public const string Growth = "ST_LargeDendroSlime_Growth", Charge = "ST_MutantElectroSlime_Charge";
    public const string DischargeClear = "SE_MutantElectroSlime_DischargeClear";
    private static string FirePrefix(BattleEntity entity) => entity?.EntityID == 2021 ? "ST_PyroSlime_" : "ST_LargePyroSlime_";
    internal static void Register()
    {
        BattleSpecialRules.RegisterCondition("SlimeNeedsIgnition", (_, c) => c?.Caster?.IsAlive == true
            && (c.Caster.GetAura("Pyro")?.AuraAmount ?? 0) > 0 && !c.Caster.HasStatus(FirePrefix(c.Caster) + "Ignited"));
        BattleSpecialRules.RegisterCondition("SlimeNeedsQuench", (_, c) => c?.Caster?.IsAlive == true
            && (c.Caster.GetAura("Pyro")?.AuraAmount ?? 0) <= 0 && c.Caster.HasStatus(FirePrefix(c.Caster) + "Ignited"));
        foreach (int id in new[] { 2021, 2028 })
        {
            int enemy = id;
            BattleSpecialRules.RegisterNumberedCondition((id * 100 + 1).ToString(), (_, c) => c?.Caster?.HasStatus(FirePrefix(c.Caster) + "Ignited") == true);
            BattleSpecialRules.RegisterNumberedCondition((id * 100 + 2).ToString(), (_, c) => c?.Caster?.HasStatus(FirePrefix(c.Caster) + "Extinguished") == true);
            if (id == 2028) BattleSpecialRules.RegisterNumberedCondition("202803", (_, c) => c?.Caster?.HasStatus("ST_LargePyroSlime_Ignited") == true);
            string waiting = id == 2021 ? "ST_PyroSlime_Extinguished" : "ST_LargePyroSlime_Extinguished";
            BattleSpecialRules.RegisterStatusLifecycle(waiting, (s, h) => {
                if (BattleManager.Instance != null) s.EarliestTimerTurn = BattleManager.Instance.TurnCount + 1;
            }, null);
        }
        BattleSpecialRules.RegisterTargets("SlimeDeathNeighbors()", (b, c) => DeathNeighbors(b, ScriptHookContext.Current?.Target));
        BattleSpecialRules.RegisterNumberedCondition("202302", (_, c) => c?.Caster?.GetEnemyElementalShield() != null);
        BattleSpecialRules.RegisterNumberedCondition("202402", (_, c) => c?.Caster != null && !c.Caster.HasStatus(Growth));
        BattleSpecialRules.RegisterNumberedCondition("202403", (_, c) => c?.Caster != null && !c.Caster.HasStatus("ST_LargeDendroSlime_Poison"));
        BattleSpecialRules.RegisterNumberedCondition("202902", (_, c) => Count(c?.Caster, Charge) >= 5);
        BattleSpecialRules.RegisterNumberedCondition("203003", (_, c) => c?.Caster != null && c.Caster.GetEnemyElementalShield() == null
            && !c.Caster.HasStatus("ST_CryoMage_Icicles"));
        BattleSpecialRules.RegisterCondition("EnemyShieldBroken", (_, c) => c?.Declaration?.Get<StatusEventData>()?.Reason == "ShieldBroken");
        BattleSpecialRules.RegisterCondition("SlimeCanGrow", (_, c) => Count(c?.Caster, Growth) > 0 && EmptyEnemyPositions(BattleManager.Instance).Count > 0);
        BattleSpecialRules.RegisterCondition("SlimeSummoned", (_, c) => (c?.Status?.CountRuntimeState as CounterMemory)?.Summoned == true);
        BattleSpecialRules.RegisterCondition("SlimeGrowthEmpty", (_, c) => Count(c?.Caster, Growth) == 0);
        BattleSpecialRules.RegisterCondition("MutantElectroRecipient", (_, c) => c?.Caster?.IsAlive == true && c.Recipient != null
            && c.Recipient.Side == c.Caster.Side && (c.Recipient.EntityID == 2018 || c.Recipient.EntityID == 2025));
        BattleSpecialRules.RegisterCondition("MutantChargeAction", (_, c) => IsChargeAction(c?.Status, c?.Declaration?.Get<ActionEventData>()));
        BattleSpecialRules.RegisterCount("MutantDischargeCount", c => (c?.GetStatus(Charge)?.CountRuntimeState as CounterMemory)?.DischargeCount ?? 0);
        BattleSpecialRules.RegisterCondition("CryoMageRetaliation", (_, c) => Attacker(c)?.IsAlive == true);
        BattleSpecialRules.RegisterTargets("CryoMageAttacker()", (b, c) => {
            var target = Attacker(ScriptHookContext.Current);
            return target?.IsAlive == true && b?.GetEntityByPosition(target.Side, target.SlotPosition) == target
                ? new List<int> { target.SlotPosition } : new List<int>();
        });
    }

    public static int Count(BattleEntity entity, string id)
    { var s = entity?.GetStatus(id); return s?.HasIntegerRuntimeValue == true ? s.RuntimeIntValue : 0; }
    private static BattleEntity Attacker(ScriptHookContext c)
    {
        var damage = c?.DamageEvent;
        var source = damage?.Source?.SourceEntity;
        return damage?.HitLanded == true && damage.Target == c.Caster && source?.Side == BattleSide.Ally ? source : null;
    }
    public static List<int> DeathNeighbors(BattleManager battle, BattleEntity dead)
    {
        var result = new List<int>();
        if (battle == null || dead == null) return result;
        foreach (int slot in new[] { dead.SlotPosition - 1, dead.SlotPosition + 1 })
            if (battle.GetEntityByPosition(dead.Side, slot)?.IsAlive == true) result.Add(slot);
        return result;
    }
    public static List<int> EmptyEnemyPositions(BattleManager battle)
    {
        var result = new List<int>();
        if (battle?.Field != null) foreach (var field in battle.Field.EnemySlots)
            if (field != null && field.Occupant?.IsAlive != true) result.Add(field.SlotIndex);
        return result;
    }
    public static void Summon(BattleEntity caster, StatusInstance source, string enemyID)
    {
        var memory = source?.CountRuntimeState as CounterMemory;
        if (memory != null) memory.Summoned = false;
        if (!int.TryParse(enemyID, out int id) || id <= 0) throw new FormatException("Summon.Param1须为敌人ID。");
        var battle = BattleManager.Instance;
        var free = EmptyEnemyPositions(battle);
        if (caster?.IsAlive != true || free.Count == 0) return;
        int position = free[BattleRandom.NextInt(0, free.Count)];
        bool success = battle.SummonEnemy(id, caster.Level, position) != null;
        if (memory != null) memory.Summoned = success;
    }
    public static void ClearAuras(BattleEntity target)
    {
        if (target == null) return;
        using var collection = BattleResolutionContext.BeginCollection();
        using var elements = ElementEventScope.Begin("ReignitionClear", target);
        foreach (string element in ElementQuery.Elements)
        {
            target.RemoveAura(element);
            string intrinsic = ElementQuery.IntrinsicStatusID(element);
            if (!string.IsNullOrEmpty(intrinsic)) target.RemoveStatus(intrinsic);
        }
        foreach (var type in new[] { ReactionType.Frozen, ReactionType.Burning, ReactionType.Quicken })
            ReactionStateSystem.RemoveEntityState(target, type);
    }
    private static bool IsChargeAction(StatusInstance state, ActionEventData action)
    {
        var source = action?.Subject;
        return state?.Caster?.IsAlive == true && source != null && source.Side == state.Caster.Side
            && (source == state.Caster || source.EntityID == 2018 || source.EntityID == 2025);
    }
    internal sealed class CounterMemory : IDisposable
    {
        internal IDisposable Listener;
        internal int DischargeCount;
        internal bool Summoned;
        public void Dispose() { Listener?.Dispose(); Listener = null; }
    }
    internal sealed class Counter : CountStatusHandler
    {
        private readonly int initial, maximum;
        internal Counter(int initial, int maximum) { this.initial = initial; this.maximum = maximum; }
        public override bool SupportsEffectChange => true;
        public override bool EffectUsesIntegerValue => true;
        public override void OnCreated(StatusInstance state, object host)
        {
            state.RuntimeIntValue = initial;
            var memory = new CounterMemory(); state.CountRuntimeState = memory;
            if (state.StatusID2 != Charge) return;
            memory.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, action) => {
                if (!IsChargeAction(state, action)) return;
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusActionDict.TryGetValue(Charge, out var rows)) return;
                foreach (var row in rows)
                    if (row.ActionType == "OnTrigger" && row.ScriptHook == "MutantChargeAction()")
                        state.Caster.EnemyCtrl.TryExecuteEventStatusAction(state, row, host,
                            new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = host,
                                Target = action.Subject, Declaration = BattleEventHub.Current });
            }, state.ApplyOrder, () => state.IsActive && state.Caster?.IsAlive == true);
        }
        public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication request) => state;
        public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
        {
            if (state.StatusID2 == Charge && request.EffectID == DischargeClear)
                ((CounterMemory)state.CountRuntimeState).DischargeCount = state.RuntimeIntValue;
            request.ApplyInteger(state, host, 0, maximum);
        }
        public override void OnRemoved(StatusInstance state, object host) => (state.CountRuntimeState as CounterMemory)?.Dispose();
    }
}
