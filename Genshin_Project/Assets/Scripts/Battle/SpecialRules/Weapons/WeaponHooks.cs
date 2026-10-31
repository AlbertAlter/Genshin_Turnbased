using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>武器专用处理器唯一登记处。只使用实例来源和正式声明，不另建响应队列。</summary>
public static class WeaponHooks
{
    internal static void RegisterCounts()
    {
        CountStatusSystem.Register(WeaponPendingAPCount.Slingshot, new WeaponPendingAPCount(3012));
        CountStatusSystem.Register(WeaponPendingAPCount.Amos, new WeaponPendingAPCount(3181));
        CountStatusSystem.Register(SturdyBoneCountHandler.StatusID, new SturdyBoneCountHandler());
        // 以下各自明确采用“获点刷新整窗”，不是其他Count的默认规则。
        CountStatusSystem.Register("ST_Weapon_4305602", new WeaponIncrementCount(3056, 5, 0, 2));
        CountStatusSystem.Register("ST_Weapon_4308002", new WeaponIncrementCount(3080, 5, 0, 2));
        CountStatusSystem.Register("ST_Weapon_4311202", new WeaponIncrementCount(3112, 5, 0, 2));
        CountStatusSystem.Register("ST_Weapon_4313202", new WeaponIncrementCount(3132, 5, 0, 0));
        CountStatusSystem.Register("ST_Weapon_4305802", new WeaponIncrementCount(3058, 4, 2, 2));
        CountStatusSystem.Register("ST_Weapon_4308402", new WeaponIncrementCount(3084, 4, 2, 2));
        CountStatusSystem.Register("ST_Weapon_4309602", new WeaponIncrementCount(3096, 2, 2, 2, true));
        CountStatusSystem.Register("ST_Weapon_4314702", new WeaponIncrementCount(3147, 3, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4319302", new WeaponIncrementCount(3193, 7, 2, 0));
        CountStatusSystem.Register(SerpentSpineWeaponCount.ID, new SerpentSpineWeaponCount());
        CountStatusSystem.Register(FadingTwilightWeaponCount.ID, new FadingTwilightWeaponCount());
        CountStatusSystem.Register("ST_Weapon_4309102", new WeaponIncrementCount(3091, 2, 2, 0, true));
        CountStatusSystem.Register("ST_Weapon_4309902", new WeaponIncrementCount(3099, 3, 8, 0));
        CountStatusSystem.Register("ST_Weapon_4310002", new WeaponIncrementCount(3100, 2, 5, 0, true));
        CountStatusSystem.Register("ST_Weapon_4310902", new WeaponIncrementCount(3109, 2, 3, 0, true));
        CountStatusSystem.Register("ST_Weapon_4312202", new WeaponIncrementCount(3122, 3, 8, 0));
        CountStatusSystem.Register("ST_Weapon_4312802", new WeaponIncrementCount(3128, 3, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4313402", new WeaponIncrementCount(3134, 2, 3, 0, true));
        CountStatusSystem.Register("ST_Weapon_4315603", new WeaponIncrementCount(3156, 5, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4315803", new WeaponIncrementCount(3158, 2, 0, 0));
        CountStatusSystem.Register("ST_Weapon_4316103", new WeaponIncrementCount(3161, 3, 5, 0));
        CountStatusSystem.Register("ST_Weapon_4316303", new WeaponIncrementCount(3163, 3, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4316304", new WeaponIncrementCount(3163, 2, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4316503", new WeaponIncrementCount(3165, 3, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4316602", new WeaponIncrementCount(3166, 2, 8, 0));
        CountStatusSystem.Register("ST_Weapon_4317203", new WeaponIncrementCount(3172, 5, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4317303", new WeaponIncrementCount(3173, 4, 0, 0));
        CountStatusSystem.Register("ST_Weapon_4317903", new WeaponIncrementCount(3179, 4, 1, 0));
        CountStatusSystem.Register("ST_Weapon_4318003", new WeaponIncrementCount(3180, 3, 1, 0));
        CountStatusSystem.Register("ST_Weapon_4318303", new WeaponIncrementCount(3183, 4, 0, 0));
        CountStatusSystem.Register("ST_Weapon_4319503", new WeaponIncrementCount(3195, 5, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4319903", new WeaponIncrementCount(3199, 3, 3, 0));
        CountStatusSystem.Register("ST_Weapon_4320103", new WeaponAwardCount(3201, 2, 2));
        CountStatusSystem.Register("ST_Weapon_4320803", new WeaponIncrementCount(3208, 5, 2, 0));
        CountStatusSystem.Register("ST_Weapon_4321002", new WeaponIncrementCount(3210, 3, 8, 0));
        CountStatusSystem.Register("ST_Weapon_4321403", new WeaponAwardCount(3214, 3, 1));
        CountStatusSystem.Register("ST_Weapon_4309904", new WeaponMarkRewardCount(3099, "ST_Weapon_4309902"));
        CountStatusSystem.Register("ST_Weapon_4312204", new WeaponMarkRewardCount(3122, "ST_Weapon_4312202"));
        CountStatusSystem.Register(WidsithWeaponCount.ID, new WidsithWeaponCount());
        CountStatusSystem.Register(FruitOfFulfillmentWeaponCount.ID, new FruitOfFulfillmentWeaponCount());
        CountStatusSystem.Register(FlowingPurityAccumulator.ID, new FlowingPurityAccumulator());
        CountStatusSystem.Register(FlowingPurityReward.ID, new FlowingPurityReward());
        CountStatusSystem.Register("ST_Weapon_4315804", new WeaponTeamRewardCount(3158));
        CountStatusSystem.Register("ST_Weapon_4317304", new WeaponTeamRewardCount(3173));
        CountStatusSystem.Register("ST_Weapon_4318304", new WeaponTeamRewardCount(3183));
        CountStatusSystem.Register("ST_Weapon_4316203", new WeaponEMHitCount(3162, 28, "Normal", "Skill"));
        CountStatusSystem.Register("ST_Weapon_4318703", new WeaponEMHitCount(3187, 12, "Heavy"));
        CountStatusSystem.Register(AlleyHunterWeaponCount.ID, new AlleyHunterWeaponCount());
        CountStatusSystem.Register(CalamityQuellerWeaponCount.ID, new CalamityQuellerWeaponCount());
        CountStatusSystem.Register(TulaytullahWeaponCount.ID, new TulaytullahWeaponCount());
        CountStatusSystem.Register(LostPrayerWeaponCount.ID, new LostPrayerWeaponCount());
        CountStatusSystem.Register(HaranSigilWeaponCount.ID, new HaranSigilWeaponCount());
        CountStatusSystem.Register(HaranBonusWeaponCount.ID, new HaranBonusWeaponCount());
        CountStatusSystem.Register(SurfingTimeWeaponCount.ID, new SurfingTimeWeaponCount());
        CountStatusSystem.Register(EndOfLineWeaponCount.ID, new EndOfLineWeaponCount());
        CountStatusSystem.Register(KingsSquireWeaponCount.ID, new KingsSquireWeaponCount());
        CountStatusSystem.Register(PlumageWeaponCount.ID, new PlumageWeaponCount());
        CountStatusSystem.Register(SacrificialJadeWeaponCount.ID, new SacrificialJadeWeaponCount());
        CountStatusSystem.Register("ST_Weapon_4315902", new WeaponQualificationCount(3159));
        CountStatusSystem.Register("ST_Weapon_4318402", new WeaponQualificationCount(3184));
        CountStatusSystem.Register("ST_Weapon_4318502", new WeaponQualificationCount(3185));
        CountStatusSystem.Register("ST_Weapon_4318901", new WeaponQualificationCount(3189));
        CountStatusSystem.Register(BlazingSunsWeaponCount.ID, new BlazingSunsWeaponCount());
        CountStatusSystem.Register(TalesWeaponCount.ID, new TalesWeaponCount());
        CountStatusSystem.Register(CashflowWeaponCount.ID, new CashflowWeaponCount());
        CountStatusSystem.Register(AbsolutionWeaponCount.ID, new AbsolutionWeaponCount());
        CountStatusSystem.Register(NightweaverWeaponCount.ID, new NightweaverWeaponCount());
        CountStatusSystem.Register(WeaponDamageCompletionHooks.Pride, new WholeDamageWeaponCount(3171));
        CountStatusSystem.Register(WeaponDamageCompletionHooks.Verdict, new WholeDamageWeaponCount(3176));
        CountStatusSystem.Register(WeaponDamageCompletionHooks.Harp, new WholeDamageWeaponCount(3182));
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterHitScope(BeginHit);
        BattleSpecialRules.RegisterNumberedCondition("4300201", (_, c) =>
            Aura(c, "Hydro", "Cryo") || Frozen(c));
        BattleSpecialRules.RegisterNumberedCondition("4300701", (_, c) =>
        {
            var owner = Owner(c);
            return owner != null && owner.GetFinalHP() > 0f
                && owner.CurrentHP / owner.GetFinalHP() < Param(c?.Status, 1);
        });
        BattleSpecialRules.RegisterNumberedCondition("4300801", (_, c) => Aura(c, "Pyro", "Electro"));
        BattleSpecialRules.RegisterNumberedCondition("4301301", (_, c) => Aura(c, "Hydro", "Pyro"));
        WeaponPendingAPCount.Register();
        WeaponRosterHooks.Register();
        NarzissenkreuzWeaponHooks.Register();
        WeaponStackTransferHooks.Register();
        WeaponSwitchHooks.Register();
        WeaponSnapshotHooks.Register();
        WeaponPostDamageGate.Register();
        SnowTombedWeaponHooks.Register();
        EyeOfPerceptionWeaponHooks.Register();
        WeaponAuraReactionAndFieldHooks.Register();
        WeaponEnergyAndOffFieldHooks.Register();
        WeaponReactionAndRetaliationTargets.Register();
        WeaponShieldAndLifeHooks.Register();
        WeaponResourceAndAttributeHooks.Register();
        WeaponCountAwardHooks.Register();
        WeaponShieldCreationHooks.Register();
        WeaponChargedCompletionHooks.Register();
        BattleSpecialRules.RegisterNumberedCondition("4303302", (_, c) => c?.DamageEvent != null
            && (c.DamageEvent.DamageType == "Normal" || c.DamageEvent.DamageType == "Heavy") && OwnCritical(c));
        BattleSpecialRules.RegisterNumberedCondition("4304201", (_, c) => Owner(c) != null
            && OwnStatus(c?.Status, "ST_Weapon_4304202") == null);
        BattleSpecialRules.RegisterNumberedCondition("4304203", (_, c) => c?.Status?.IsActive == true
            && c.Status.StatusID2 == "ST_Weapon_4304203" && c.DamageEvent?.CausedDeath == true
            && ReferenceEquals(c.StatusHost, c.DamageEvent.Target));
        BattleSpecialRules.RegisterNumberedCondition("4304204", (_, c) => HasOwnTargetMark(c, "ST_Weapon_4304203"));
        BattleSpecialRules.RegisterNumberedCondition("4303601", (_, c) => Owner(c) != null
            && OwnStatus(c?.Status, "ST_Weapon_4303603") == null);
        BattleSpecialRules.RegisterNumberedCondition("4305001", (_, c) => Owner(c)?.HasShieldProtection() == true);
        BattleSpecialRules.RegisterNumberedCondition("4305702", (_, c) => Owner(c)?.HasShieldProtection() == true);
        BattleSpecialRules.RegisterNumberedCondition("4305501", (_, c) => Aura(c, "Hydro", "Electro"));
        BattleSpecialRules.RegisterNumberedCondition("4310601", (_, c) => Aura(c, "Hydro", "Pyro"));
        BattleSpecialRules.RegisterNumberedCondition("4309003", (_, c) => Owner(c) != null
            && Owner(c).MaxEnergy > 0f && Owner(c).CurrentEnergy >= Owner(c).MaxEnergy);
        BattleSpecialRules.RegisterNumberedCondition("4309702", (_, c) => HasOwnTargetMark(c, "ST_Weapon_4309703"));
        BattleSpecialRules.RegisterNumberedCondition("4311001", (_, c) => LivingEnemyCount(c) >= 2);
        BattleSpecialRules.RegisterNumberedCondition("4311003", (_, c) =>
        {
            int count = LivingEnemyCount(c);
            return count >= 0 && count < 2; // 未构筑队伍不是“0名敌人”。
        });
        foreach (string id in new[] { "4305103", "4307703" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) =>
            {
                var team = BattleManager.Instance?.TeamBonuses;
                return team?.Contains(Owner(c)) == true && team.Moonsign.Level >= 2;
            });
        BattleSpecialRules.RegisterNumberedCondition("4305202", (_, c) => Owner(c) != null
            && Owner(c).MaxEnergy > 40f && Owner(c).MaxEnergy <= 60f);
        BattleSpecialRules.RegisterNumberedCondition("4305203", (_, c) => Owner(c) != null
            && Owner(c).MaxEnergy <= 40f);
        BattleSpecialRules.RegisterNumberedCondition("4302101", (_, c) => Aura(c, "Hydro", "Electro"));
        BattleSpecialRules.RegisterNumberedCondition("4302501", (_, c) => Aura(c, "Pyro", "Electro"));
        // Charts/Enemy.xlsx的正式“生物种类=史莱姆”清单。不是按显示名称模糊匹配。
        BattleSpecialRules.RegisterNumberedCondition("4301701", (_, c) => c?.Target != null
            && c.Target.Type == BattleEntity.EntityType.Enemy && c.Target.EntityID >= 2015 && c.Target.EntityID <= 2029);
        foreach (string id in new[] { "4302703", "4305603", "4308003", "4311203", "4313203" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => OwnCritical(c));
        BattleSpecialRules.RegisterNumberedCondition("4319303", (_, c) => OwnCount(c, "ST_Weapon_4319302") >= 7f);
        BattleSpecialRules.RegisterNumberedCondition("4302801", (_, c) => Owner(c) != null && !Owner(c).HasStatus("ST_Weapon_4302804"));
        BattleSpecialRules.RegisterNumberedCondition("4302803", (_, c) =>
        {
            var owner = Owner(c);
            return owner != null && owner.StatusDict.GetAll("ST_Weapon_4302802").Count >= 5;
        });
        RegisterRefinementProbability("4302901", true);
        RegisterRefinementProbability("4305401", true);
        RegisterRefinementProbability("4308201", true);
        RegisterRefinementProbability("4311101", true);
        RegisterRefinementProbability("4312901", true);
        RegisterRefinementProbability("4303101", false);
        RegisterRefinementProbability("4305901", false);
        RegisterRefinementProbability("4307901", false);
        RegisterRefinementProbability("4313301", false);
        RegisterEnergyHealing("ST_Weapon_4300601", "4300601");
        RegisterEnergyHealing("ST_Weapon_4302301", "4302301");
        RegisterRewardConditions();
        WeaponHitWindowHooks.Register();
        SurfingTimeInitialHP.Register();
        WeaponCompletionHooks.Register();
        PlumageWeaponCount.Register();
        WeaponQualificationCount.Register();
        BattleSpecialRules.RegisterNumberedCondition("4317804", (_, c) =>
            Owner(c)?.HasStatus(NightSoulStatusHandler.BlessingStatusID) == true);
        TalesWeaponCount.Register();
        CashflowWeaponCount.Register();
        AbsolutionWeaponCount.Register();
        NightweaverWeaponCount.Register();
        WeaponDamageCompletionHooks.Register();
        CinnabarWeaponHooks.Register();
        VerdictWeaponTriggers.Register();
        BattleSpecialRules.RegisterBattleReady(WeaponEventCountState.BattleReady);
    }

    private static void RegisterRewardConditions()
    {
        BattleSpecialRules.RegisterNumberedCondition("4309903", (_, c) => OwnCount(c, "ST_Weapon_4309902") > 0f);
        BattleSpecialRules.RegisterNumberedCondition("4312203", (_, c) => OwnCount(c, "ST_Weapon_4312202") > 0f);
        foreach (string id in new[] { "4309905", "4312205", "4313402", "4314602", "4314604", "4315901", "4316001", "4318701", "4319801", "4320702" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => IsElemental(c?.DamageElement));
        for (int theme = 1; theme <= 3; theme++)
        {
            int selected = theme;
            BattleSpecialRules.RegisterNumberedCondition("43130020" + theme, (_, c) =>
                c?.Status?.StatusID2 == WidsithWeaponCount.ID && c.Status.RuntimeValue == selected);
        }
        BattleSpecialRules.RegisterNumberedCondition("4314603", (_, c) => FlowingPurityAccumulator.Matches(c?.Status, c?.Declaration));
        BattleSpecialRules.RegisterNumberedCondition("431460302", (_, c) => FlowingPurityAccumulator.CanReward(c?.Status, c?.Declaration));
        BattleSpecialRules.RegisterNumberedCondition("4316202", (_, c) =>
        {
            var hit = c?.DamageEvent;
            return hit?.Source?.SourceEntity == Owner(c) && hit?.Source?.SourceKind != ReactionSourceKind.DerivedReaction
                && hit?.DamageType == "Normal" && hit.ActualHPDamage > 0f && IsElemental(hit.Element);
        });
        RegisterSigilConditions("43158", 2);
        RegisterSigilConditions("43173", 4);
        RegisterSigilConditions("43183", 4);
        for (int refinement = 1; refinement <= 5; refinement++)
        {
            int rank = refinement;
            BattleSpecialRules.RegisterNumberedCondition("43173060" + refinement, (_, c) => c?.Status?.WeaponContext?.Refinement == rank);
        }
    }

    private static void RegisterSigilConditions(string prefix, int maximum)
    {
        BattleSpecialRules.RegisterNumberedCondition(prefix + "02", (_, c) => Owner(c) != null && OwnStatus(c?.Status, "ST_Weapon_" + prefix + "07") == null);
        BattleSpecialRules.RegisterNumberedCondition(prefix + "0202", (_, c) => OwnCount(c, "ST_Weapon_" + prefix + "03") >= maximum);
    }

    internal static bool IsElemental(string element) => element == "Pyro" || element == "Hydro"
        || element == "Electro" || element == "Cryo" || element == "Anemo" || element == "Dendro" || element == "Geo";

    internal static StatusInstance OwnStatus(StatusInstance source, string id)
    {
        var owner = source?.WeaponContext?.Equipper;
        var status = owner?.GetStatus(id);
        return status != null && status.IsActive && status.Caster == owner
            && status.WeaponContext?.SourceInstanceID == source?.WeaponContext?.SourceInstanceID ? status : null;
    }

    /// <summary>仅接通明确登记的被动Buff参数覆盖；返回true包括合法0，不能再回退成单名参数。</summary>
    public static bool TryResolveBuffScalar(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect == null || effect.EffectType != "Buff") return false;
        if (WeaponPendingAPCount.TryResolve(effect, status, out value)) return true;
        if (WeaponRosterHooks.TryResolve(effect, status, out value)) return true;
        if (WeaponSnapshotHooks.TryResolve(effect, status, out value)) return true;
        if (WeaponShieldAndLifeHooks.TryResolve(effect, status, out value)) return true;
        if (WeaponResourceAndAttributeHooks.TryResolve(effect, status, out value)) return true;
        if (WeaponShieldCreationHooks.TryResolve(effect, status, out value)) return true;
        if (TalesWeaponCount.TryResolve(effect, status, out value)) return true;
        if (effect.StatusEffectID == 431980301 && effect.DamageHook == "Hook(431980301)")
        {
            var source = status?.WeaponContext?.Equipper;
            if (source == null || status.Caster != source || status.StatusID2 != CalamityQuellerWeaponCount.ID
                || status.WeaponContext.WeaponID != 3198 || source.CharacterCtrl == null)
                throw new InvalidOperationException("息灾攻击增量必须读取本来源Count。");
            value = status.RuntimeValue * Param(status, 2) * (source.CharacterCtrl.IsActive ? 1f : 2f);
            return true;
        }
        int weapon, parameter;
        switch (effect.DamageHook?.Trim())
        {
            case "Hook(430630101)": if (effect.StatusEffectID != 430630101) return false; weapon = 3063; parameter = 1; break;
            case "Hook(430630201)": if (effect.StatusEffectID != 430630201) return false; weapon = 3063; parameter = 2; break;
            case "Hook(431140101)": if (effect.StatusEffectID != 431140101) return false; weapon = 3114; parameter = 1; break;
            case "Hook(431140201)": if (effect.StatusEffectID != 431140201) return false; weapon = 3114; parameter = 2; break;
            default: return false;
        }
        var owner = status?.WeaponContext?.Equipper;
        var battle = BattleManager.Instance;
        var data = DataManager.Instance;
        if (owner == null || status.Caster != owner || status.WeaponContext.WeaponID != weapon
            || status.MainData?.StatusID != effect.StatusEffectID / 100)
            throw new InvalidOperationException("千岩参数必须使用本武器装备者的来源上下文。");
        if (battle == null || data == null || owner.Side != BattleSide.Ally)
            throw new InvalidOperationException("千岩参数查询需要完整战斗编队与已加载的角色Nation数据。");
        var roster = new List<int>();
        bool includesOwner = false;
        foreach (var ally in battle.Allies)
        {
            if (ally?.Entity == null) continue;
            roster.Add(ally.Entity.EntityID); // 不筛存活、前后台，也不读取受益者自己的武器精炼。
            if (ally.Entity == owner) includesOwner = true;
        }
        if (!includesOwner) throw new InvalidOperationException("千岩装备者尚未加入本场完整编队。");
        value = Math.Min(4, data.CountCharactersFromNation(roster, "璃月")) * Param(status, parameter);
        return true;
    }

    /// <summary>读表校验与运行时共用的已实装费用参数契约，不放行未知公式或钩子。</summary>
    public static bool SupportsAPCostParameter(int effectID, string parameter, string hook)
        => effectID == 432120101 && parameter?.Trim() == "-Index(1)" && hook?.Trim() == "Hook(432120101)";

    public static bool TryResolveAPCost(StatusEffectData effect, StatusInstance status, out int value)
    {
        value = 0;
        if (effect?.EffectType != "APCost" || !SupportsAPCostParameter(effect.StatusEffectID, effect.Param1, effect.DamageHook)) return false;
        if (status?.WeaponContext?.WeaponID != 3212 || status.StatusID2 != "ST_Weapon_4321201"
            || status.WeaponContext.Equipper == null || status.Caster != status.WeaponContext.Equipper)
            throw new InvalidOperationException("图莱杜拉减费缺少本来源武器参数。");
        float reduction = Param(status, 1);
        if (reduction < 0f || (double)reduction > int.MaxValue || reduction != (int)reduction)
            throw new InvalidOperationException("图莱杜拉普攻减费必须为非负整数。");
        value = -(int)reduction;
        return true;
    }

    // 只约束表中图莱杜拉旧窗口的三条具体效果；不改变其他武器自移除后继续执行的既定效果链。
    public static bool CanExecuteWindowEffect(StatusEffectData effect, StatusInstance source)
    {
        if (!WeaponStackTransferHooks.CanExecute(effect, source)) return false;
        if (effect?.StatusEffectID == 431710301) return WeaponDamageCompletionHooks.CanExecutePride(source);
        if (effect?.StatusEffectID == 430940201)
            return source?.StatusID2 == EndOfLineWeaponCount.ID && source.IsActive && source.RuntimeValue > 0f
                && source.WeaponContext?.Equipper?.HasStatusInstance(source) == true;
        if (effect == null || (effect.StatusEffectID != 432120302 && effect.StatusEffectID != 432120303
            && effect.StatusEffectID != 432120304)) return true;
        var owner = source?.WeaponContext?.Equipper;
        return source != null && source.StatusID2 == TulaytullahWeaponCount.ID && source.IsActive
            && owner != null && ReferenceEquals(owner.GetStatus(TulaytullahWeaponCount.ID), source);
    }

    internal static BattleEntity Owner(ScriptHookContext context)
        => context?.Status?.WeaponContext?.Equipper ?? context?.Caster;

    private static bool HasOwnTargetMark(ScriptHookContext context, string id)
    {
        var owner = context?.Status?.WeaponContext?.Equipper;
        if (owner == null || context.Target == null || context.Target.Side == owner.Side) return false;
        foreach (var mark in context.Target.StatusDict.GetAll(id))
            if (mark.IsActive && mark.Caster == owner && EquipmentStatusLifetime.CanExecute(mark)
                && mark.WeaponContext?.SourceInstanceID == context.Status.WeaponContext.SourceInstanceID)
                return true;
        return false;
    }

    private static int LivingEnemyCount(ScriptHookContext context)
    {
        var owner = Owner(context);
        var battle = BattleManager.Instance;
        if (owner == null || owner.Side != BattleSide.Ally || battle?.Field == null) return -1;
        var enemies = new HashSet<BattleEntity>();
        foreach (var controller in battle.Enemies)
        {
            var enemy = controller?.Entity;
            if (enemy != null && enemy.IsAlive && enemy.Side == BattleSide.Enemy && enemy.SlotPosition > 0
                && ReferenceEquals(battle.Field.GetSlot(BattleSide.Enemy, enemy.SlotPosition)?.Occupant, enemy))
                enemies.Add(enemy);
        }
        return enemies.Count;
    }


    internal static float Param(StatusInstance status, int index)
    {
        if (status?.WeaponContext == null || !status.WeaponContext.ResolvedParams.TryGetValue(index, out float value)
            || float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException("武器效果缺少有效的精炼参数：" + status?.StatusID2 + " Index(" + index + ")");
        return value;
    }

    private sealed class HitFacts
    {
        internal long ID;
        internal BattleEntity Source;
        internal readonly Dictionary<BattleEntity, HashSet<string>> Auras = new Dictionary<BattleEntity, HashSet<string>>();
        internal readonly HashSet<BattleEntity> Frozen = new HashSet<BattleEntity>();
    }
    private sealed class HitScope : IDisposable
    {
        private readonly HitFacts previous;
        private bool disposed;
        internal HitScope(HitFacts facts) { previous = HitSnapshot.Value; HitSnapshot.Value = facts; }
        public void Dispose() { if (!disposed) { disposed = true; HitSnapshot.Value = previous; } }
    }
    private static readonly AsyncLocal<HitFacts> HitSnapshot = new AsyncLocal<HitFacts>();
    private static IDisposable BeginHit(long id, BattleEntity source, IEnumerable<BattleEntity> targets)
    {
        var facts = new HitFacts { ID = id, Source = source };
        if (targets != null)
            foreach (var target in targets)
            {
                if (target == null || facts.Auras.ContainsKey(target)) continue;
                var auras = new HashSet<string>();
                foreach (string element in new[] { "Hydro", "Cryo", "Pyro", "Electro" })
                    if (ElementQuery.HasAura(target, element)) auras.Add(element);
                facts.Auras.Add(target, auras);
                if (FrozenReactionHandler.IsFrozen(target)) facts.Frozen.Add(target);
            }
        return new HitScope(facts);
    }
    private static bool Aura(ScriptHookContext context, string a, string b)
    {
        var target = context?.Target;
        if (target == null) return false;
        if (BattleEventHub.CurrentHitId == 0)
            return ElementQuery.HasAura(target, a) || ElementQuery.HasAura(target, b);
        var frame = HitSnapshot.Value;
        return frame != null && frame.ID == BattleEventHub.CurrentHitId && frame.Source == Owner(context)
            && frame.Auras.TryGetValue(target, out var auras) && (auras.Contains(a) || auras.Contains(b));
    }
    private static bool Frozen(ScriptHookContext context)
    {
        if (context?.Target == null) return false;
        if (BattleEventHub.CurrentHitId == 0) return FrozenReactionHandler.IsFrozen(context.Target);
        var frame = HitSnapshot.Value;
        return frame != null && frame.ID == BattleEventHub.CurrentHitId && frame.Source == Owner(context)
            && frame.Frozen.Contains(context.Target);
    }

    internal static bool HasCryoInfluence(StatusInstance source, BattleEntity target)
    {
        var context = new ScriptHookContext { Status = source, Caster = source?.Caster, Target = target };
        return Aura(context, "Cryo", "Cryo") || Frozen(context);
    }

    private static float OwnCount(ScriptHookContext context, string id)
    {
        var status = Owner(context)?.GetStatus(id);
        return status != null && status.IsActive
            && status.WeaponContext?.SourceInstanceID == context?.Status?.WeaponContext?.SourceInstanceID
            ? status.RuntimeValue : 0f;
    }

    private static bool OwnCritical(ScriptHookContext context)
    {
        BattleEntity owner = Owner(context);
        var hit = context?.DamageEvent;
        return owner != null && hit != null && hit.Source?.SourceEntity == owner
            && hit.HitLanded && hit.Critical?.IsCritical == true;
    }

    private static void RegisterRefinementProbability(string root, bool requiresCritical)
    {
        for (int refinement = 1; refinement <= 5; refinement++)
        {
            int rank = refinement;
            string id = root + rank.ToString("D2", CultureInfo.InvariantCulture);
            BattleSpecialRules.RegisterNumberedCondition(id, (_, context) =>
            {
                if (context?.Status?.WeaponContext?.Refinement != rank) return false;
                var hit = context.DamageEvent;
                BattleEntity owner = Owner(context);
                if (owner == null || hit == null || hit.Source?.SourceEntity != owner || !hit.HitLanded) return false;
                if (requiresCritical ? hit.Critical?.IsCritical != true : hit.DamageType != "Skill" || hit.ActualHPDamage <= 0f)
                    return false;
                float chance = Param(context.Status, 1);
                float roll = BattleRandom.NextFloat01();
                bool success = roll < chance;
                LogManager.Log(LogCategory.ScriptHook, $"Hook({id}) rate={chance:R} roll={roll:R} "
                    + $"hit={hit.RuntimeHitID} target={hit.Target?.EntityID} success={success}");
                return success;
            });
        }
    }

    private sealed class EnergySubscription { internal IDisposable Value; }
    private static readonly ConditionalWeakTable<StatusInstance, EnergySubscription> EnergySubscriptions
        = new ConditionalWeakTable<StatusInstance, EnergySubscription>();

    private static bool EnergyMatches(StatusInstance status, ResourceEventData fact)
        => status?.WeaponContext?.Equipper != null && fact != null && fact.Resource == "Energy" && fact.Subject == status.WeaponContext.Equipper
            && fact.Added > 0d;

    private static void RegisterEnergyHealing(string statusID, string hookID)
    {
        BattleSpecialRules.RegisterNumberedCondition(hookID, (_, c) => EnergyMatches(c?.Status, c?.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterStatusLifecycle(statusID, (status, host) =>
        {
            BattleEntity owner = WeaponCountLifetime.RequireOwner(status, host);
            var subscription = new EnergySubscription();
            EnergySubscriptions.Add(status, subscription);
            subscription.Value = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (header, fact) =>
            {
                if (!EnergyMatches(status, fact)) return;
                var declaration = BattleEventHub.Current;
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
                foreach (var action in actions)
                    if (action.ActionType == "OnTrigger" && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id) && id == hookID)
                        owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                        { Caster = owner, Target = owner, Status = status, StatusHost = host, Declaration = declaration },
                            () => status.IsActive && owner.HasStatusInstance(status));
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
        }, (status, host) =>
        {
            if (!EnergySubscriptions.TryGetValue(status, out var subscription)) return;
            subscription.Value?.Dispose();
            EnergySubscriptions.Remove(status);
        });
    }

    // 只豁免明确接入的类型/效果组合，其他DamageHook仍由原入口明确报未支持。
    public static bool HandlesEffectHook(StatusEffectData effect)
        => effect != null && ((effect.EffectType == "Heal"
            && ((effect.StatusEffectID == 430060101 && effect.DamageHook == "Hook(430060101)")
                || (effect.StatusEffectID == 430230101 && effect.DamageHook == "Hook(430230101)")
                || IsTargetHealing(effect)))
            || (effect.EffectType == "Shield" && effect.StatusEffectID == 430570101
                && effect.DamageHook == "Hook(430570101)") || HandlesEnergyHook(effect)
            || WeaponStackTransferHooks.HandlesEnergy(effect) || WeaponSnapshotHooks.HandlesApply(effect)
            || WeaponShieldCreationHooks.HandlesApply(effect) || ForceCritical(effect));

    private static bool HandlesEnergyHook(StatusEffectData effect)
        => effect?.EffectType == "GainEnergy" && effect.DamageHook == "Hook(" + effect.StatusEffectID + ")"
            && (effect.StatusEffectID == 430640301 || effect.StatusEffectID == 431150301 || effect.StatusEffectID == 431150401
                || effect.StatusEffectID == 431360301 || effect.StatusEffectID == 431360401
                || effect.StatusEffectID == 432010202 || effect.StatusEffectID == 432040103
                || effect.StatusEffectID == 432130301 || effect.StatusEffectID == 432130401 || effect.StatusEffectID == 432140202
                || effect.StatusEffectID == 432230202 || effect.StatusEffectID == 432240202
                || effect.StatusEffectID == 430760101 || effect.StatusEffectID == 431210101 || effect.StatusEffectID == 431230101);

    public static bool TryResolveEnergy(StatusEffectData effect, StatusInstance status, out float value)
    {
        if (WeaponStackTransferHooks.TryResolveEnergy(effect, status, out value)) return true;
        value = 0f;
        if (!HandlesEnergyHook(effect)) return false;
        int weapon = effect.StatusEffectID / 10000 - 40000;
        int parameter = effect.StatusEffectID == 430640301 || effect.StatusEffectID == 431150301 || effect.StatusEffectID == 431150401 ? 2 : 1;
        if (effect.StatusEffectID == 432010202 || effect.StatusEffectID == 432040103 || effect.StatusEffectID == 432140202) parameter = 3;
        if (effect.StatusEffectID == 432230202) parameter = 2;
        if (effect.StatusEffectID == 432240202) parameter = 4;
        if (status?.WeaponContext?.WeaponID != weapon || status.Caster != status.WeaponContext.Equipper
            || status.MainData?.StatusID != effect.StatusEffectID / 100 || effect.Param1 != "Index(" + parameter + ")")
            throw new InvalidOperationException("武器回能参数缺少本来源Index(" + parameter + ")：" + effect.StatusEffectID);
        value = Param(status, parameter);
        return true; // 只交付参数，实际能量变化和声明仍由统一GainEnergy入口提交。
    }

    public static string ShieldFormula(StatusEffectData effect, StatusInstance status)
    {
        if (effect?.StatusEffectID == 430570101 && HandlesEffectHook(effect))
        {
            var owner = status?.WeaponContext?.Equipper;
            if (owner == null || status.Caster != owner || status.WeaponContext.WeaponID != 3057
                || effect.Param1 != "Index(1)*TotalHP")
                throw new InvalidOperationException("钟剑护盾必须读取本装备者Index(1)*TotalHP。");
        }
        // Shield与现有Damage/Heal一致，先展开来源Index，再由原公式解析器求当前属性。
        // 未登记的DamageHook仍在RepeatEffect入口报未支持，不因展开Index而放行。
        return WeaponExpressionEvaluator.Expand(effect?.Param1, status);
    }

    public static bool ForceCritical(StatusEffectData effect)
        => effect?.EffectType == "Damage" && effect.StatusEffectID == 430150101
            && effect.DamageHook == "Hook(430150101)";

    public static string HealingFormula(StatusEffectData effect, StatusInstance status)
    {
        if (!HandlesEffectHook(effect) || effect.EffectType != "Heal") return effect.Param1;
        if (IsTargetHealing(effect))
        {
            var owner = status?.WeaponContext?.Equipper;
            if (owner == null || status.Caster != owner || status.WeaponContext.WeaponID != 3136
                || status.MainData?.StatusID != effect.StatusEffectID / 100 || effect.Param1 != "Index(2)*Target.TotalHP")
                throw new InvalidOperationException("试作金珀治疗必须读取本武器Index(2)及每名受术者自己的生命上限。");
            return effect.Param1; // Index在每段展开，Target.TotalHP在段内逐目标准备。
        }
        var fact = ScriptHookContext.Current?.Declaration?.Get<ResourceEventData>();
        if (!EnergyMatches(status, fact)) throw new InvalidOperationException("逐能量治疗缺少本次真实获能声明。");
        // 能量保留float小数，乘本次实际Added；治疗量仍走统一HealBatch与加成/抵契。
        return effect.Param1 + "*" + fact.Added.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool IsTargetHealing(StatusEffectData effect)
        => effect?.EffectType == "Heal" && (effect.StatusEffectID == 431360302 || effect.StatusEffectID == 431360402)
            && effect.DamageHook == "Hook(" + effect.StatusEffectID + ")";
}

/// <summary>仅为武器已明确选择的整窗刷新策略共用计时操作；不定义Count默认行为。</summary>
internal static class WeaponCountLifetime
{
    internal static BattleEntity RequireApplication(StatusInstance status, BattleEntity host, CountStatusApplication application, int weaponID)
    {
        RequireOwner(status, host, weaponID);
        if (application?.Caster != host || application.WeaponContext?.Equipper != host
            || application.WeaponContext.WeaponID != weaponID
            || application.WeaponContext.SourceInstanceID != status.WeaponContext.SourceInstanceID)
            throw new InvalidOperationException("Count重施不能跨武器来源合并。");
        return host;
    }
    internal static BattleEntity RequireOwner(StatusInstance status, object host, int weaponID = 0)
    {
        if (!(host is BattleEntity owner) || owner != status?.Caster || owner != status.WeaponContext?.Equipper
            || owner.CharacterCtrl == null || (weaponID > 0 && status.WeaponContext.WeaponID != weaponID))
            throw new InvalidOperationException("武器Count/监听必须属于本武器装备者：" + status?.StatusID2);
        return owner;
    }

    internal static void Refresh(StatusInstance status, BattleEntity owner, int duration, int phase)
    {
        if (phase == 0) phase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : status.AddInPhase;
        var battle = BattleManager.Instance;
        if (owner.HasStatusInstance(status)) battle?.UnregisterStatusTick(status);
        status.StackCount = 1;
        status.IsPermanent = duration == 0;
        status.RemainingPhaseCount = duration;
        status.AddInPhase = phase;
        status.TriggerPhase = 0;
        if (owner.HasStatusInstance(status)) battle?.RegisterStatusTick(status, owner, null);
        // 仅处理这些已声明Self绑定子项的武器。保留具体实例，不重绑/重建或重置行动账本。
        foreach (var child in status.BoundStatuses)
        {
            if (child == null || !child.IsActive || !owner.HasStatusInstance(child)) continue;
            battle?.UnregisterStatusTick(child);
            child.IsPermanent = status.IsPermanent;
            child.RemainingPhaseCount = duration;
            child.AddInPhase = phase;
            battle?.RegisterStatusTick(child, owner, null);
        }
    }
}

/// <summary>登记处逐个明确上限、期限、计时阶段和数值单位；获点只由各自Action调用。</summary>
internal sealed class WeaponIncrementCount : CountStatusHandler
{
    private readonly int weaponID, maximum, duration, phase;
    private readonly bool storesParam;
    internal WeaponIncrementCount(int weaponID, int maximum, int duration, int phase, bool storesParam = false)
    { this.weaponID = weaponID; this.maximum = maximum; this.duration = duration; this.phase = phase; this.storesParam = storesParam; }
    private float Unit(StatusInstance status) => storesParam ? WeaponHooks.Param(status, 1) : 1f;
    public override void OnCreated(StatusInstance status, object host)
    {
        BattleEntity owner = WeaponCountLifetime.RequireOwner(status, host, weaponID);
        status.RuntimeValue = Unit(status);
        WeaponCountLifetime.Refresh(status, owner, duration, phase);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireOwner(status, host, weaponID);
        if (application.Caster != host || application.WeaponContext?.SourceInstanceID != status.WeaponContext.SourceInstanceID)
            throw new InvalidOperationException("Count重施不能跨武器来源合并。");
        float unit = Unit(status);
        status.RuntimeValue = Math.Min(maximum * unit, status.RuntimeValue + unit);
        WeaponCountLifetime.Refresh(status, host, duration, phase);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}

/// <summary>3061螭骨：开场0，前台阶段1加点，真实HP伤害响应扣点，后台保留。</summary>
internal sealed class SerpentSpineWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4306101";
    private sealed class State { internal int LastTurn = -1; internal readonly HashSet<long> DamageEvents = new HashSet<long>(); }
    public override void OnCreated(StatusInstance status, object host)
    {
        WeaponCountLifetime.RequireOwner(status, host, 3061);
        status.RuntimeValue = 0f; status.StackCount = 1; status.IsPermanent = true;
        status.RemainingPhaseCount = 0; status.AddInPhase = status.TriggerPhase = 1;
        status.CountRuntimeState = new State();
    }
    public override void OnItsTurn(StatusInstance status, object host)
    {
        BattleEntity owner = WeaponCountLifetime.RequireOwner(status, host, 3061);
        var battle = BattleManager.Instance; var state = (State)status.CountRuntimeState;
        if (battle == null || (int)battle.CurrentPhase != 1 || state.LastTurn == battle.TurnCount) return;
        state.LastTurn = battle.TurnCount;
        if (owner.IsAlive && owner.CharacterCtrl.IsActive) status.RuntimeValue = Math.Min(5f, status.RuntimeValue + 1f);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireOwner(status, host, 3061);
        var damage = ScriptHookContext.Current?.DamageEvent;
        if (application.Caster != host || application.WeaponContext?.SourceInstanceID != status.WeaponContext.SourceInstanceID
            || damage?.Target != host || damage.ActualHPDamage <= 0f || damage.Declaration == null)
            throw new InvalidOperationException("螭骨扣点必须来自装备者本次已提交的真实HP伤害。");
        if (((State)status.CountRuntimeState).DamageEvents.Add(damage.Declaration.Header.EventId))
            status.RuntimeValue = Math.Max(0f, status.RuntimeValue - 1f);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>3093落霞：序号与最终比例分开保存；两回合冷却由原Action账本控制。</summary>
internal sealed class FadingTwilightWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4309301";
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3093);
        status.CountRuntimeState = 0; status.RuntimeValue = WeaponHooks.Param(status, 1);
        WeaponCountLifetime.Refresh(status, owner, 0, 2);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireOwner(status, host, 3093);
        if (application.Caster != host || application.WeaponContext?.SourceInstanceID != status.WeaponContext.SourceInstanceID)
            throw new InvalidOperationException("落霞不能跨来源重施。");
        int next = ((int)status.CountRuntimeState + 1) % 3;
        status.RuntimeValue = WeaponHooks.Param(status, next + 1);
        status.CountRuntimeState = next;
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>测距规/勘探钻机：消费标记前保存实际份数，奖励查询不再读取已清空的池。</summary>
internal sealed class WeaponMarkRewardCount : CountStatusHandler
{
    private readonly int weaponID;
    private readonly string marksID;
    internal WeaponMarkRewardCount(int weaponID, string marksID) { this.weaponID = weaponID; this.marksID = marksID; }
    private void Save(StatusInstance status, BattleEntity owner)
    {
        var marks = WeaponHooks.OwnStatus(status, marksID);
        if (marks == null || marks.RuntimeValue < 1f || marks.RuntimeValue > 3f || marks.RuntimeValue != (int)marks.RuntimeValue)
            throw new InvalidOperationException("抗争奖励必须在本来源团结标记清除前保存实际1至3枚。");
        status.RuntimeValue = marks.RuntimeValue;
        WeaponCountLifetime.Refresh(status, owner, 3, 0);
    }
    public override void OnCreated(StatusInstance status, object host) => Save(status, WeaponCountLifetime.RequireOwner(status, host, weaponID));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Save(status, WeaponCountLifetime.RequireApplication(status, host, application, weaponID));
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}

/// <summary>流浪乐章：先统一抽一个主题，再由三个只读绑定条件选中一个子项。</summary>
internal sealed class WidsithWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4313002";
    private static void Draw(StatusInstance status, BattleEntity owner)
    {
        status.RuntimeValue = BattleRandom.NextInt(1, 4);
        WeaponCountLifetime.Refresh(status, owner, 2, 0);
        LogManager.Log(LogCategory.ScriptHook, "流浪乐章本次主题=" + status.RuntimeValue);
    }
    public override void OnCreated(StatusInstance status, object host) => Draw(status, WeaponCountLifetime.RequireOwner(status, host, 3130));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3130);
        StatusBindingSystem.RemoveBindings(status);
        Draw(status, host);
        StatusBindingSystem.ApplyBindings(status, host);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}

/// <summary>盈满之实：正式反应重设衰减原点；每满一整回合同阶段只掉一份。</summary>
internal sealed class FruitOfFulfillmentWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4314302";
    private sealed class State
    {
        internal int NextTurn;
        internal readonly HashSet<long> Reactions = new HashSet<long>();
    }
    private static void Gain(StatusInstance status, BattleEntity owner)
    {
        var context = ScriptHookContext.Current;
        var declaration = context?.Declaration ?? context?.ReactionEvent?.Declaration;
        var fact = declaration?.Get<ReactionEventData>();
        var battle = BattleManager.Instance;
        if (fact?.Source != owner || battle == null || (int)battle.CurrentPhase < 1 || (int)battle.CurrentPhase > 6)
            throw new InvalidOperationException("盈满之实获点必须有装备者正式反应与实际回合阶段。");
        var state = (State)status.CountRuntimeState;
        if (!state.Reactions.Add(declaration.Header.EventId)) return;
        status.RuntimeValue = Math.Min(5f, status.RuntimeValue + 1f);
        bool attached = owner.HasStatusInstance(status);
        if (attached) battle.UnregisterStatusTick(status);
        status.StackCount = 1; status.IsPermanent = true; status.RemainingPhaseCount = 0;
        status.AddInPhase = status.TriggerPhase = (int)battle.CurrentPhase;
        state.NextTurn = battle.TurnCount + 1;
        if (attached) battle.RegisterStatusTick(status, owner, null);
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3143);
        status.RuntimeValue = 0f; status.CountRuntimeState = new State();
        Gain(status, owner);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Gain(status, WeaponCountLifetime.RequireApplication(status, host, application, 3143));
        return status;
    }
    public override void OnItsTurn(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3143);
        var battle = BattleManager.Instance; var state = (State)status.CountRuntimeState;
        if (battle == null || (int)battle.CurrentPhase != status.TriggerPhase || battle.TurnCount < state.NextTurn) return;
        state.NextTurn = battle.TurnCount + 1;
        status.RuntimeValue = Math.Max(0f, status.RuntimeValue - 1f);
        if (status.RuntimeValue == 0f) owner.RemoveStatusInstance(status);
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>纯水流华私有累计器，不污染公共生命之契，不从治疗事实重复记账。</summary>
internal sealed class FlowingPurityAccumulator : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4314603";
    private sealed class State
    {
        internal readonly HashSet<long> Seen = new HashSet<long>();
        internal long CompletedEvent;
        internal long RewardedEvent;
    }
    internal static bool Matches(StatusInstance status, BattleDeclaration declaration)
    {
        var fact = declaration?.Get<ResourceEventData>();
        return status?.WeaponContext?.WeaponID == 3146 && fact != null && fact.Subject == status.WeaponContext.Equipper
            && fact.Resource == "BondOfLife" && fact.Removed > 0d;
    }
    internal static bool CanReward(StatusInstance source, BattleDeclaration declaration)
    {
        if (!Matches(source, declaration)) return false;
        var fact = declaration.Get<ResourceEventData>();
        var accumulator = WeaponHooks.OwnStatus(source, ID);
        var state = accumulator?.CountRuntimeState as State;
        return fact.Before > 0d && fact.After <= 0d && state != null
            && state.CompletedEvent == declaration.Header.EventId && state.RewardedEvent != declaration.Header.EventId;
    }
    internal static float Take(StatusInstance source, BattleDeclaration declaration)
    {
        if (!CanReward(source, declaration)) throw new InvalidOperationException("纯水流华奖励缺少本次完整消契已提交的累计快照。");
        var accumulator = WeaponHooks.OwnStatus(source, ID);
        var state = (State)accumulator.CountRuntimeState;
        float amount = accumulator.RuntimeValue;
        // 奖励处理器先成功算出有限快照，再提交清空，保留永久累计器与行动账本。
        float bonus = Math.Min(amount / 1000f * WeaponHooks.Param(source, 2), WeaponHooks.Param(source, 3));
        if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0f)
            throw new InvalidOperationException("纯水流华消契增伤必须是有效非负数。");
        source.RuntimeValue = bonus;
        accumulator.RuntimeValue = 0f;
        state.RewardedEvent = declaration.Header.EventId;
        return bonus;
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3146);
        status.RuntimeValue = 0f; status.CountRuntimeState = new State();
        WeaponCountLifetime.Refresh(status, owner, 0, 0);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3146);
        var declaration = ScriptHookContext.Current?.Declaration;
        if (!Matches(status, declaration)) throw new InvalidOperationException("纯水流华累计只接受本装备者实际消契声明。");
        var state = (State)status.CountRuntimeState;
        if (!state.Seen.Add(declaration.Header.EventId)) return status;
        var fact = declaration.Get<ResourceEventData>();
        status.RuntimeValue += (float)fact.Removed;
        if (fact.Before > 0d && fact.After <= 0d) state.CompletedEvent = declaration.Header.EventId;
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

internal sealed class FlowingPurityReward : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4314604";
    private static void Save(StatusInstance status, BattleEntity owner)
    {
        FlowingPurityAccumulator.Take(status, ScriptHookContext.Current?.Declaration);
        WeaponCountLifetime.Refresh(status, owner, 4, 0);
    }
    public override void OnCreated(StatusInstance status, object host) => Save(status, WeaponCountLifetime.RequireOwner(status, host, 3146));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Save(status, WeaponCountLifetime.RequireApplication(status, host, application, 3146));
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}

/// <summary>仅苍古/松籁/终末选择的同名整组接管策略，不是所有队伍Buff的默认覆盖规则。</summary>
internal sealed class WeaponTeamRewardCount : CountStatusHandler
{
    private readonly int weaponID;
    internal WeaponTeamRewardCount(int weaponID) { this.weaponID = weaponID; }
    private static void ClearOld(StatusInstance incoming, BattleEntity owner)
    {
        var battle = BattleManager.Instance;
        var entities = new HashSet<BattleEntity> { owner };
        if (battle != null)
        {
            foreach (var ally in battle.Allies) if (ally?.Entity != null) entities.Add(ally.Entity);
            foreach (var enemy in battle.Enemies) if (enemy?.Entity != null) entities.Add(enemy.Entity);
        }
        foreach (var entity in entities)
            if (entity.Side == owner.Side)
                foreach (var old in entity.GetStatusInstances(incoming.StatusID2))
                    if (!ReferenceEquals(old, incoming)) entity.RemoveStatusInstance(old, "Replaced");
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weaponID);
        ClearOld(status, owner);
        status.RuntimeValue = 1f;
        WeaponCountLifetime.Refresh(status, owner, 3, 0);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, weaponID);
        ClearOld(status, host);
        StatusBindingSystem.RemoveBindings(status);
        status.RuntimeValue = 1f;
        WeaponCountLifetime.Refresh(status, host, 3, 0);
        StatusBindingSystem.ApplyBindings(status, host);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}

/// <summary>裁叶/猎人按本次当前精通提供逐目标基础加算；预留、取消、旧轮次隔离均在伤害作用域内。</summary>
internal sealed class WeaponEMHitCount : CountStatusHandler
{
    private readonly int weaponID, uses;
    private readonly HashSet<string> types;
    internal WeaponEMHitCount(int weaponID, int uses, params string[] types)
    { this.weaponID = weaponID; this.uses = uses; this.types = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase); }
    private void Reset(StatusInstance status, BattleEntity owner)
    {
        status.CountRuntimeState = new object(); // 每轮独立身份；旧预留的回调不得增减新池。
        status.RuntimeValue = uses;
        WeaponCountLifetime.Refresh(status, owner, 3, 0);
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, weaponID));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, weaponID));
        return status;
    }
    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weaponID);
        if (context?.Attacker != owner || !owner.IsAlive || context.Target == null || context.Target.Side == owner.Side
            || !status.IsActive || !owner.HasStatusInstance(status) || status.RuntimeValue < 1f
            || context.SourceKind == ReactionSourceKind.DerivedReaction || !types.Contains(context.DamageType ?? "")) return null;
        float bonus = WeaponHooks.Param(status, 2) * owner.GetFinalEM(context.EffectID, context.SkillID,
            context.SkillType, context.Element, context.Target);
        if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0f)
            throw new InvalidOperationException("精通基础加算必须为有效非负数。");
        object generation = status.CountRuntimeState;
        status.RuntimeValue -= 1f;
        bool Current() => status.IsActive && owner.HasStatusInstance(status) && ReferenceEquals(status.CountRuntimeState, generation);
        return new CountDamageContribution(bonus,
            result => { if (Current() && status.RuntimeValue <= 0f) owner.RemoveStatusInstance(status); },
            () => { if (Current()) status.RuntimeValue += 1f; });
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>仅供下面明确登记的武器存辅助数据/正式声明订阅；没有Count初值或增减的默认规则。</summary>
internal class WeaponEventCountState : IDisposable
{
    internal bool LayoutReady, OnField;
    internal int APRemainder;
    internal long FrontSince = -1, NextTick = -1;
    internal Action<BattleManager> WhenBattleReady;
    internal readonly HashSet<long> APEvents = new HashSet<long>();
    internal readonly HashSet<long> SwitchEvents = new HashSet<long>();
    internal readonly HashSet<long> Hits = new HashSet<long>();
    private readonly List<IDisposable> listeners = new List<IDisposable>();
    internal void Listen<T>(StatusInstance status, BattleEntity owner, BattleEventKind kind,
        Action<BattleEventHeader, T> receive) where T : BattleEventData
    {
        listeners.Add(BattleEventHub.Subscribe(kind, receive, status.ApplyOrder,
            () => status.IsActive && owner.HasStatusInstance(status) && ReferenceEquals(status.CountRuntimeState, this)));
    }
    public void Dispose()
    {
        foreach (var listener in listeners) listener.Dispose();
        listeners.Clear(); WhenBattleReady = null;
        APEvents.Clear(); SwitchEvents.Clear(); Hits.Clear();
    }
    internal static void BattleReady(BattleManager battle)
    {
        foreach (var ally in battle.Allies)
            if (ally?.Entity != null)
                foreach (var status in ally.Entity.GetStatusList())
                    if (status.IsActive && status.CountRuntimeState is WeaponEventCountState state)
                        state.WhenBattleReady?.Invoke(battle);
    }
}

internal static class WeaponCountClock
{
    internal static long At(int turn, int phase)
    {
        if (turn < 1 || phase < 1 || phase > 6) throw new InvalidOperationException("武器计时缺少真实回合/阶段。");
        return ((long)turn - 1L) * 6L + phase - 1;
    }
    internal static long Now()
    {
        var battle = BattleManager.Instance;
        if (battle == null) throw new InvalidOperationException("武器计时需要真实战斗阶段。");
        return At(battle.TurnCount, (int)battle.CurrentPhase);
    }
    internal static void Schedule(StatusInstance status, BattleEntity owner, long nextTick)
    {
        var battle = BattleManager.Instance;
        bool attached = owner.HasStatusInstance(status);
        if (attached) battle?.UnregisterStatusTick(status);
        status.StackCount = 1; status.IsPermanent = true; status.RemainingPhaseCount = 0;
        status.AddInPhase = status.TriggerPhase = nextTick < 0 ? 1 : (int)(nextTick % 6L) + 1;
        if (attached) battle?.RegisterStatusTick(status, owner, null);
    }
    internal static void RemoveState(StatusInstance status)
    {
        (status.CountRuntimeState as WeaponEventCountState)?.Dispose();
        status.CountRuntimeState = null; status.RuntimeValue = 0f;
    }
    internal static ScriptHookContext RequireWindowSource(StatusInstance status)
    {
        var context = ScriptHookContext.Current;
        if (!ReferenceEquals(context?.Status, status) || !status.IsActive
            || !status.WeaponContext.Equipper.HasStatusInstance(status))
            throw new InvalidOperationException("获点必须来自仍有效的本次武器窗口实例。");
        return context;
    }
}

/// <summary>3087暗巷：开场0，后台每25真实AP加比例；前台满1整回合后每25AP衰减。</summary>
internal sealed class AlleyHunterWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4308701";
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3087);
        var state = new WeaponEventCountState(); status.CountRuntimeState = state;
        status.RuntimeValue = 0f;
        WeaponCountClock.Schedule(status, owner, -1);
        state.WhenBattleReady = battle =>
        {
            if (state.LayoutReady) return;
            state.LayoutReady = true; state.OnField = owner.CharacterCtrl.IsActive;
            state.FrontSince = state.OnField ? WeaponCountClock.Now() : -1;
        };
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3087);
        var context = WeaponCountClock.RequireWindowSource(status);
        var state = (WeaponEventCountState)status.CountRuntimeState;
        if (!state.LayoutReady) throw new InvalidOperationException("暗巷猎手尚未完成真实编队初始化。");
        var declaration = context.Declaration;
        var change = declaration?.Get<SwitchEventData>();
        if (change != null && (change.Subject == host || change.Outgoing == host))
        {
            if (!state.SwitchEvents.Add(declaration.Header.EventId)) return status;
            state.APRemainder = 0; state.OnField = change.Subject == host;
            state.FrontSince = state.OnField ? WeaponCountClock.At(change.Turn, change.Phase) : -1;
            return status;
        }
        var payment = declaration?.Get<APSpentEventData>();
        if (payment == null || payment.Subject?.Side != BattleSide.Ally || payment.Amount <= 0)
            throw new InvalidOperationException("暗巷猎手重施只接受真实换人或正数AP付款。");
        if (!state.APEvents.Add(declaration.Header.EventId)) return status;
        bool onField = payment.WasOnField(host);
        if (onField != state.OnField) throw new InvalidOperationException("暗巷猎手AP与换人声明顺序不一致。");
        long time = WeaponCountClock.At(payment.Turn, payment.Phase);
        if (onField && (state.FrontSince < 0 || time < state.FrontSince + 6L)) return status;
        long total = (long)state.APRemainder + payment.Amount;
        long steps = total / 25L; state.APRemainder = (int)(total % 25L);
        float delta = steps * WeaponHooks.Param(status, onField ? 3 : 1);
        status.RuntimeValue = onField ? Math.Max(0f, status.RuntimeValue - delta)
            : Math.Min(WeaponHooks.Param(status, 2), status.RuntimeValue + delta);
        return status;
    }
    public override void OnItsTurn(StatusInstance status, object host)
    {
        WeaponCountLifetime.RequireOwner(status, host, 3087);
        // 阶段入口不送/扣增伤；AP分支用付款原始时间与FrontSince比较，不能把切入当回合已满。
        // FrontSince保存完整回合+阶段，因此阶段1之外满一回合也不会被延后到下一阶段1。
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}

/// <summary>3198息灾：本次5回合窗口只累计创建后的正式付款，每25AP加1至6。</summary>
internal sealed class CalamityQuellerWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4319803";
    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        (status.CountRuntimeState as WeaponEventCountState)?.Dispose();
        var state = new WeaponEventCountState(); status.CountRuntimeState = state;
        status.RuntimeValue = 0f;
        WeaponCountLifetime.Refresh(status, owner, 5, 0);
        state.Listen<APSpentEventData>(status, owner, BattleEventKind.APSpent, (header, payment) =>
        {
            if (payment.Subject?.Side != BattleSide.Ally || payment.Amount <= 0 || !state.APEvents.Add(header.EventId)) return;
            long total = (long)state.APRemainder + payment.Amount;
            long steps = total / 25L; state.APRemainder = (int)(total % 25L);
            status.RuntimeValue = Math.Min(6f, status.RuntimeValue + steps);
        });
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3198));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, 3198));
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}

