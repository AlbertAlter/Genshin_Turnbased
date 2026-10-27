using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>班尼特只记录本次选择、真实命中和具体领域绑定；倍率、回能、持续时间与执行顺序仍在配表。</summary>
public static class BennettScriptHooks
{
    public const string Field = "ST_Bennett_Field", EndPhase = "ST_Bennett_FieldEnemyEnd";
    public const string Encouraged = "ST_Bennett_Encouraged", Follow = "ST_Bennett_C4Follow";
    private static readonly AsyncLocal<Cast> Current = new AsyncLocal<Cast>();
    private static readonly ConditionalWeakTable<StatusInstance, List<IDisposable>> Listeners
        = new ConditionalWeakTable<StatusInstance, List<IDisposable>>();

    internal static void Register()
    {
        // 爆发只手选鼓舞对象；全体敌伤与自身领域均自动执行，不再要求第二种阵营的确认。
        BattleSpecialRules.RegisterAutomaticSkillTargets(effect => effect?.SkillEffectID2 == "SE_Burst_BennettDamage");
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusLifecycle(Encouraged, CaptureRecipient, null);
        BattleSpecialRules.RegisterStatusLifecycle(Field, StartField, null);
        BattleSpecialRules.RegisterStatusLifecycle(Follow, StartFollow, Stop);
        BattleSpecialRules.RegisterCondition("Bennett_Encouraged", (a, c) => Empty(a) && ValidEncouragement(c));
        BattleSpecialRules.RegisterCondition("Bennett_LowHP", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1020 && LowHP(c.Caster));
        BattleSpecialRules.RegisterCondition("Bennett_EnchantWeapon", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1020 && c.StatusHost is BattleEntity target
            && (target.WeaponType == 0 || target.WeaponType == 1 || target.WeaponType == 3));
        BattleSpecialRules.RegisterCondition("Bennett_FieldAlive", (a, c) => Empty(a) && ActiveField(c?.Caster));
        BattleSpecialRules.RegisterCondition("Bennett_SelfAirborne", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1020
            && !(Talent(c.Caster, 2) && ActiveField(c.Caster)));
        BattleSpecialRules.RegisterCondition("Bennett_C4Action", (a, c) => Empty(a)
            && c?.Status?.StatusID2 == Follow && c.Caster?.IsAlive == true
            && c.Declaration?.Kind == BattleEventKind.ActionCompleted
            && c.Declaration.Get<ActionEventData>()?.ActionType == "Normal"
            && c.Declaration.Data.Subject == c.Caster);
        BattleSpecialRules.RegisterTargets("Bennett_AirborneTargets()", AirborneTargets);
        BattleSpecialRules.RegisterTargets("Bennett_InjuredAllies()", (battle, owner) =>
        {
            var positions = new List<int>();
            if (battle != null && ActiveField(owner))
                foreach (var ally in battle.Allies)
                    if (ally?.Entity != null && ally.Entity.Side == owner.Side && LowHP(ally.Entity))
                        positions.Add(ally.Entity.SlotPosition);
            positions.Sort(); return positions;
        });
        BattleSpecialRules.RegisterTargets("Bennett_C4Target()", (battle, owner) =>
        {
            var c = ScriptHookContext.Current;
            return c?.Caster == owner && c.Status?.StatusID2 == Follow && ValidEnemy(battle, owner, c.Target)
                ? new List<int> { c.Target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterBuffScalar("Bennett_BaseAttack()", ResolveAttackBonus);
    }

    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Talent(BattleEntity owner, int index)
        => ScriptHookEvaluator.Evaluate("Check(1020_T" + index + ")", owner);
    private static bool ActiveField(BattleEntity owner) => owner?.EntityID == 1020 && owner.IsAlive && owner.HasStatus(Field);
    private static bool LowHP(BattleEntity target)
        => target?.IsAlive == true && target.CurrentHP < .7f * target.GetFinalHP();
    private static bool ValidEnemy(BattleManager battle, BattleEntity owner, BattleEntity target)
        => battle != null && owner?.IsAlive == true && target?.IsAlive == true && target.Side != owner.Side
            && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target;

    private static bool ValidEncouragement(ScriptHookContext c)
    {
        if (c?.Status?.StatusID2 != Encouraged || !ActiveField(c.Caster)
            || !(c.StatusHost is BattleEntity recipient) || recipient != c.Recipient || !recipient.IsAlive) return false;
        var parent = c.Caster.GetStatus(Field);
        return parent.BoundStatuses.Contains(c.Status)
            && (c.Caster.ConstellationLevel >= 1 || recipient.CurrentHP > .7f * recipient.GetFinalHP());
    }

    private static float ResolveAttackBonus(StatusEffectData effect, StatusInstance status)
    {
        var owner = status?.Caster;
        if (effect.StatusEffectID != 410200301 || status?.StatusID2 != Encouraged
            || owner?.EntityID != 1020 || owner.CharacterCtrl == null || !string.IsNullOrWhiteSpace(effect.Param1)
            || !owner.CharacterCtrl.TryResolveStatusLevelScalar(effect, out float ratio))
            throw new InvalidOperationException("鼓舞加攻必须读取本来源班尼特的爆发等级标量与基础攻击。");
        return owner.GetTotalBaseATK() * (ratio + (owner.ConstellationLevel >= 1 ? .2f : 0f));
    }

    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string SkillID, ActionType;
        internal readonly StatusInstance FollowAtStart;
        internal readonly HashSet<BattleEntity> SkillHits = new HashSet<BattleEntity>();
        internal readonly List<StatusInstance> Recipients = new List<StatusInstance>();
        internal BattleEntity MainTarget;
        private readonly Cast previous = Current.Value;
        private readonly IDisposable listener;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; SkillID = skill?.SkillID2; ActionType = skill?.ActionType; Current.Value = this;
            if (owner?.EntityID != 1020) return;
            FollowAtStart = owner.GetStatus(Follow);
            listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
            {
                var source = hit.Source;
                if (!hit.HitLanded || source?.CountsAsHit != true || source.SourceEntity != owner
                    || source.SourceKind != ReactionSourceKind.CharacterSkill || source.SourceSkillID != SkillID
                    || hit.Subject == null || hit.Subject.Side == owner.Side) return;
                if ((SkillID == "SK_Skill_Bennett2" && source.SourceEffectID == "SE_Skill_Bennett2Damage")
                    || (SkillID == "SK_Skill_Bennett3" && source.SourceEffectID == "SE_Skill_Bennett3Damage")) SkillHits.Add(hit.Subject);
                if (ActionType == "Normal" && header.TargetIndex == 1 && MainTarget == null) MainTarget = hit.Subject;
            }, 0, () => ReferenceEquals(Current.Value, this));
        }
        public void Dispose()
        {
            try
            {
                listener?.Dispose();
                foreach (var marker in Recipients)
                    if (marker.IsActive) StatusHostAccess.Remove(marker.Host, marker, "UnboundBennettRecipient");
            }
            finally { Current.Value = previous; }
        }
    }

