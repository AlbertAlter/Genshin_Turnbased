/// <summary>角色固有类型与实时保护资格；纯查询，不增删状态、不抽随机。</summary>
public static class ArtifactEligibilityHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("451802", (argument, context) =>
        {
            var owner = context?.Status?.Caster ?? context?.Caster;
            return owner != null && (owner.WeaponType == 0 || owner.WeaponType == 1 || owner.WeaponType == 3);
        });
        BattleSpecialRules.RegisterNumberedCondition("452002", (argument, context) =>
        {
            var owner = context?.Status?.Caster ?? context?.Caster;
            return owner != null && (owner.WeaponType == 2 || owner.WeaponType == 4);
        });
        BattleSpecialRules.RegisterNumberedCondition("453202", (argument, context) =>
        {
            var owner = context?.Status?.Caster ?? context?.Caster;
            return owner != null && owner.HasShieldProtection();
        });
    }
}
