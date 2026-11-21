using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>2081–2085的命中资格、形态记录、反制及血线保护。技能和状态结果由表执行。</summary>
internal static class Enemy2081To2085Hooks
{
    internal const string GraderCore = "ST_RuinGrader_Core";
    private const string GraderStun = "ST_RuinGrader_Paralysis";
    private const string VishapEntry = "ST_Geovishap_Entry";
    private const string PrimoEntry = "ST_PrimoGeovishap_Entry";
    private const string PrimoCounter = "ST_PrimoGeovishap_Countered";
    private const string HeraldEntry = "ST_AbyssHerald_Entry";
    private const string LectorEntry = "ST_AbyssLector_Entry";
    private const string HeraldShield = "ST_AbyssHerald_RebirthShield";
    private const string LectorShield = "ST_AbyssLector_RebirthShield";
    private const string HeraldWard = "ST_AbyssHerald_ThawWard";
    private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo" };

    private sealed class ActionMemory
    {
        internal BattleEntity Owner;
        internal float ShieldFraction = -1f;
        internal long ShieldCheckHit;
        internal readonly HashSet<BattleEntity> ProtectedTargets = new HashSet<BattleEntity>();
        internal bool HitShield;
        internal bool ShowerAbsorbed;
        internal bool ShowerUnabsorbed;
        internal readonly Dictionary<BattleEntity, int> Penalties = new Dictionary<BattleEntity, int>();
        internal readonly HashSet<long> AllowedPenalties = new HashSet<long>();
    }

