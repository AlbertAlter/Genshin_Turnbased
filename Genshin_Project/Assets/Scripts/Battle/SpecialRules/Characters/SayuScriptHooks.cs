using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>早柚的逐人滚动、单次元素转化及貉的选人。所有数值提交仍委托公共效果执行器。</summary>
public static class SayuScriptHooks
{
    public const int CharacterID = 1043;
    public const string Rule = "ST_Sayu_Rule", Roll = "ST_Sayu_Windwheel", Daruma = "ST_Sayu_Daruma";
    internal const string ManualReturn = "SayuManualReturn";
    private const string RollDamage = "STE_Sayu_RollDamage", KickDamage = "STE_Sayu_KickDamage";
    private static readonly string[] Priority = { "Pyro", "Hydro", "Electro", "Cryo" };
    private static readonly AsyncLocal<DamageFrame> Damage = new AsyncLocal<DamageFrame>();
    private static readonly AsyncLocal<TickFrame> Tick = new AsyncLocal<TickFrame>();
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    private static readonly IDisposable Empty = new EmptyScope();
    private sealed class DamageFrame : IDisposable
    {
        internal readonly StatusInstance Roll;
        internal readonly List<BattleEntity> Targets;
        internal readonly string Absorbed;
        private readonly DamageFrame previous = Damage.Value;
        internal DamageFrame(StatusInstance state, List<BattleEntity> targets)
        { Roll = state; Targets = targets; Absorbed = state.RuntimeElement; Damage.Value = this; }
        public void Dispose() => Damage.Value = previous;
    }
    private sealed class TickFrame : IDisposable
    {
        internal readonly StatusInstance Status;
        internal readonly BattleEntity Patient;
        private readonly TickFrame previous = Tick.Value;
        internal TickFrame(StatusInstance state)
        {
            Status = state;
            Patient = Allies(BattleManager.Instance, state.Caster)
                .Where(a => a.CurrentHP <= .7f * a.GetFinalHP())
                .OrderBy(a => a.CurrentHP / Math.Max(1f, a.GetFinalHP())).ThenBy(a => a.SlotPosition).FirstOrDefault();
            Tick.Value = this;
        }
        public void Dispose() => Tick.Value = previous;
    }
    private static bool Alive(StatusInstance s) => s?.IsActive == true && s.Caster?.IsAlive == true
        && StatusHostAccess.Contains(s.Host, s);
    internal static BattleEntity Require(StatusInstance state, object host)
    {
        if (host is not BattleEntity owner || owner.EntityID != CharacterID || state.Caster != owner)
            throw new InvalidOperationException("早柚自身状态必须由本人持有。");
        return owner;
    }
    private static bool Check(BattleEntity owner, string key)
        => ScriptHookEvaluator.Evaluate("Check(" + key + ")", new ScriptHookContext { Caster = owner });
    private static IEnumerable<BattleEntity> Allies(BattleManager battle, BattleEntity owner)
        => battle == null ? Enumerable.Empty<BattleEntity>() : battle.Allies.Select(a => a?.Entity)
            .Where(e => e?.IsOnField == true && e.Side == owner.Side);
    private static bool Enemy(BattleEntity target, BattleEntity owner) => target?.IsOnField == true
        && target.Side != owner.Side && BattleManager.Instance?.GetEntityByPosition(target.Side, target.SlotPosition) == target;
    private static bool OwnHit(ScriptHookContext c, string effect) => c?.Caster?.EntityID == CharacterID
        && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.SourceEntity == c.Caster
        && c.DamageEvent.Source.CountsAsHit && c.DamageEvent.Source.SourceEffectID == effect
        && c.DamageEvent.Target?.Side != c.Caster.Side;

