using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>万叶的本次施放转化、实际战技命中范围及实时精通增伤；执行与声明仍走公共入口。</summary>
public static class KazuhaScriptHooks
{
    public const int CharacterID = 1044;
    public const string Rule = "ST_Kazuha_Rule", Field = "ST_Kazuha_Autumn", Soumon = "ST_Kazuha_Soumon";
    private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo" };
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<string> Executing = new AsyncLocal<string>();
    private sealed class Restore : IDisposable
    {
        private Action restore;
        internal Restore(Action action) { restore = action; }
        public void Dispose() { var action = restore; restore = null; action?.Invoke(); }
    }
    private sealed class RangeMemory { internal List<BattleEntity> Targets; }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly bool LowEnergy;
        internal readonly HashSet<BattleEntity> Hits = new HashSet<BattleEntity>();
        internal List<BattleEntity> Targets = new List<BattleEntity>();
        internal string Absorbed = string.Empty;
        internal bool Locked;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner = owner; Skill = skill.SkillID2; LowEnergy = owner.CurrentEnergy < 45f; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private static Cast Current(BattleEntity owner) => Casting.Value?.Owner == owner ? Casting.Value : null;
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    private static bool Enemy(BattleManager battle, BattleEntity owner, BattleEntity target)
        => target?.IsOnField == true && target.Side != owner.Side
            && battle?.GetEntityByPosition(target.Side, target.SlotPosition) == target;
    private static bool SkillCast(ScriptHookContext context)
        => context?.Caster?.EntityID == CharacterID && Current(context.Caster)?.Skill == "SK_Skill_Kazuha";
    private static bool Affected(BattleEntity target, string element)
        => ElementQuery.Capture(target).Any(pool => pool.Element == element && pool.IsPresent)
            || ElementQuery.ReadShield(target, element)?.IsPresent == true;

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => owner?.EntityID == CharacterID && skill != null
            ? new Cast(owner, skill) : new Restore(null));
        BattleSpecialRules.RegisterEffectInvocationScope((owner, effect, _) => {
            string previous = Executing.Value; Executing.Value = effect;
            return new Restore(() => Executing.Value = previous);
        });
        BattleSpecialRules.RegisterHitScope((_, owner, targets) => {
            var cast = Current(owner);
            if (cast == null || cast.Locked || !((cast.Skill == "SK_Skill_Kazuha" && Executing.Value == "SE_Skill_Kazuha")
                || (cast.Skill == "SK_Burst_Kazuha" && Executing.Value == "SE_Burst_Kazuha"))) return new Restore(null);
            cast.Locked = true;
            cast.Targets = targets.Where(t => Enemy(BattleManager.Instance, owner, t)).Distinct().ToList();
            foreach (string element in Elements)
                if (Affected(owner, element) || cast.Targets.Any(t => Affected(t, element)))
                { cast.Absorbed = element; break; }
            return new Restore(null);
        });
        BattleSpecialRules.RegisterDeclarationObserver(declaration => {
            var cast = Casting.Value;
            if (cast?.Skill != "SK_Skill_Kazuha" || declaration?.Get<DamageEventData>() is not DamageEventData hit
                || !hit.HitLanded || !hit.Source.CountsAsHit || hit.Source.SourceEntity != cast.Owner
                || hit.Source.SourceEffectID != "SE_Skill_Kazuha" || hit.Subject?.Side == cast.Owner.Side) return;
            cast.Hits.Add(hit.Subject);
        });
        BattleSpecialRules.RegisterTargets("Kazuha_SkillMinions()", (battle, owner) => {
            var cast = Current(owner);
            return cast?.Skill == "SK_Skill_Kazuha" ? cast.Hits.Where(t => Enemy(battle, owner, t)
                && DataManager.Instance.EnemyMainDict.TryGetValue(t.EntityID, out var identity)
                && string.Equals(identity.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.SlotPosition).OrderBy(p => p).ToList() : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("Kazuha_Absorbed", (a, c) => Empty(a) && SkillCast(c)
            && !string.IsNullOrEmpty(Current(c.Caster).Absorbed));
        BattleSpecialRules.RegisterCondition("Kazuha_C4Converted", (a, c) => Empty(a) && SkillCast(c)
            && Current(c.Caster).LowEnergy && !string.IsNullOrEmpty(Current(c.Caster).Absorbed));
        BattleSpecialRules.RegisterCondition("Kazuha_C4Unconverted", (a, c) => Empty(a) && SkillCast(c)
            && Current(c.Caster).LowEnergy && string.IsNullOrEmpty(Current(c.Caster).Absorbed));
        BattleSpecialRules.RegisterCondition("Kazuha_Swirled", (element, c) => Array.IndexOf(Elements, element) >= 0
            && c?.Caster?.EntityID == CharacterID && c.ReactionEvent?.SourceEntity == c.Caster
            && c.ReactionEvent.Type == ReactionType.Swirl && c.ReactionEvent.InvolvedElements.Contains(element));
        BattleSpecialRules.RegisterStatusLifecycle(Field, (state, host) => Capture(state, host, "SK_Burst_Kazuha", false), null);
        BattleSpecialRules.RegisterStatusLifecycle(Soumon, (state, host) => Capture(state, host, "SK_Skill_Kazuha", true), null);
        BattleSpecialRules.RegisterTargets("Kazuha_SoumonTargets()", (battle, owner) => {
            var state = ScriptHookContext.Current?.Status;
            return state?.StatusID2 == Soumon && state.Caster == owner && state.ScriptRuntimeState is RangeMemory memory
                ? memory.Targets.Where(t => Enemy(battle, owner, t)).Select(t => t.SlotPosition).ToList() : new List<int>();
        });
        BattleSpecialRules.RegisterStatusDamage("Kazuha_AbsorbedDamage()", context => {
            if (!Active(context.Status) || context.Status.Caster?.EntityID != CharacterID
                || context.Status.StatusID2 is not (Field or Soumon)) return Task.CompletedTask;
            string element = context.Status.RuntimeElement;
            return Array.IndexOf(Elements, element) < 0 ? Task.CompletedTask
                : context.ExecuteDefault(context.Effect.WithDamageElement(element));
        });
        BattleSpecialRules.RegisterBuffScalar("Kazuha_EMBonus()", (effect, state) => {
            if (state.Caster?.EntityID != CharacterID || !(state.StatusID2 == "ST_Kazuha_C6"
                || Elements.Any(e => state.StatusID2 == "ST_Kazuha_" + e + "Bonus")))
                throw new InvalidOperationException("万叶精通增伤缺少正确的来源状态。");
            return BuffScalarParser.Evaluate(effect.Param1, readAttribute: name => name == "TotalEM"
                ? state.Caster.GetFinalEM() : throw new FormatException("万叶增伤只读取来源TotalEM：" + name));
        });
    }
    private static void Capture(StatusInstance state, object host, string skill, bool needsAbsorption)
    {
        var cast = Current(state.Caster);
        if (state.Caster?.EntityID != CharacterID || !ReferenceEquals(host, state.Caster) || cast?.Skill != skill
            || (needsAbsorption && string.IsNullOrEmpty(cast.Absorbed)))
            throw new InvalidOperationException("万叶转化状态缺少本次施放记录。");
        // 只保存元素和具体目标身份，不保存攻击/精通等面板快照。
        state.RuntimeElement = cast.Absorbed;
        if (needsAbsorption) state.ScriptRuntimeState = new RangeMemory { Targets = cast.Targets.ToList() };
    }
}
