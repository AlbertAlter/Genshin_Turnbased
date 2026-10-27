using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>519普通扩散减抗：只消费已经提交的扩散伤害，不从反应原点推测传播目标。</summary>
public static class ViridescentVenererArtifactHooks
{
    public const string StatusID = "ST_Artifact_451904";
    private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo" };
    private sealed class Subscription
    {
        internal IDisposable Value;
        internal readonly HashSet<long> Seen = new HashSet<long>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();

    internal static void Register()
    {
        for (int index = 0; index < Elements.Length; index++)
        {
            string element = Elements[index];
            BattleSpecialRules.RegisterNumberedCondition("4519040" + (index + 1), (argument, context) =>
                Matches(context?.Status, context?.Declaration?.Get<DamageEventData>(), element));
        }
        BattleSpecialRules.RegisterTargets("Hook(45190410)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current;
            var damage = context?.Declaration?.Get<DamageEventData>();
            if (!Matches(context?.Status, damage) || owner != context.Status.Caster)
                return new List<int>();
            BattleEntity target = damage.Subject;
            int position = target.SlotPosition;
            // 保持实体身份；该位置已换成其他敌人时不把旧伤害奖励转交给新占位者。
            return battle != null && battle.GetEntityByPosition(BattleSide.Enemy, position) == target
                ? new List<int> { position } : new List<int>();
        });
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, Start, Stop);
    }

    private static bool Matches(StatusInstance status, DamageEventData damage, string element = null)
    {
        if (status?.StatusID2 != StatusID || damage?.Subject == null
            || !damage.Subject.IsAlive || damage.Subject.Side != BattleSide.Enemy
            || damage.DamageType != "Reaction" || damage.DamageBeforeShield <= 0f
            || (damage.ActualHPDamage <= 0f && damage.ShieldAbsorbed <= 0f)) return false;
        DamageSourceInfo source = damage.Source;
        if (!source.HasSourceEntity || source.SourceEntity != status.Caster
            || source.ReactionType != ReactionType.Swirl) return false;
        // 派生扩散的CountsAsHit/HitLanded可以为false；这里认已发生的伤害，而非普通攻击命中。
        if (element != null) return damage.Element == element;
        foreach (string candidate in Elements) if (damage.Element == candidate) return true;
        return false;
    }

    private static void Start(StatusInstance status, object host)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
            (header, damage) =>
            {
                if (!Matches(status, damage) || !subscription.Seen.Add(header.EventId)) return;
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusActionDict.TryGetValue(StatusID, out var actions)) return;
                BattleDeclaration declaration = BattleEventHub.Current;
                foreach (var action in actions)
                {
                    if (action.ActionType != "OnTrigger" || action.ReactionTriggered != "Swirl"
                        || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                        || (id != "45190401" && id != "45190402" && id != "45190403" && id != "45190404")) continue;
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                    {
                        Caster = owner, Target = damage.Subject, Status = status, StatusHost = owner,
                        Declaration = declaration, HitEffectID = damage.Source.SourceEffectID,
                        DamageElement = damage.Element
                    }, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
                }
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
        // 原ReactionTriggered(Swirl)适配器仍可派发，但其上下文不是DamageResolved，以上条件必为false。
        // 不重复发布伤害/反应；原ApplyStatus行仍负责持续时间、显示、覆盖和装备来源归属。
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }
}
