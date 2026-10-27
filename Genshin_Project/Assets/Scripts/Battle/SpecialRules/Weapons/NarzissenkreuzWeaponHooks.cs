using System;
using System.Threading.Tasks;

/// <summary>3047：只读取本人芒/荒公共标记；冲击沿用正常Damage、固有元素和0元素量。</summary>
internal static class NarzissenkreuzWeaponHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("4304701", (_, context) =>
        {
            var status = context?.Status;
            var owner = status?.WeaponContext?.Equipper;
            return status?.StatusID2 == "ST_Weapon_4304701" && status.WeaponContext.WeaponID == 3047
                && owner != null && status.Caster == owner && !ArkheStatuses.HasAny(owner);
        });
        BattleSpecialRules.RegisterStatusDamage("Hook(430470101)", Execute);
    }

    private static Task Execute(StatusDamageExecution context)
    {
        var source = context.Effect;
        var status = context.Status;
        var owner = WeaponCountLifetime.RequireOwner(status, context.Host, 3047);
        if (status.StatusID2 != "ST_Weapon_4304701" || source.StatusEffectID != 430470101)
            throw new InvalidOperationException("水仙冲击必须使用本武器监听状态及对应效果。");
        // 从正式角色身份配置读取固有元素；不读取当前普攻附魔或触发命中的Element。
        string element = DataManager.Instance?.GetCharacterOverview(owner.EntityID)?.Element;
        if (!WeaponHooks.IsElemental(element))
            throw new InvalidOperationException("水仙冲击的装备者缺少有效固有元素。");
        var prepared = new StatusEffectData
        {
            StatusEffectID = source.StatusEffectID, StatusEffectID2 = source.StatusEffectID2,
            EffectIndex = source.EffectIndex, EffectType = source.EffectType,
            Element = element, DamageType = source.DamageType,
            Duration = source.Duration, AddInPhase = source.AddInPhase, TriggerPhase = source.TriggerPhase,
            Param1 = source.Param1, Param2 = source.Param2, Param3 = source.Param3,
            TargetType = source.TargetType, TargetSelect = source.TargetSelect,
            TargetConsecutive = source.TargetConsecutive, TargetOverride = source.TargetOverride,
            ScriptHook = source.ScriptHook, ExecuteCount = source.ExecuteCount,
            ExecuteCountHandler = source.ExecuteCountHandler, AuthoredFields = source.AuthoredFields
        };
        // Param1仍为Index(1)*TotalATK,0；不改共享表行、不关闭反应管线、不伪造Normal/Heavy标签。
        return context.ExecuteDefault(prepared);
    }
}