/// <summary>3212图莱杜拉：AP与普通命中两路共用比例上限；获点不刷新4回合。</summary>
internal sealed class TulaytullahWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4321203";
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3212);
        var origin = ScriptHookContext.Current?.Status;
        if (origin?.StatusID2 != "ST_Weapon_4321202" || !origin.IsActive || !owner.HasStatusInstance(origin)
            || origin.WeaponContext?.SourceInstanceID != status.WeaponContext.SourceInstanceID)
            throw new InvalidOperationException("图莱杜拉只有正式战技窗口监听可以从无到有建窗。");
        status.RuntimeValue = 0f; status.CountRuntimeState = new WeaponEventCountState();
        WeaponCountLifetime.Refresh(status, owner, 4, 0);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3212);
        var context = WeaponCountClock.RequireWindowSource(status);
        var state = (WeaponEventCountState)status.CountRuntimeState;
        float gained;
        var payment = context.Declaration?.Get<APSpentEventData>();
        if (payment != null)
        {
            if (payment.Subject?.Side != BattleSide.Ally || payment.Amount <= 0 || !state.APEvents.Add(context.Declaration.Header.EventId)) return status;
            long total = (long)state.APRemainder + payment.Amount;
            long steps = total / 25L; state.APRemainder = (int)(total % 25L);
            gained = steps * WeaponHooks.Param(status, 2);
        }
        else
        {
            var hit = context.DamageEvent;
            if (hit?.Source?.SourceEntity != host || hit.Source.SourceKind == ReactionSourceKind.DerivedReaction
                || !hit.HitLanded || hit.DamageType != "Normal")
                throw new InvalidOperationException("图莱杜拉获点必须为本窗正数付款或本人Normal真实命中。");
            long id = hit.Declaration?.Header.HitId ?? hit.RuntimeHitID;
            if (id <= 0) throw new InvalidOperationException("图莱杜拉命中缺少真实逻辑HitId。");
            if (!state.Hits.Add(id)) return status;
            gained = WeaponHooks.Param(status, 3);
        }
        status.RuntimeValue = Math.Min(WeaponHooks.Param(status, 4), status.RuntimeValue + gained);
        return status; // 不重建、续时或移动阶段；满额同样维持原期限。
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}

