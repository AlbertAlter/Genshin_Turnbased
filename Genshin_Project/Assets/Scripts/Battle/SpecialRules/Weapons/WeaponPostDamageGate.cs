using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>晶核、苍翠、昭心、天空之卷：整效果（含ExecuteCount）一次概率候选，不缓存概率结果。</summary>
internal static class WeaponPostDamageGate
{
    private static readonly AsyncLocal<Invocation> Current = new AsyncLocal<Invocation>();
    private sealed class Invocation : IDisposable
    {
        internal readonly Invocation Previous = Current.Value;
        internal readonly BattleEntity Owner;
        internal readonly long Execution;
        internal readonly HashSet<StatusInstance> Submitted = new HashSet<StatusInstance>();
        internal Invocation(BattleEntity owner, long execution)
        { Owner = owner; Execution = execution; Current.Value = this; }
        public void Dispose() { Current.Value = Previous; }
    }
    internal static void Register()
        => BattleSpecialRules.RegisterEffectInvocationScope((owner, effect, execution) => new Invocation(owner, execution));
    internal static bool Allow(StatusInstance status, StatusActionData action, DamageResolvedEvent fact)
    {
        bool snow = (status?.StatusID2 == "ST_Weapon_4306201" || status?.StatusID2 == "ST_Weapon_4311301" || status?.StatusID2 == "ST_Weapon_4313801")
            && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string hook) && "ST_Weapon_" + hook == status.StatusID2;
        bool hunt = status?.StatusID2 == "ST_Weapon_4308601"
            && ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out string post) && post == "4308601";
        bool orb = status?.StatusID2 == "ST_Weapon_4313701"
            && ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out string eye) && eye == "4313701";
        bool atlas = status?.StatusID2 == "ST_Weapon_4320602"
            && ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out string cloud) && cloud == "4320602";
        var frame = Current.Value;
        if ((!snow && !hunt && !orb && !atlas) || frame == null) return true;
        return fact?.Source?.SourceEntity == frame.Owner
            && fact.Declaration?.Header.EffectExecutionId == frame.Execution && frame.Submitted.Add(status);
    }
}
