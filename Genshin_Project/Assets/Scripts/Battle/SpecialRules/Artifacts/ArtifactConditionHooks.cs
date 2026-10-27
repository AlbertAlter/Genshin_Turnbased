/// <summary>仅作只读筛选；获点、扣费及状态生命周期仍由原效果与专用Count处理。</summary>
public static class ArtifactConditionHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterTargets("Hook(45420201)", (battle, owner) =>
        {
            var hit = ScriptHookContext.Current?.DamageEvent;
            if (owner == null || hit?.Source == null || hit.Source.SourceEntity != owner || !hit.HitLanded
                || hit.Source.SourceKind == ReactionSourceKind.DerivedReaction
                || (hit.DamageType != "Skill" && hit.DamageType != "Burst")
                || hit.Target == null || !hit.Target.IsAlive || hit.Target.Side == owner.Side)
                return new System.Collections.Generic.List<int>();
            return new System.Collections.Generic.List<int> { hit.Target.SlotPosition };
        });
        BattleSpecialRules.RegisterNumberedCondition("453602", (argument, context) =>
            Owner(context) != null && Owner(context).CurrentEnergy >= 15f
                && !Owner(context).HasStatus("ST_Artifact_453603"));
        BattleSpecialRules.RegisterNumberedCondition("454903", (argument, context) =>
            Owner(context)?.CharacterCtrl != null && !Owner(context).CharacterCtrl.IsActive);
        BattleSpecialRules.RegisterNumberedCondition("455104", (argument, context) => NighttimeWhispers(context));
        BattleSpecialRules.RegisterNumberedCondition("455501", (argument, context) =>
            Owner(context)?.CharacterCtrl?.IsActive == true && Owner(context).HasStatus("ST_NightSoulBlessing"));
        BattleSpecialRules.RegisterNumberedCondition("455502", (argument, context) =>
        {
            var change = context?.Declaration?.Get<ResourceEventData>();
            return change != null && change.Subject == Owner(context) && change.Resource == "NightSoul"
                && change.Reason == ResourceChangeReason.Spend && change.WasOnField && change.Removed >= 1d;
        });
        BattleSpecialRules.RegisterNumberedCondition("455702", (argument, context) =>
        {
            var hit = context?.DamageEvent;
            return Owner(context) != null && hit?.Source != null && hit.Source.SourceEntity == Owner(context) && hit.ActualHPDamage > 0f
                && (hit.DamageType == "Normal" || hit.DamageType == "Burst")
                && hit.Source.SourceKind != ReactionSourceKind.DerivedReaction;
        });
        BattleSpecialRules.RegisterNumberedCondition("455703", (argument, context) =>
            EmptyEnergyWithout(context, "ST_Artifact_455705"));
        BattleSpecialRules.RegisterNumberedCondition("455704", (argument, context) =>
            EmptyEnergyWithout(context, "ST_Artifact_455706"));
        BattleSpecialRules.RegisterNumberedCondition("455902", (argument, context) =>
        {
            var hit = context?.DamageEvent;
            return Owner(context) != null && hit?.Source != null && hit.Source.SourceEntity == Owner(context)
                && hit.ActualHPDamage > 0f && IsElement(hit.Element);
        });
        RegisterReaction("451902", ReactionType.Swirl);
        RegisterReaction("452202", ReactionType.Overloaded, ReactionType.ElectroCharged,
            ReactionType.Superconduct, ReactionType.Hyperbloom);
        RegisterReaction("452302", ReactionType.Overloaded, ReactionType.Burning, ReactionType.Burgeon);
        RegisterReaction("456302", ReactionType.Superconduct);
        foreach (string id in new[] { "455804", "455904" })
            BattleSpecialRules.RegisterNumberedCondition(id, (argument, context) => Moonsign(context) == 1);
        foreach (string id in new[] { "455805", "455905" })
            BattleSpecialRules.RegisterNumberedCondition(id, (argument, context) => Moonsign(context) >= 2);

        for (int index = 0; index < 6; index++)
        {
            int tier = index % 3 + 1;
            bool same = index < 3;
            BattleSpecialRules.RegisterNumberedCondition("4543030" + (index + 1), (argument, context) =>
                TeamElementCount(context, same) == tier);
            BattleSpecialRules.RegisterNumberedCondition((454602 + index) + "01", (argument, context) =>
            {
                int sources = NymphSources(Owner(context));
                return tier == 3 ? sources >= 3 : sources == tier;
            });
        }
    }

    private static BattleEntity Owner(ScriptHookContext context) => context?.Status?.Caster ?? context?.Caster;

    private static bool NighttimeWhispers(ScriptHookContext context)
    {
        BattleEntity owner = Owner(context);
        if (owner == null || !owner.IsAlive) return false;
        StatusInstance window = owner.GetStatus("ST_Artifact_455103");
        if (window == null || !window.IsActive || !EquipmentStatusLifetime.CanExecute(window)
            || (!window.IsPermanent && window.RemainingPhaseCount <= 0)) return false;
        foreach (Shield shield in owner.Shields)
            if (shield != null && shield.Kind == ShieldKind.Crystallize && shield.Value > 0f && shield.Duration > 0f)
                return true;
        var team = BattleManager.Instance?.TeamBonuses;
        // 用户约定：敌方场地存在任意有效月笼即可，不限创建者、格子或距离；双岩不是前置条件。
        return team != null && team.Contains(owner) && team.Resonance.HasEnemyFieldMoonCage;
    }

    private static void RegisterReaction(string id, params ReactionType[] types)
    {
        BattleSpecialRules.RegisterNumberedCondition(id, (argument, context) =>
        {
            // 这是“当前计算的反应伤害”，不是响应链中曾发生过的反应声明。
            if (context == null || !context.DamageReaction.HasValue) return false;
            foreach (ReactionType type in types) if (context.DamageReaction.Value == type) return true;
            return false;
        });
    }
    private static bool EmptyEnergyWithout(ScriptHookContext context, string excluded)
        => Owner(context) != null && Owner(context).CurrentEnergy == 0f && !Owner(context).HasStatus(excluded);

    private static int Moonsign(ScriptHookContext context)
    {
        var team = BattleManager.Instance?.TeamBonuses;
        return team != null && !team.IsDisposed && team.Contains(Owner(context)) ? team.Moonsign.Level : 0;
    }

    private static int TeamElementCount(ScriptHookContext context, bool same)
    {
        var team = BattleManager.Instance?.TeamBonuses;
        BattleEntity owner = Owner(context);
        if (team == null || team.IsDisposed || !team.Contains(owner)) return -1;
        string element = team.ElementOf(owner);
        if (!IsElement(element)) return -1;
        int count = 0;
        foreach (BattleEntity member in team.Members)
        {
            if (member == owner) continue;
            string other = team.ElementOf(member);
            if (!IsElement(other)) return -1; // 缺少固有元素不能冒充异元素。
            if ((other == element) == same) count++;
        }
        return count; // 固定编队，包含后台；按成员数，不按元素种类数。
    }

    private static int NymphSources(BattleEntity owner)
    {
        if (owner == null) return 0;
        int count = 0;
        for (int id = 454608; id <= 454611; id++)
            if (owner.HasStatus("ST_Artifact_" + id)) count++;
        return count; // 同来源即使有多份实例也只算一类，不合并四种状态的独立期限。
    }

    internal static bool IsElement(string element) => element == "Pyro" || element == "Hydro"
        || element == "Electro" || element == "Cryo" || element == "Anemo" || element == "Geo" || element == "Dendro";
}