    private static readonly ConditionalWeakTable<BattleResolutionContext.CounterScope, ActionMemory> Actions
        = new ConditionalWeakTable<BattleResolutionContext.CounterScope, ActionMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, double[]> Received
        = new ConditionalWeakTable<StatusInstance, double[]>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> StunListeners
        = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);

    internal static void Register()
    {
        Condition("GraderCoreHit", c => c?.Caster?.IsAlive == true && c.Status?.RuntimeIntValue > 0
            && c.DamageEvent?.Target == c.Caster && c.DamageEvent.HitLanded
            && c.DamageEvent.Source?.CountsAsHit == true && c.DamageEvent.Source.SourceSide == BattleSide.Ally
            && (c.Declaration?.Header.ActionScopeId ?? 0) > 0);
        Condition("GraderCoreEmpty", c => c?.Caster?.GetStatus(GraderCore)?.RuntimeIntValue == 0
            && !c.Caster.HasStatus(GraderStun));
        BattleSpecialRules.RegisterStatusLifecycle(GraderStun, (s, host) => {
            DelayCurrentTimer(s);
            if (host is BattleEntity owner)
                StunListeners.Add(s, BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) => {
                    if (fact.Host == owner && fact.StatusId == PublicControlStates.EnemyStun
                        && s.BoundStatuses.Exists(child => child.ApplyOrder == fact.InstanceId))
                        owner.RemoveStatusInstance(s, "CoreStunEnded");
                }, s.ApplyOrder, () => owner.IsAlive && s.IsActive && !s.DeathCleanupPending));
        }, (s, _) => {
            if (StunListeners.TryGetValue(s, out var listener)) listener.Dispose();
            StunListeners.Remove(s);
        });

        BattleSpecialRules.RegisterStatusLifecycle(VishapEntry,
            (s, _) => Received.Add(s, new double[Elements.Length]), (s, _) => Received.Remove(s));
        BattleSpecialRules.RegisterStatusLifecycle(PrimoEntry, (s, _) => {
            s.RuntimeElement = Elements[BattleRandom.NextInt(0, Elements.Length)];
            s.RuntimeIntValue = Turn;
        }, null);
        BattleSpecialRules.RegisterCondition("VishapSelectForm", (element, c) => c?.Status != null && c.Status.RuntimeElement == element
            && c.Caster?.IsAlive == true && !c.Caster.HasStatus(
                (c.Status.StatusID2 == PrimoEntry ? "ST_PrimoGeovishap_Form" : "ST_Geovishap_Form") + element));
        Condition("VishapShieldContact", c => GetAction(c?.Caster)?.HitShield == true);
        Condition("PrimoCanShower", c => c?.Caster?.IsAlive == true && c.Caster.CurrentHP > .2f * c.Caster.GetFinalHP());
        Condition("PrimoShowerCountered", c => GetAction(c?.Caster) is ActionMemory a
            && a.ShowerAbsorbed && !a.ShowerUnabsorbed);
        Condition("PrimoNormalResistance", c => c?.Caster?.HasStatus(PrimoCounter) != true);
        Condition("PrimoRestoreShell", c => c?.Caster?.GetStatus(PrimoEntry) is StatusInstance entry
            && Turn >= entry.RuntimeIntValue && (Turn - entry.RuntimeIntValue + 1) % 2 == 0);
        BattleSpecialRules.RegisterStatusLifecycle(PrimoCounter, (s, _) => DelayCurrentTimer(s), null);
        BattleSpecialRules.RegisterEnemyTargetDamageScale("PrimoShieldedShower()", (effect, target) =>
            target.HasShieldProtection()
                ? float.Parse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture) : 1f);
        BattleSpecialRules.RegisterFixedStatusAmount("PrimoCounterLoss()", (effect, s, host) =>
            float.Parse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture)
                * (host as BattleEntity ?? s.Caster).GetFinalHP());

        Condition("AbyssRebirthReady", c => c?.Caster?.IsAlive == true && c.Status?.RuntimeIntValue == 1);
        BattleSpecialRules.RegisterStatusLifecycle(HeraldShield, (_, host) => MarkReborn(host as BattleEntity), null);
        BattleSpecialRules.RegisterStatusLifecycle(LectorShield, (_, host) => MarkReborn(host as BattleEntity), null);
        BattleSpecialRules.RegisterHPDamageGuard(GuardRebirthHP);
        Condition("AbyssRebirthBroken", c => c?.Declaration?.Get<StatusEventData>()?.Reason == "ShieldBroken");
        BattleSpecialRules.RegisterFixedStatusAmount("AbyssRebirthDefeat()", (_, s, host) =>
            (host as BattleEntity ?? s.Caster).CurrentHP);
        Condition("AbyssInRebirth", c => RebirthShield(c?.Caster) != null);
        BattleSpecialRules.RegisterCount("HeraldComboExtra", owner => {
            float ratio = ShieldFraction(owner);
            return ratio < 0f ? 0 : ratio <= 1f / 3f ? 3 : ratio <= 2f / 3f ? 2 : 1;
        });
        BattleSpecialRules.RegisterCount("HeraldBladesHits", owner => {
            float ratio = ShieldFraction(owner);
            return ratio < 0f ? 1 : ratio <= .5f ? 3 : 2;
        });
        BattleSpecialRules.RegisterCount("LectorThunderHits", owner => {
            float ratio = ShieldFraction(owner);
            return ratio < 0f ? 2 : ratio < .5f ? 4 : 3;
        });
        BattleSpecialRules.RegisterCount("LectorWaveHits", owner => ShieldFraction(owner) < 0f ? 1 : 2);

        BattleSpecialRules.RegisterCondition("AbyssPenaltyHit", (skill, c) => {
            var hit = c?.DamageEvent;
            if (hit?.Target?.IsAlive != true || hit.ActualHPDamage <= 0f || hit.Source?.SourceEntity != c.Caster
                || hit.Source.SourceKind != ReactionSourceKind.EnemySkill || hit.Source.SourceSkillID != skill) return false;
            return !IsVolley(skill) || GetAction(c.Caster)?.AllowedPenalties.Contains(c.Declaration?.Header.EventId ?? 0) == true;
        });
        Condition("AbyssHeraldThawed", c => c?.Caster?.HasStatus(HeraldShield) == true);
        BattleSpecialRules.RegisterStatusLifecycle(HeraldWard, (s, _) => {
            int phase = (int)(BattleManager.Instance?.CurrentPhase ?? 0);
            if (phase <= 0) return;
            s.AddInPhase = phase;
            DelayCurrentTimer(s);
        }, null);
        FrozenReactionHandler.RegisterPolicy(
            immune: owner => owner.HasStatus(HeraldWard),
            adjustAmount: (owner, amount) => owner.HasStatus(HeraldShield) && amount > 1f ? amount - 1f : amount,
            removed: OnThawed);
        BattleSpecialRules.RegisterDeclarationObserver(Observe);
        BattleSpecialRules.RegisterHitScope(RecordPreHitShields);
    }

    private static void Condition(string name, Func<ScriptHookContext, bool> test)
        => BattleSpecialRules.RegisterCondition(name, (_, c) => test(c));

    private static void DelayCurrentTimer(StatusInstance status)
    {
        if ((int)(BattleManager.Instance?.CurrentPhase ?? 0) == status.AddInPhase)
            status.EarliestTimerTurn = Turn + 1;
    }

    private static ActionMemory GetAction(BattleEntity owner)
    {
        var key = BattleResolutionContext.CurrentAction;
        return owner != null && key != null && Actions.TryGetValue(key, out var memory) && memory.Owner == owner ? memory : null;
    }

    private static IDisposable RecordPreHitShields(long id, BattleEntity owner, IEnumerable<BattleEntity> targets)
    {
        var skill = DamageQueryContext.CurrentSkillID;
        if (owner?.EntityID != 2082 || skill == null
            || !skill.StartsWith("SK_Geovishap_Roll", StringComparison.Ordinal)
                && !skill.StartsWith("SK_Geovishap_Jump", StringComparison.Ordinal)) return null;
        var memory = GetAction(owner);
        if (memory == null) return null;
        memory.ShieldCheckHit = id;
        memory.ProtectedTargets.Clear();
        foreach (var target in targets)
            if (target.Side == BattleSide.Ally && target.HasShieldProtection()) memory.ProtectedTargets.Add(target);
        return null;
    }

    private static void Observe(BattleDeclaration declaration)
    {
        if (declaration.Data is ActionEventData action && declaration.Kind == BattleEventKind.ActionStarted
            && action.Subject?.Side == BattleSide.Enemy && action.Subject.EntityID >= 2081 && action.Subject.EntityID <= 2085)
        {
            var key = BattleResolutionContext.CurrentAction;
            if (key != null && !Actions.TryGetValue(key, out _))
                Actions.Add(key, new ActionMemory { Owner = action.Subject, ShieldFraction = ReadShieldFraction(action.Subject) });
            return;
        }
        if (!(declaration.Data is DamageEventData hit)) return;
        RecordVishapDamage(hit);
        var source = hit.Source;
        var memory = GetAction(source.SourceEntity);
        if (memory == null || source.SourceKind != ReactionSourceKind.EnemySkill || hit.Subject?.Side != BattleSide.Ally) return;
        if (hit.HitLanded && memory.ShieldCheckHit == declaration.Header.HitId && memory.ProtectedTargets.Contains(hit.Subject))
            memory.HitShield = true;
        if (hit.HitLanded && source.SourceSkillID?.StartsWith("SK_PrimoGeovishap_ShowerRelease", StringComparison.Ordinal) == true)
        {
            memory.ShowerAbsorbed |= hit.ShieldAbsorbed > 0f;
            memory.ShowerUnabsorbed |= hit.RequestedDamage > 0f;
        }
        if (hit.ActualHPDamage <= 0f || !IsVolley(source.SourceSkillID)) return;
        memory.Penalties.TryGetValue(hit.Subject, out int count);
        memory.Penalties[hit.Subject] = count + 1;
        if (count < 4) memory.AllowedPenalties.Add(declaration.Header.EventId);
    }

    private static void RecordVishapDamage(DamageEventData hit)
    {
        if (hit.ActualHPDamage <= 0f || hit.Subject?.IsAlive != true) return;
        var entry = hit.Subject.GetStatus(VishapEntry);
        if (entry == null || !string.IsNullOrEmpty(entry.RuntimeElement) || !Received.TryGetValue(entry, out var sums)) return;
        int element = Array.IndexOf(Elements, hit.Element);
        if (element >= 0) sums[element] += hit.ActualHPDamage;
        if (hit.HPAfter < .6f * hit.Subject.GetFinalHP()) entry.RuntimeElement = SelectElement(sums);
    }

    internal static string SelectElement(double[] sums)
    {
        var tied = new List<int> { 0 };
        for (int i = 1; i < Elements.Length; i++)
        {
            double best = sums[tied[0]];
            if (sums[i] > best) tied.Clear();
            if (sums[i] >= best) tied.Add(i);
        }
        return Elements[tied.Count == 1 ? tied[0] : tied[BattleRandom.NextInt(0, tied.Count)]];
    }

    private static bool IsVolley(string skill) => skill == "SK_AbyssHerald_Torrents" || skill == "SK_AbyssLector_Violet";
    private static StatusInstance Entry(BattleEntity owner) => owner?.GetStatus(HeraldEntry) ?? owner?.GetStatus(LectorEntry);
    private static Shield RebirthShield(BattleEntity owner)
    {
        var shield = owner?.GetEnemyElementalShield();
        string id = shield?.SourceStatusID2;
        return id == HeraldShield || id == LectorShield ? shield : null;
    }
    private static float ReadShieldFraction(BattleEntity owner) => RebirthShield(owner) is Shield shield
        ? shield.Value / shield.InitialValue : -1f;
    private static float ShieldFraction(BattleEntity owner) => GetAction(owner)?.ShieldFraction ?? ReadShieldFraction(owner);
    private static void MarkReborn(BattleEntity owner)
    {
        if (Entry(owner) is StatusInstance entry) entry.RuntimeIntValue = 2;
    }
    private static float GuardRebirthHP(BattleEntity owner, float amount)
    {
        var entry = Entry(owner);
        if (entry == null || entry.RuntimeIntValue == 2) return amount;
        float allowed = Math.Max(0f, owner.CurrentHP - .2f * owner.GetFinalHP());
        // 在扣血前锁定跨线事实，避免浮点相减后恰好高于20%而漏开盾。
        if (amount >= allowed) entry.RuntimeIntValue = 1;
        return Math.Min(amount, allowed);
    }

    internal static DamageSourceInfo RebirthDefeatSource(EnemySkillEffectData effect, StatusInstance status)
    {
        if (effect.DamageHook != "AbyssRebirthDefeat()" || status?.ShieldBreakSource == null) return null;
        var source = status.ShieldBreakSource.Snapshot();
        source.CountsAsHit = false; // 继承实际破盾者的击杀归属，不产生额外攻击命中。
        // 原装备已经完成破盾；之后的装备生命周期不应取消敌人自己的死亡效果。
        if (!EquipmentStatusLifetime.CanExecute(source))
        {
            source.EquipmentSource = null;
            source.SourceStatus = null;
        }
        return source;
    }

    private static void OnThawed(BattleEntity owner)
    {
        var status = owner?.GetStatus(HeraldShield);
        var dm = DataManager.Instance;
        if (status == null || owner.IsAlive != true || owner.EnemyCtrl == null
            || dm == null || !dm.StatusActionDict.TryGetValue(HeraldShield, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == "AbyssHeraldThawed()")
                owner.EnemyCtrl.TryExecuteEventStatusAction(status, action, owner,
                    new ScriptHookContext { Caster = owner, Status = status, StatusHost = owner, Target = owner, Declaration = BattleEventHub.Current });
    }
}
