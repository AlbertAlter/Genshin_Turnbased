using System;

/// <summary>3062/3113/3138：完整命中效果的一次概率；晶核各目标在本hit开始前独立读取冰影响。3113仅单体。</summary>
internal static class SnowTombedWeaponHooks
{
    internal static bool Handles(StatusEffectData effect)
        => effect != null && (effect.StatusEffectID == 430620101 || effect.StatusEffectID == 431130101 || effect.StatusEffectID == 431380101)
            && effect.EffectType == "Damage" && effect.DamageHook == "Hook(" + effect.StatusEffectID + ")";

    internal static void Register()
    {
        foreach (int weapon in new[] { 3062, 3113, 3138 }) RegisterCrystal(weapon);
    }

    private static void RegisterCrystal(int weapon)
    {
        string statusID = ((40000 + weapon) * 100 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string effectID = statusID + "01";
        BattleSpecialRules.RegisterNumberedCondition(statusID, (_, context) =>
        {
            var owner = context?.Status?.WeaponContext?.Equipper;
            if (context?.Status?.StatusID2 != "ST_Weapon_" + statusID || context.Status.WeaponContext?.WeaponID != weapon
                || owner == null || context.Status.Caster != owner
                || context.DamageEvent?.Source?.SourceEntity != owner || !context.DamageEvent.HitLanded
                || (context.DamageEvent.DamageType != "Normal" && context.DamageEvent.DamageType != "Heavy")) return false;
            bool hasCenter = false;
            if (context.ActionTargetPositions != null)
                foreach (int position in context.ActionTargetPositions)
                {
                    var target = BattleManager.Instance?.GetEntityByPosition(BattleSide.Enemy, position);
                    if (target != null && target.IsAlive && target.Side != owner.Side) { hasCenter = true; break; }
                }
            if (!hasCenter) return false;
            float chance = WeaponHooks.Param(context.Status, 1);
            float roll = BattleRandom.NextFloat01();
            bool success = roll < chance;
            LogManager.Log(LogCategory.ScriptHook, $"Hook({statusID}) rate={chance:R} roll={roll:R} success={success}");
            return success;
        });
        // 明确登记这一条Damage；仍委托原伤害入口，不重发hit、反应或伤害声明。
        BattleSpecialRules.RegisterStatusDamage("Hook(" + effectID + ")", execution => execution.ExecuteDefault(execution.Effect));
    }

    internal static float ResolveBase(StatusEffectData effect, StatusInstance status, BattleEntity target, int skillType, float fallback)
    {
        if (!Handles(effect)) return fallback;
        var owner = status?.WeaponContext?.Equipper;
        int weapon = effect.StatusEffectID / 10000 - 40000;
        if (owner == null || status.Caster != owner || status.WeaponContext.WeaponID != weapon || effect.Element != "Cryo"
            || effect.Param1 != "Index(2)*TotalATK,0" || (weapon == 3113 && !string.IsNullOrWhiteSpace(effect.Param2)))
            throw new InvalidOperationException("晶核必须使用本武器来源、Cryo及零附着倍率公式；龙脊长枪不能配置Splash。");
        bool cryo = WeaponHooks.HasCryoInfluence(status, target);
        return owner.GetFinalATK(effect.StatusEffectID2, string.Empty, skillType, effect.Element)
            * WeaponHooks.Param(status, cryo ? 3 : 2);
    }
}