    private static List<int> AirborneTargets(BattleManager battle, BattleEntity owner)
    {
        var positions = new List<int>(); var frame = Current.Value; var data = DataManager.Instance;
        if (frame?.Owner != owner || owner?.EntityID != 1020 || data == null) return positions;
        foreach (var target in frame.SkillHits)
            if (ValidEnemy(battle, owner, target) && data.EnemyMainDict.TryGetValue(target.EntityID, out var enemy)
                && string.Equals(enemy.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase)) positions.Add(target.SlotPosition);
        positions.Sort(); return positions;
    }

    private static void CaptureRecipient(StatusInstance status, object host)
    {
        var frame = Current.Value;
        if (frame?.Owner?.EntityID == 1020 && frame.SkillID == "SK_Burst_Bennett"
            && status.Caster == frame.Owner && host is BattleEntity recipient && recipient.Side == frame.Owner.Side)
            frame.Recipients.Add(status);
    }

    private static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1020 || status.Caster != owner)
            throw new InvalidOperationException("班尼特的领域及四命追加窗口必须挂在本人。");
        return owner;
    }
    private static void StartField(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        var frame = Current.Value;
        if (frame?.Owner != status.Caster || frame.SkillID != "SK_Burst_Bennett") return;
        foreach (var marker in frame.Recipients)
            if (marker.IsActive && StatusHostAccess.Contains(marker.Host, marker)) status.BoundStatuses.Add(marker);
        frame.Recipients.Clear();
    }
    private static void StartFollow(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host); var subscriptions = new List<IDisposable>(); Listeners.Add(status, subscriptions);
        subscriptions.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, fact) =>
        {
            var frame = Current.Value;
            if (fact.Subject != owner || fact.ActionType != "Normal" || frame?.Owner != owner
                || frame.SkillID != fact.SkillId || frame.FollowAtStart != status || !owner.IsAlive) return;
            var data = DataManager.Instance;
            if (data == null || !data.StatusActionDict.TryGetValue(Follow, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "Bennett_C4Action()")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                    { Caster = owner, Status = status, StatusHost = host, Target = frame.MainTarget, Declaration = BattleEventHub.Current },
                    () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status));
        }, status.ApplyOrder, () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status)));
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Listeners.TryGetValue(status, out var subscriptions)) foreach (var listener in subscriptions) listener.Dispose();
        Listeners.Remove(status);
    }
}
