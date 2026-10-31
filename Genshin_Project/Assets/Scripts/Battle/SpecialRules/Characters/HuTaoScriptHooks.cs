using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>胡桃：实例固定加攻/血梅香加算、逐hit产能、爆发合并治疗、扣血前六命保护。参数和后续效果仍在表内。</summary>
public static class HuTaoScriptHooks
{
    public const string Dance = "ST_HuTao_Dance", Blossom = "ST_HuTao_Blossom", C6 = "ST_HuTao_C6";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<StatusInstance> BloomDamage = new AsyncLocal<StatusInstance>();
    private static readonly ConditionalWeakTable<BattleDeclaration, StatusInstance> C6Facts = new ConditionalWeakTable<BattleDeclaration, StatusInstance>();
    private static readonly ConditionalWeakTable<BattleDeclaration, StatusInstance> EnergyFacts = new ConditionalWeakTable<BattleDeclaration, StatusInstance>();
    private static readonly AsyncLocal<HitFrame> Hitting = new AsyncLocal<HitFrame>();
    private sealed class HitFrame : IDisposable
    {
        internal readonly long ID; internal readonly BattleEntity Owner; internal readonly StatusInstance Dance;
        internal bool EnergyRecorded;
        private readonly HitFrame previous = Hitting.Value;
        internal HitFrame(long id, BattleEntity owner)
        { ID = id; Owner = owner; Dance = owner?.EntityID == 1037 ? owner.GetStatus(HuTaoScriptHooks.Dance) : null; Hitting.Value = this; }
        public void Dispose() => Hitting.Value = previous;
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner; internal readonly string Skill; internal readonly bool Low;
        internal readonly HashSet<BattleEntity> Hits = new HashSet<BattleEntity>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner = owner; Skill = skill?.SkillID2; Low = owner?.IsAlive == true && owner.CurrentHP < owner.GetFinalHP() * .5f; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class GuardMemory : IDisposable
    {
        internal readonly StatusActionRuntimeState Cooldown = new StatusActionRuntimeState();
        internal bool Pending; internal IDisposable Listener;
        public void Dispose() => Listener?.Dispose();
    }
    private static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    private static Cast Burst(BattleEntity owner) => Casting.Value?.Owner == owner && Casting.Value.Skill == "SK_Burst_HuTao" ? Casting.Value : null;
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterHitScope((id, owner, _) => new HitFrame(id, owner));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Dance, CaptureDance, null);
        BattleSpecialRules.RegisterStatusLifecycle(Blossom, (state, host) =>
        {
            RequireSource(state);
            state.Snapshot = new Dictionary<string, float> { ["AppliedMaxHP"] = state.Caster.GetFinalHP() };
        }, null);
        BattleSpecialRules.RegisterBuffScalar("HuTao_FixedAttack()", (_, state) => Saved(state, "Attack"));
        BattleSpecialRules.RegisterBuffScalar("HuTao_FixedBlossomBonus()", (effect, state) =>
        {
            var bloom = BloomDamage.Value;
            return bloom?.Caster == state.Caster ? Coefficient(effect.Param1) * Saved(bloom, "AppliedMaxHP") : 0f;
        });
        BattleSpecialRules.RegisterCondition("HuTao_BlossomDamage", (a, c) => string.IsNullOrEmpty(a)
            && BloomDamage.Value?.Caster == c?.Caster && c?.HitEffectID == "STE_HuTao_BlossomDamage"
            && BloomDamage.Value.Host == c.Target);
        BattleSpecialRules.RegisterDamage("HuTao_BlossomMemory()", ValidateDamage, (_, owner, target, hit, level) => hit);
        BattleSpecialRules.RegisterStatusDamage("HuTao_BlossomMemory()", async execution =>
        {
            var previous = BloomDamage.Value; BloomDamage.Value = execution.Status;
            try { await execution.ExecuteDefault(execution.Effect); }
            finally { BloomDamage.Value = previous; }
        });
        BattleSpecialRules.RegisterFixedStatusAmount("HuTao_SkillCost()", (effect, state, host) =>
        {
            RequireSource(state);
            if (state.StatusID2 != Dance || host != state.Caster || effect.TargetType != "Self" || !DamageProperties.IsTrue(effect.Element))
                throw new InvalidOperationException("胡桃扣血只用于蝶舞施加时对自身的True伤害。");
            float hp = state.Caster.CurrentHP;
            return Math.Max(0f, Math.Min(hp * Coefficient(effect.Param1), hp - 1f));
        });
        BattleSpecialRules.RegisterCondition("HuTao_LowHP", (a, c) => string.IsNullOrEmpty(a) && c?.Caster?.EntityID == 1037
            && c.Caster.IsAlive && c.Caster.CurrentHP <= c.Caster.GetFinalHP() * .5f);
        BattleSpecialRules.RegisterCondition("HuTao_EnergyHit", (a, c) => string.IsNullOrEmpty(a)
            && (c?.DamageEvent?.Declaration ?? c?.Declaration) is BattleDeclaration d
            && EnergyFacts.TryGetValue(d, out var dance) && dance == c.Status && Active(dance));
        BattleSpecialRules.RegisterCondition("HuTao_HeavyHit", (a, c) => string.IsNullOrEmpty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Dance && IsAttack(c.DamageEvent?.Source, c.DamageEvent?.Target, c.Caster)
            && c.DamageEvent.HitLanded && c.DamageEvent.DamageType == "Heavy");
        BattleSpecialRules.RegisterCondition("HuTao_Alive", (a, c) => string.IsNullOrEmpty(a) && c?.Caster?.IsAlive == true);
        BattleSpecialRules.RegisterTargets("HuTao_HitEnemy()", (battle, owner) => Positions(battle, owner, new[] { ScriptHookContext.Current?.DamageEvent?.Target }));
        BattleSpecialRules.RegisterTargets("HuTao_BurstEnemies()", (battle, owner) => Positions(battle, owner, Burst(owner)?.Hits));
        BattleSpecialRules.RegisterTargets("HuTao_OtherAllies()", (battle, owner) => battle.Allies
            .Where(a => a?.Entity?.IsAlive == true && a.Entity != owner && a.Entity.Side == owner.Side)
            .Select(a => a.Entity.SlotPosition).ToList());
        BattleSpecialRules.RegisterDamage("HuTao_BurstRatio()", ValidateDamage, (_, owner, target, hit, level) =>
        {
            var cast = Burst(owner);
            if (cast == null) throw new InvalidOperationException("胡桃爆发缺少本次施放记录。");
            return cast.Low ? new HitData(Scalar(owner, "HuTao_BurstLow", 3), hit.ElementAura) : hit;
        });
        BattleSpecialRules.RegisterStatusHeal("HuTao_BurstHeal()", HealBurst);
        BattleSpecialRules.RegisterStatusLifecycle(C6, StartGuard, (state, _) => (state.ScriptRuntimeState as GuardMemory)?.Dispose());
        BattleSpecialRules.RegisterHPDamageGuard(Guard);
        BattleSpecialRules.RegisterCondition("HuTao_C6Triggered", (a, c) => string.IsNullOrEmpty(a)
            && c?.Declaration != null && C6Facts.TryGetValue(c.Declaration, out var state) && state == c.Status);
    }
    private static void RequireSource(StatusInstance state)
    { if (state?.Caster?.EntityID != 1037) throw new InvalidOperationException("胡桃状态缺少本人来源。"); }
    private static float Coefficient(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < 0f)
            throw new InvalidOperationException("胡桃状态系数必须为有限非负数。");
        return n;
    }
    private static float Scalar(BattleEntity owner, string id, int type)
    {
        if (owner?.CharacterCtrl?.TryGetSkillLevelScalar(id, type, out float value) != true)
            throw new InvalidOperationException("胡桃缺少技能等级标量：" + id);
        return value;
    }
    internal static float Saved(StatusInstance state, string key)
    {
        if (state?.Snapshot == null || !state.Snapshot.TryGetValue(key, out float value))
            throw new InvalidOperationException("胡桃状态缺少施加时记录：" + key);
        return value;
    }
    private static void CaptureDance(StatusInstance state, object host)
    {
        RequireSource(state);
        if (host != state.Caster) throw new InvalidOperationException("彼岸蝶舞必须挂胡桃本人。");
        state.Snapshot = new Dictionary<string, float>
        { ["Attack"] = Math.Min(state.Caster.GetFinalHP() * Scalar(state.Caster, "STE_HuTao_FixedAttack", 2), state.Caster.GetTotalBaseATK() * 4f) };
    }
    private static void ValidateDamage(string hook, BattleEntity owner, string type)
    { if (owner?.EntityID != 1037 || type != "Damage") throw new InvalidOperationException("胡桃伤害钩子仅用于本人Damage效果。"); }
    private static bool IsAttack(DamageSourceInfo source, BattleEntity target, BattleEntity owner)
        => owner?.EntityID == 1037 && source?.SourceEntity == owner && source.CountsAsHit && target != null && target.Side != owner.Side;
    private static List<int> Positions(BattleManager battle, BattleEntity owner, IEnumerable<BattleEntity> targets)
        => targets == null ? new List<int>() : targets.Where(t => t?.IsAlive == true && t.Side != owner.Side
            && battle.GetEntityByPosition(t.Side, t.SlotPosition) == t).Select(t => t.SlotPosition).Distinct().OrderBy(p => p).ToList();
    private static void Index(BattleDeclaration declaration)
    {
        var hit = declaration.Get<DamageEventData>(); if (hit == null) return;
        var frame = Hitting.Value;
        if (hit.HitLanded && IsAttack(hit.Source, hit.Subject, frame?.Owner))
        {
            if (frame.ID == declaration.Header.HitId && Active(frame.Dance) && !frame.EnergyRecorded
                && (hit.DamageType == "Normal" || hit.DamageType == "Heavy"))
            { frame.EnergyRecorded = true; EnergyFacts.Add(declaration, frame.Dance); }
            var cast = Burst(hit.Source.SourceEntity);
            if (cast != null && hit.Source.SourceEffectID == "SE_Burst_HuTaoDamage") cast.Hits.Add(hit.Subject);
        }
        var state = hit.Subject?.EntityID == 1037 ? hit.Subject.GetStatus(C6) : null;
        if (Active(state) && state.ScriptRuntimeState is GuardMemory memory && memory.Pending)
        { memory.Pending = false; C6Facts.Add(declaration, state); }
    }
    private static async Task HealBurst(StatusHealExecution execution)
    {
        var owner = execution.Status.Caster; var cast = Burst(owner);
        if (cast == null || cast.Hits.Count == 0 || !owner.IsAlive) return;
        float amount = cast.Hits.Count * Scalar(owner, cast.Low ? "HuTao_HealLow" : "STE_HuTao_BurstHeal", 3) * owner.GetFinalHP();
        await execution.ExecuteDefault(new List<BattleEntity> { owner }, amount.ToString("R", CultureInfo.InvariantCulture), null);
    }
    private static void StartGuard(StatusInstance state, object host)
    {
        RequireSource(state);
        if (host != state.Caster) throw new InvalidOperationException("六命监听必须挂胡桃本人。");
        var memory = new GuardMemory(); state.ScriptRuntimeState = memory;
        memory.Listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
        {
            var cause = BattleEventHub.Current;
            if (cause == null || !C6Facts.TryGetValue(cause, out var triggered) || triggered != state) return;
            if (!DataManager.Instance.StatusActionDict.TryGetValue(C6, out var rows)) throw new InvalidOperationException("胡桃六命缺少OnTrigger。");
            foreach (var row in rows.Where(r => r.ActionType == "OnTrigger" && r.ScriptHook == "HuTao_C6Triggered()"))
                state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, row, host,
                    new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = host, Declaration = cause }, () => Active(state));
        }, state.ApplyOrder, () => Active(state));
    }
    internal static float Guard(BattleEntity target, float amount)
    {
        if (target?.EntityID != 1037 || !target.IsAlive || target.ConstellationLevel < 6 || amount <= 0f) return amount;
        var state = target.GetStatus(C6);
        if (!Active(state) || !(state.ScriptRuntimeState is GuardMemory memory)) return amount;
        int turn = BattleManager.Instance?.TurnCount ?? 0;
        var action = DataManager.Instance.StatusActionDict[C6].Single(r => r.ActionType == "OnTrigger" && r.ScriptHook == "HuTao_C6Triggered()");
        if (action.Cooldown <= 0) throw new InvalidOperationException("胡桃六命必须配置正回合冷却。");
        int phase = (int)(BattleManager.Instance?.CurrentPhase ?? 0);
        if (memory.Pending || memory.Cooldown.IsCoolingDown(action.Cooldown, turn, phase)
            || target.CurrentHP - amount >= target.GetFinalHP() * .25f) return amount;
        // 立即占用冷却，避免声明响应前的后续扣血再次领取免死；后续增益按原响应顺序执行。
        memory.Cooldown.LastTriggerTurn = turn; memory.Cooldown.LastTriggerPhase = phase; memory.Pending = true;
        return Math.Min(amount, Math.Max(0f, target.CurrentHP - 1f));
    }
}
