using System;
using System.Collections.Generic;

/// <summary>Pure committed-result annotations. No gameplay hooks, target resolution, damage, or business RNG.</summary>
public static class BattleNativeEffectHooks
{
    public const string Prefix = "UE.";
    private static int owners;
    private static BattleReactionEffectMapping reactions;
    private static BattleStatusEffectMapping statuses;
    private static BattleEffectIDMapping effects;
    public static BattleTableEffectCatalog TableCatalog {get;private set;}
    private static readonly Dictionary<BattleEffectKind, BattleEffectParameters> parameters = new Dictionary<BattleEffectKind, BattleEffectParameters>();
    private static readonly Dictionary<string, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec>> handlers
        = new Dictionary<string, Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec>>(StringComparer.Ordinal);
    public static string Name(BattleEffectKind kind) => Prefix + kind;
    public static BattleEffectParameters Parameters(BattleEffectKind kind) => parameters[kind].Copy();
    public static bool TryResolveStatus(int statusID, out BattleEffectKind kind)
    {
        kind = default;
        return owners > 0 && statuses != null && statuses.TryGet(statusID, out kind);
    }
    public static bool TryStatusRule(int statusID, out BattleEffectKind? kind)
    {
        kind = null;
        return owners > 0 && statuses != null && statuses.TryRule(statusID, out kind);
    }
    public static float Lead(BattleEffectParameters settings)
        => BattleEffectTiming.Contact(settings);
    public static BattleAttackAnimationSpec ApplyEffectMapping(BattlePresentationResult result, BattleAttackAnimationSpec spec)
    {
        if (result == null) return spec;
        return ApplyEffectMapping(BattleEffectIDMapping.NumericID(result.EffectID), result.EffectID, spec,result);
    }
    public static BattleAttackAnimationSpec ApplyEffectMapping(int numericID, string id2, BattleAttackAnimationSpec spec,BattlePresentationResult result=null)
    {
        if(owners>0 && TableCatalog!=null && TableCatalog.Effects.TryGetValue(numericID,out var binding))
        {
            var resource=TableCatalog.Resources[binding.ResourceKey];
            if(!binding.Enabled)return spec?.WithNativeEffect("",0);
            if(result!=null&&!string.IsNullOrEmpty(binding.Trigger)&&binding.Trigger!=result.Data.Kind.ToString())return spec?.WithNativeEffect("",0);
            if(!binding.Matches(result))return spec;
            if(resource.Player!="Legacy")
            {
                if(spec==null)spec=new BattleAttackAnimationSpec(id2,3,1,2,.5f,.5f,0,1);
                return spec.WithNativeEffect(resource.Key,(resource.Contacts.Length>0?resource.Option("SliceLead",.12f):resource.Contact)/resource.Speed);
            }
        }
        if (owners == 0 || effects == null || !effects.TryRule(numericID, out var kind)) return spec;
        if (spec == null)
            spec = new BattleAttackAnimationSpec(id2, 3, 1, 2, .5f, .5f, 0, 1);
        return kind.HasValue ? spec.WithNativeEffect(kind.Value.ToString(), Lead(parameters[kind.Value]))
            : spec.WithNativeEffect("", 0);
    }
    public static bool TryResolveReaction(BattlePresentationResult result, out BattleAttackAnimationSpec spec)
    {
        spec = null;
        if (owners == 0 || !BattleReactionEffectPlan.TryGetReaction(result, out var reaction)) return false;
        bool mapped = reactions.TryGet(reaction, out var kind);
        // Derived damage keeps its real source IDs but never repeats the source skill's cast, timeline or flight.
        spec = new BattleAttackAnimationSpec(result.EffectID, 3, 1, 2,
            mapped ? parameters[kind]["DurationSeconds"] : .25f, .5f, 0, 1,
            nativeEffect: mapped ? kind.ToString() : "");
        return true;
    }
    public static void Retain(Func<BattleEffectKind, BattleEffectParameters> load = null, Func<BattleReactionEffectMapping> reactionLoad = null, Func<BattleStatusEffectMapping> statusLoad = null, Func<BattleEffectIDMapping> effectLoad = null, Func<BattleTableEffectCatalog> tableLoad=null)
    {
        if (owners > 0) { owners++; return; }
        try
        {
            TableCatalog=(tableLoad??BattleTableEffectCatalog.Load)();
            reactions = (reactionLoad ?? BattleReactionEffectMapping.Load)();
            statuses = (statusLoad ?? BattleStatusEffectMapping.Load)();
            effects = (effectLoad ?? BattleEffectIDMapping.Load)();
            foreach (var kind in BattleEffectParameters.Kinds)
            {
                parameters.Add(kind, (load ?? BattleEffectParameters.Load)(kind));
                string name = Name(kind);
                Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec> handler = (result, spec) =>
                    spec.WithNativeEffect(kind.ToString(), Lead(parameters[kind]),
                        kind == BattleEffectKind.AmberArrowRain && result?.Data is DamageEventData damage && damage.Source.SourceStatusApplyOrder != 0 ? (int?)1 : null);
                BattleAttackAnimationHooks.Register(name, handler); handlers.Add(name, handler);
            }
            Func<BattlePresentationResult, BattleAttackAnimationSpec, BattleAttackAnimationSpec> flip = (result, spec) => spec;
            BattleAttackAnimationHooks.Register("Venti.AirborneFlip", flip); handlers.Add("Venti.AirborneFlip", flip);
            owners = 1;
        }
        catch { Unregister(); throw; }
    }
    public static void Release() { if (owners > 0 && --owners == 0) Unregister(); }
    private static void Unregister()
    {
        foreach (var pair in handlers) BattleAttackAnimationHooks.Unregister(pair.Key, pair.Value);
        handlers.Clear(); parameters.Clear(); reactions = null; statuses = null; effects = null; TableCatalog=null;
    }
}