    internal static void Register()
    {
        BattleSpecialRules.RegisterOffFieldSwitch(CanReturn, owner => owner.RemoveStatusInstance(owner.GetStatus(Roll), ManualReturn));
        BattleSpecialRules.RegisterSkillSettled((owner, skill, _) => {
            if (owner?.EntityID == CharacterID && skill?.SkillID2 == "SK_Skill_Sayu" && Alive(owner.GetStatus(Roll)))
                BattleManager.Instance?.RequestFreeSkillReplacement(owner);
        });
        BattleSpecialRules.RegisterStatusActionScope(s => s?.StatusID2 == Daruma ? new TickFrame(s) : Empty);
        BattleSpecialRules.RegisterStatusLifecycle(Roll, (state, host) => { Require(state, host); state.RuntimeElement = string.Empty; }, null);
        BattleSpecialRules.RegisterCondition("Sayu_RollHit", (a, c) => string.IsNullOrEmpty(a) && OwnHit(c, RollDamage));
        BattleSpecialRules.RegisterCondition("Sayu_KickHit", (a, c) => string.IsNullOrEmpty(a) && OwnHit(c, KickDamage));
        BattleSpecialRules.RegisterCondition("Sayu_OwnReaction", (a, c) => string.IsNullOrEmpty(a)
            && c?.Caster?.EntityID == CharacterID && c.ReactionEvent?.SourceEntity == c.Caster);
        BattleSpecialRules.RegisterCondition("Sayu_OffField", (a, c) => string.IsNullOrEmpty(a)
            && c?.Caster?.EntityID == CharacterID && c.Caster.IsOffField);
        BattleSpecialRules.RegisterCondition("Sayu_ManualReturn", (a, c) => string.IsNullOrEmpty(a)
            && c?.Status?.StatusID2 == Roll && c.Caster?.IsAlive == true
            && c.Declaration?.Get<StatusEventData>() is StatusEventData fact
            && fact.Kind == BattleEventKind.StatusRemoved && fact.Reason == ManualReturn
            && fact.InstanceId == c.Status.ApplyOrder && ReferenceEquals(fact.Host, c.Caster));
        BattleSpecialRules.RegisterAnemoResult((owner, effect, hook, state, result) => {
            if (owner?.EntityID != CharacterID || state?.StatusID2 != Roll || result == null
                || effect is not (RollDamage or KickDamage) || !string.IsNullOrEmpty(state.RuntimeElement)) return;
            foreach (string element in Priority)
                if (result.TriggeredReactions.Any(r => r.Type == ReactionType.Swirl
                    && r.SourceEntity == owner && r.InvolvedElements.Contains(element)))
                { state.RuntimeElement = element; break; }
        });
        BattleSpecialRules.RegisterStatusDamage("Sayu_Roll()", ExecuteRoll);
        BattleSpecialRules.RegisterStatusDamage("Sayu_Kick()", ExecuteKick);
        BattleSpecialRules.RegisterStatusDamage("Sayu_Absorbed()", execution => {
            var frame = Damage.Value;
            if (frame?.Roll != execution.Status || string.IsNullOrEmpty(frame.Absorbed)) return Task.CompletedTask;
            execution.Targets = frame.Targets.Where(t => Enemy(t, execution.Status.Caster)).ToList();
            return execution.ExecuteDefault(execution.Effect.WithDamageElement(frame.Absorbed));
        });
        // Count只保存命中层数；增伤放在普通DMGBonus乘区，连提前结束后的舞踢也读取原实例。
        BattleSpecialRules.RegisterBuffScalar("Sayu_C2Bonus()", (effect, state) => state.Caster?.EntityID == CharacterID
            && Damage.Value?.Roll?.Caster == state.Caster ? .066f * Damage.Value.Roll.RuntimeIntValue : 0f);
        BattleSpecialRules.RegisterStatusHeal("Sayu_SwirlHeal()", execution => execution.ExecuteDefault(
            Allies(BattleManager.Instance, execution.Status.Caster).ToList(),
            Number(300f + 1.2f * execution.Status.Caster.GetFinalEM()), null));
        BattleSpecialRules.RegisterStatusHeal("Sayu_DarumaHeal()", HealDaruma);
        BattleSpecialRules.RegisterStatusDamage("Sayu_DarumaDamage()", execution => {
            if (Tick.Value?.Status != execution.Status) throw new InvalidOperationException("不倒貉貉缺少本次周期上下文。");
            if (Tick.Value.Patient != null && execution.Status.Caster.ConstellationLevel < 1) return Task.CompletedTask;
            return execution.ExecuteDefault(execution.Effect);
        });
        BattleSpecialRules.RegisterStatusHitModifier("Sayu_DarumaDamage()");
        BattleSpecialRules.RegisterDamage("Sayu_DarumaDamage()", (_, owner, type) => {
            if (owner?.EntityID != CharacterID || type != "Damage") throw new NotSupportedException("不倒貉貉仅用于早柚状态伤害。");
        }, (_, owner, target, hit, level) => new HitData(hit.Multiplier
            + (owner.ConstellationLevel >= 6 ? Math.Min(4f, .002f * owner.GetFinalEM()) : 0f), hit.ElementAura));
        BattleSpecialRules.RegisterTargets("Sayu_DarumaTargets()", (battle, owner) => {
            var frame = Tick.Value;
            var result = new List<int>();
            if (frame?.Status?.Caster != owner || (frame.Patient != null && owner.ConstellationLevel < 1)) return result;
            var candidates = battle.Enemies.Select(e => e?.Entity).Where(e => Enemy(e, owner)).ToList();
            int count = Check(owner, "1043_T2") ? 3 : 1;
            while (candidates.Count > 0 && result.Count < count)
            {
                int index = candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count);
                result.Add(candidates[index].SlotPosition); candidates.RemoveAt(index);
            }
            return result;
        });
        BattleSpecialRules.RegisterTargets("Sayu_KickTargets()", (battle, owner) => {
            var candidates = battle.Enemies.Select(e => e?.Entity).Where(e => Enemy(e, owner)).OrderBy(e => e.SlotPosition).ToList();
            if (candidates.Count == 0) return new List<int>();
            var center = candidates[candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count)];
            return BattlePositionSystem.GetAdjacentPositions(center.Side, center.SlotPosition, 1);
        });
        BattleSpecialRules.RegisterTargets("Sayu_AbsorbedTargets()", (_, owner) => Damage.Value?.Roll?.Caster == owner
            ? Damage.Value.Targets.Where(t => Enemy(t, owner)).Select(t => t.SlotPosition).ToList() : new List<int>());
    }
    private static bool CanReturn(BattleEntity owner) => owner?.EntityID == CharacterID && owner.IsAlive && owner.IsOffField
        && Alive(owner.GetStatus(Roll)) && owner.GetStatusInstances(PublicControlStates.OffField)
            .All(s => s.BindingParent == owner.GetStatus(Roll));

    private static async Task ExecuteRoll(StatusDamageExecution execution)
    {
        Require(execution.Status, execution.Host);
        // 位置从右到左，每个人的风伤、反应、追加和响应结清后再处理下一个位置。
        var targets = execution.Targets.OrderByDescending(t => t.SlotPosition).ToArray();
        foreach (var target in targets)
        {
            if (!Alive(execution.Status) || BattleExecution.GameplayEffectsStopped) break;
            if (!Enemy(target, execution.Status.Caster)) continue;
            using var frame = new DamageFrame(execution.Status, new List<BattleEntity> { target });
            execution.Targets = frame.Targets;
            await execution.ExecuteDefault(execution.Effect);
            await BattleExecution.Flush();
            if (Alive(execution.Status)) await Absorbed(execution, "STE_Sayu_RollAbsorbed");
        }
    }
    private static async Task ExecuteKick(StatusDamageExecution execution)
    {
        Require(execution.Status, execution.Host);
        using var frame = new DamageFrame(execution.Status, execution.Targets.ToList());
        await execution.ExecuteDefault(execution.Effect);
        await BattleExecution.Flush();
        await Absorbed(execution, "STE_Sayu_KickAbsorbed");
    }
    private static Task Absorbed(StatusDamageExecution execution, string id)
    {
        if (execution.Status.Caster?.IsAlive != true || string.IsNullOrEmpty(Damage.Value?.Absorbed)
            || BattleExecution.GameplayEffectsStopped) return Task.CompletedTask;
        if (!DataManager.Instance.StatusEffectDict.TryGetValue(id, out var effect))
            throw new InvalidOperationException("早柚缺少转化追加效果：" + id);
        return execution.Status.Caster.CharacterCtrl.ExecuteStatusEffectAsync(effect, execution.Status, execution.Host);
    }
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static async Task HealDaruma(StatusHealExecution execution)
    {
        var frame = Tick.Value;
        if (frame?.Status != execution.Status) throw new InvalidOperationException("不倒貉貉治疗缺少本次周期上下文。");
        var owner = execution.Status.Caster;
        var patient = frame.Patient;
        if (patient?.IsOnField != true) return;
        if (!owner.CharacterCtrl.TryGetSkillLevelScalar("Sayu_DarumaHealATK", 3, out float ratio)
            || !owner.CharacterCtrl.TryGetSkillLevelScalar("Sayu_DarumaHealFlat", 3, out float flat))
            throw new InvalidOperationException("早柚缺少不倒貉貉治疗的等级参数。");
        float basis = ratio * owner.GetFinalATK(execution.Effect.StatusEffectID2, string.Empty, 3, execution.Effect.Element) + flat
            + (owner.ConstellationLevel >= 6 ? Math.Min(6000f, 3f * owner.GetFinalEM()) : 0f);
        await execution.ExecuteDefault(new List<BattleEntity> { patient }, Number(basis), null);
        if (!Alive(execution.Status) || !Check(owner, "1043_T2") || BattleExecution.GameplayEffectsStopped) return;
        var neighbors = Allies(BattleManager.Instance, owner).Where(a => a != patient
            && Math.Abs(a.SlotPosition - patient.SlotPosition) == 1).ToList();
        await execution.ExecuteDefault(neighbors, Number(basis * .2f), null);
    }
}

/// <summary>同一次风风轮累计0～10次滚动命中；clear只归零，生命周期和绑定仍由公共状态处理。</summary>
public sealed class SayuRollCounter : CountStatusHandler
{
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host) { SayuScriptHooks.Require(state, host); state.RuntimeIntValue = 0; }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); SayuScriptHooks.Require(state, host); request.ApplyInteger(state, host, 0, 10); }
}
