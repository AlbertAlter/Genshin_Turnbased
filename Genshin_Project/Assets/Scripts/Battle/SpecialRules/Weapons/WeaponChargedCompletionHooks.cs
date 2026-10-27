using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3220以真实整次Heavy技能执行为边界，只记录其直接Damage命中；结束后定向执行原清除行。</summary>
internal static class WeaponChargedCompletionHooks
{
    private const string Listener = "ST_Weapon_4322002";
    private sealed class Invocation : IDisposable
    {
        internal readonly Invocation Previous = Current.Value;
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly StatusInstance Status;
        internal readonly Dictionary<long, string> Effects = new Dictionary<long, string>();
        internal BattleDeclaration Landed;
        internal bool Closed;
        internal Invocation(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.ActionType == "Heavy" ? skill.SkillID2 : null;
            Status = owner?.GetStatus(Listener); Current.Value = this;
        }
        internal bool Valid => Status?.WeaponContext?.WeaponID == 3220 && Status.WeaponContext.Equipper == Owner
            && Status.Caster == Owner && Status.IsActive && Owner.HasStatusInstance(Status) && EquipmentStatusLifetime.CanExecute(Status);
        public void Dispose()
        {
            if (Closed) return;
            Closed = true;
            try
            {
                if (Skill == null || Landed == null || !Valid) return;
                Completed.Add(Landed, this);
                WeaponHitWindowHooks.Request(Status, Owner, "432200203", Landed, () => Closed && Valid);
            }
            finally { Current.Value = Previous; }
        }
    }
    private static readonly AsyncLocal<Invocation> Current = new AsyncLocal<Invocation>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Invocation> Completed = new ConditionalWeakTable<BattleDeclaration, Invocation>();
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Invocation(owner, skill));
        // 独立状态/武器响应及嵌套技能有自己的帧，不污染外层重击。
        BattleSpecialRules.RegisterStatusActionScope(status => new Invocation(status?.Caster, null));
        BattleSpecialRules.RegisterDamageEffectScope((owner, effect, execution) =>
        {
            var frame = Current.Value;
            if (frame != null && !frame.Closed && frame.Owner == owner && frame.Skill != null) frame.Effects[execution] = effect;
            return null;
        }, (_, __) => { });
        BattleSpecialRules.RegisterDeclarationObserver(declaration =>
        {
            var frame = Current.Value; var damage = declaration.Get<DamageEventData>();
            if (frame == null || frame.Closed || frame.Skill == null || !frame.Valid || damage == null || !damage.HitLanded
                || damage.DamageType != "Heavy" || damage.Source.SourceEntity != frame.Owner
                || damage.Source.SourceKind != ReactionSourceKind.CharacterSkill || damage.Source.SourceSkillID != frame.Skill
                || !frame.Effects.TryGetValue(declaration.Header.EffectExecutionId, out string effect)
                || effect != damage.Source.SourceEffectID) return;
            frame.Landed = declaration; // 只索引真实身份与命中；不在Record、单hit或单Damage结束处清Buff。
        });
        BattleSpecialRules.RegisterNumberedCondition("432200203", (_, c) => c?.Declaration != null
            && Completed.TryGetValue(c.Declaration, out var frame) && frame.Status == c.Status && frame.Closed && frame.Valid);
    }
}
