using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>宵宫专属来源、施放快照、蓄力位置与火光继承。伤害、获层、回能和冷却均执行源表效果。</summary>
public static class YoimiyaScriptHooks
{
    public const int CharacterID = 1042;
    public const string Rule = "ST_Yoimiya_Rule", Dance = "ST_Yoimiya_Niwabi", Combo = "ST_Yoimiya_Combo";
    public const string Pyro = "ST_Yoimiya_T1Pyro", Blaze = "ST_Yoimiya_AurousBlaze", Explosions = "ST_Yoimiya_Explosions";
    private const string A4 = "ST_Yoimiya_A4ATK";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<StatusInstance> Transfer = new AsyncLocal<StatusInstance>();
    private static readonly ConditionalWeakTable<StatusInstance, BlazeBudget> Budgets = new ConditionalWeakTable<StatusInstance, BlazeBudget>();
    private sealed class BlazeBudget { internal int Turn, Used; }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    private static readonly IDisposable Empty = new EmptyScope();
    private sealed class TransferScope : IDisposable
    {
        private readonly StatusInstance previous = Transfer.Value;
        internal TransferScope(StatusInstance state) { Transfer.Value = state; }
        public void Dispose() => Transfer.Value = previous;
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly bool Enhanced;
        internal readonly float NormalScale;
        internal readonly int PyroStacks, Arrows;
        internal readonly StatusInstance Marker;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill.SkillID2;
            Enhanced = owner.GetStatusInstances(Dance).Any(Active);
            PyroStacks = owner.GetStatusInstances(Pyro).Count(Active);
            Marker = FindMarker(owner);
            Arrows = Marker == null ? 0 : Marker.StatusID2[Marker.StatusID2.Length - 1] - '0';
            NormalScale = 1f;
            if (skill.ActionType == "Normal")
            {
                if (Enhanced && !owner.CharacterCtrl.TryGetSkillLevelScalar("Yoimiya_NiwabiScale", 2, out NormalScale))
                    throw new InvalidOperationException("宵宫缺少战技强化等级倍率。");
                NormalScale += .1f * (owner.GetStatus(Combo)?.RuntimeIntValue ?? 0);
            }
            Casting.Value = this;
        }
        public void Dispose() => Casting.Value = previous;
    }
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    internal static BattleEntity Require(StatusInstance state, object host)
    {
        if (host is not BattleEntity owner || owner.EntityID != CharacterID || state.Caster != owner)
            throw new InvalidOperationException("宵宫自身计数必须由本人持有。");
        return owner;
    }
    private static bool IsMarker(string id) => id is "ST_Yoimiya_ChargeTarget1" or "ST_Yoimiya_ChargeTarget2" or "ST_Yoimiya_ChargeTarget3";
    private static bool NoArgument(string text) => string.IsNullOrEmpty(text);
    private static Cast Current(BattleEntity owner) => Casting.Value?.Owner == owner ? Casting.Value : null;
    private static StatusInstance FindMarker(BattleEntity owner)
        => BattleManager.Instance?.Field?.EnemySlots.SelectMany(s => s.StatusList)
            .FirstOrDefault(s => s.Caster == owner && IsMarker(s.StatusID2) && Active(s));
    private static bool NormalHit(ScriptHookContext c) => c?.Caster?.EntityID == CharacterID
        && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.SourceEntity == c.Caster
        && c.DamageEvent.Source.CountsAsHit && c.DamageEvent.DamageType == "Normal"
        && c.DamageEvent.Target?.Side != c.Caster.Side
        && c.DamageEvent.Source.SourceEffectID is "SE_Normal_Yoimiya" or "SE_Normal_YoimiyaC6";
    private static bool EnhancedHit(ScriptHookContext c) => NormalHit(c) && Current(c.Caster)?.Enhanced == true;
    private static bool CanBlaze(ScriptHookContext c)
    {
        var hit = c?.DamageEvent;
        if (c?.Status?.StatusID2 != Blaze || !Active(c.Status) || hit?.HitLanded != true
            || hit.Source?.SourceEntity == null || hit.Source.SourceEntity == c.Caster
            || hit.Source.SourceSide != c.Caster.Side || !hit.Source.CountsAsHit
            || !ReferenceEquals(hit.Target, c.Status.Host) || hit.DamageType is not ("Normal" or "Heavy" or "Skill" or "Burst")) return false;
        return Budgets.TryGetValue(c.Status, out var budget) && (budget.Turn != BattleManager.Instance.TurnCount || budget.Used < 2);
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => owner?.EntityID == CharacterID && skill != null ? new Cast(owner, skill) : Empty);
        BattleSpecialRules.RegisterCondition("Yoimiya_NormalHit", (a, c) => NoArgument(a) && NormalHit(c));
        BattleSpecialRules.RegisterCondition("Yoimiya_EnhancedHit", (a, c) => NoArgument(a) && EnhancedHit(c));
        BattleSpecialRules.RegisterCondition("Yoimiya_EnhancedCast", (a, c) => NoArgument(a)
            && Current(c?.Caster)?.Skill == "SK_Normal_Yoimiya" && Current(c.Caster).Enhanced);
        BattleSpecialRules.RegisterCondition("Yoimiya_NoC1", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID && c.Caster.ConstellationLevel < 1);
        BattleSpecialRules.RegisterCondition("Yoimiya_PyroCrit", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Element == "Pyro"
            && c.DamageEvent.Critical?.IsCritical == true && c.DamageEvent.Source?.SourceEntity == c.Caster);
        BattleSpecialRules.RegisterCondition("Yoimiya_BlazeHit", (a, c) => NoArgument(a) && CanBlaze(c));
        BattleSpecialRules.RegisterCondition("Yoimiya_C4Full", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID
            && c.Caster.GetStatus(Explosions)?.RuntimeIntValue >= 3);
        BattleSpecialRules.RegisterCondition("Yoimiya_ExplosionSettled", (a, c) => NoArgument(a)
            && c?.Caster?.EntityID == CharacterID && c.EventHookName == "Yoimiya_ExplosionSettled");
        BattleSpecialRules.RegisterCount("Yoimiya_KindlingCount", owner => Current(owner)?.Skill == "SK_Heavy_YoimiyaRelease" ? Current(owner).Arrows : 0);
        BattleSpecialRules.RegisterDamage("Yoimiya_NormalScale()", (hook, owner, type) => {
            if (owner?.EntityID != CharacterID || type != "Damage") throw new NotSupportedException("宵宫普攻倍率仅用于本人伤害。");
        }, (_, owner, target, hit, level) => {
            var cast = Current(owner);
            if (cast?.Skill != "SK_Normal_Yoimiya") throw new InvalidOperationException("宵宫普攻缺少本次行为快照。");
            return new HitData(hit.Multiplier * cast.NormalScale, hit.ElementAura);
        });
        BattleSpecialRules.RegisterAutomaticSkillTargets(e => e?.SkillEffectID2 is "SE_Heavy_YoimiyaKindling"
            or "SE_Burst_YoimiyaMark" or "SE_Burst_YoimiyaMarkC1");
        BattleSpecialRules.RegisterTargets("Yoimiya_ChargeTarget()", (_, owner) => {
            var marker = Current(owner)?.Marker ?? FindMarker(owner);
            return marker?.Host is FieldPosition field ? new List<int> { field.SlotIndex } : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Yoimiya_Caster()", (_, owner) => owner?.IsAlive == true ? new List<int> { owner.SlotPosition } : new List<int>());
        BattleSpecialRules.RegisterTargets("Yoimiya_OtherAllies()", (battle, owner) => battle.Allies
            .Where(a => a?.Entity?.IsAlive == true && a.Entity != owner).Select(a => a.Entity.SlotPosition).ToList());
        BattleSpecialRules.RegisterTargets("Yoimiya_BlazeArea()", (_, owner) => ScriptHookContext.Current?.StatusHost is BattleEntity target
            ? BattlePositionSystem.GetAdjacentPositions(target.Side, target.SlotPosition, 1) : new List<int>());
        BattleSpecialRules.RegisterTargets("Yoimiya_BlazeNeighbor()", (battle, owner) => {
            if (ScriptHookContext.Current?.StatusHost is not BattleEntity dead) return new List<int>();
            var candidates = BattlePositionSystem.GetAdjacentPositions(dead.Side, dead.SlotPosition, 1)
                .Where(p => p != dead.SlotPosition && battle.GetEntityByPosition(dead.Side, p)?.IsOnField == true).ToList();
            return candidates.Count == 0 ? new List<int>() : new List<int> { candidates[BattleRandom.NextInt(0, candidates.Count)] };
        });
        for (int stage = 1; stage <= 3; stage++)
            BattleSpecialRules.RegisterStatusLifecycle("ST_Yoimiya_ChargeTarget" + stage, ReplaceMarker, null);
        BattleSpecialRules.RegisterStatusLifecycle(Blaze, CreateBlaze, null);
        BattleSpecialRules.RegisterStatusLifecycle(Pyro, (state, host) => {
            var owner = Require(state, host);
            foreach (var previous in owner.GetStatusInstances(Pyro))
                if (Active(previous) && previous.Caster == owner)
                    StatusBindingSystem.ExtendDuration(previous, state.RemainingPhaseCount - previous.RemainingPhaseCount);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(A4, (state, host) => {
            var cast = Current(state.Caster);
            if (state.Caster?.EntityID != CharacterID || host is not BattleEntity target || target.Side != state.Caster.Side || target == state.Caster
                || cast?.Skill != "SK_Burst_Yoimiya") throw new InvalidOperationException("炎昼风物诗须由宵宫本次爆发施加给其他队友。");
            state.Snapshot = new Dictionary<string, float> { ["Yoimiya_A4Value"] = .1f + .01f * cast.PyroStacks };
        }, null);
        BattleSpecialRules.RegisterBuffScalar("Yoimiya_A4Value()", (effect, state) => state.Snapshot != null
            && state.Snapshot.TryGetValue("Yoimiya_A4Value", out float value) ? value : throw new InvalidOperationException("炎昼风物诗缺少施放时层数快照。"));
        BattleSpecialRules.RegisterStatusDamage("Yoimiya_BlazeExplosion()", async execution => {
            await execution.ExecuteDefault(execution.Effect);
            if (execution.Targets.Count == 0 || BattleExecution.IsCancelled || BattleExecution.GameplayEffectsStopped) return;
            var owner = execution.Status.Caster; var root = owner?.GetStatus(Rule);
            if (!Active(root) || !DataManager.Instance.StatusActionDict.TryGetValue(Rule, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "Yoimiya_ExplosionSettled()")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(root, action, owner, new ScriptHookContext {
                        Caster = owner, Status = root, StatusHost = owner,
                        Declaration = ScriptHookContext.Current?.Declaration, EventHookName = "Yoimiya_ExplosionSettled"
                    }, () => Active(root));
        });
        BattleSpecialRules.RegisterEffectInvocationScope((owner, id, execution) => {
            var state = ScriptHookContext.Current?.Status;
            if (owner?.EntityID != CharacterID || state?.StatusID2 != Blaze) return Empty;
            if (id == "STE_Yoimiya_Transfer") return new TransferScope(state);
            if (id == "STE_Yoimiya_Explosion" && Budgets.TryGetValue(state, out var budget))
            {
                int turn = BattleManager.Instance.TurnCount;
                if (budget.Turn != turn) { budget.Turn = turn; budget.Used = 0; }
                budget.Used++;
            }
            return Empty;
        });
    }
    private static void ReplaceMarker(StatusInstance state, object host)
    {
        if (state.Caster?.EntityID != CharacterID || host is not FieldPosition field || field.Side == state.Caster.Side)
            throw new InvalidOperationException("宵宫瞄准状态必须挂在敌方场地位置。");
        foreach (var slot in field.OwnerField.EnemySlots)
            foreach (var old in slot.StatusList.ToArray())
                if (old.Caster == state.Caster && IsMarker(old.StatusID2)) slot.RemoveStatusInstance(old, "Replaced");
    }
    private static void CreateBlaze(StatusInstance state, object host)
    {
        if (state.Caster?.EntityID != CharacterID || host is not BattleEntity target || target.Side == state.Caster.Side)
            throw new InvalidOperationException("琉金火光必须由宵宫施加给敌人。");
        var old = Transfer.Value;
        if (old?.StatusID2 == Blaze && old.Caster == state.Caster && Budgets.TryGetValue(old, out var budget))
        {
            state.RemainingPhaseCount = old.RemainingPhaseCount; state.IsPermanent = old.IsPermanent;
            state.AddInPhase = old.AddInPhase; state.TriggerPhase = old.TriggerPhase;
            Budgets.Add(state, budget);
        }
        else
        {
            Budgets.Add(state, new BlazeBudget());
            foreach (var enemy in BattleManager.Instance.Enemies)
                if (enemy?.Entity != null)
                    foreach (var previous in enemy.Entity.GetStatusInstances(Blaze).ToArray())
                        if (previous.Caster == state.Caster) enemy.Entity.RemoveStatusInstance(previous, "Replaced");
        }
    }
}

/// <summary>倍率命中次数及四命爆炸进度；初值0，公共重新施加，clear只改值。</summary>
public sealed class YoimiyaCounter : CountStatusHandler
{
    private readonly int maximum;
    public YoimiyaCounter(int maximum) { this.maximum = maximum; }
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host) { YoimiyaScriptHooks.Require(state, host); state.RuntimeIntValue = 0; }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); YoimiyaScriptHooks.Require(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, maximum); }
}