/// <summary>3207四风：真实布局确定后，连续上场每满一整回合加1至4，下场/倒下归零但保留管理器。</summary>
internal sealed class LostPrayerWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4320702";
    private static void SetField(StatusInstance status, BattleEntity owner, WeaponEventCountState state, bool onField, long time)
    {
        state.OnField = onField; status.RuntimeValue = 0f;
        state.NextTick = onField ? time + 6L : -1;
        WeaponCountClock.Schedule(status, owner, state.NextTick);
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3207);
        var state = new WeaponEventCountState(); status.CountRuntimeState = state;
        status.RuntimeValue = 0f; WeaponCountClock.Schedule(status, owner, -1);
        state.WhenBattleReady = battle =>
        {
            if (state.LayoutReady) return;
            state.LayoutReady = true;
            SetField(status, owner, state, owner.IsAlive && owner.CharacterCtrl.IsActive, WeaponCountClock.Now());
        };
        state.Listen<SwitchEventData>(status, owner, BattleEventKind.SwitchResolved, (header, change) =>
        {
            if (!state.LayoutReady || (change.Subject != owner && change.Outgoing != owner) || !state.SwitchEvents.Add(header.EventId)) return;
            SetField(status, owner, state, change.Subject == owner && owner.IsAlive, WeaponCountClock.At(change.Turn, change.Phase));
        });
        state.Listen<DeathEventData>(status, owner, BattleEventKind.Death, (header, death) =>
        {
            if (death.Subject == owner) SetField(status, owner, state, false, 0);
        });
        state.Listen<HPChangedEventData>(status, owner, BattleEventKind.HPChanged, (header, hp) =>
        {
            if (state.LayoutReady && hp.Subject == owner && hp.Before <= 0f && hp.After > 0f)
                SetField(status, owner, state, hp.SubjectWasOnField, WeaponCountClock.At(hp.Turn, hp.Phase));
        });
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
        => throw new InvalidOperationException("四风永久Count没有普通重施规则；由正式布局/换人/死亡/阶段入口维护。");
    public override void OnItsTurn(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3207);
        var state = (WeaponEventCountState)status.CountRuntimeState;
        if (!state.LayoutReady || !state.OnField || state.NextTick < 0) return;
        if (BattleManager.Instance == null || (int)BattleManager.Instance.CurrentPhase != status.TriggerPhase) return;
        if (!owner.IsAlive) { SetField(status, owner, state, false, 0); return; }
        long now = WeaponCountClock.Now();
        if (now < state.NextTick) return;
        status.RuntimeValue = Math.Min(4f, status.RuntimeValue + 1f);
        state.NextTick = now + 6L; // 同阶段重入不会再获点；上限之外不储存待发份数。
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}
