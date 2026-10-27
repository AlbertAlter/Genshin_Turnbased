using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>芭芭拉命中去重计数与歌声之环条件；六命复活边界由BarbaraRevivalHooks管理。</summary>
public static class BarbaraScriptHooks
{
    public const string HeavyEffectID = "SE_Heavy_Barbara";
    public const string HeavySkillID = "SK_Heavy_Barbara";
    private static readonly AsyncLocal<SkillScope> Current = new AsyncLocal<SkillScope>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(BeginSkill);
        BattleSpecialRules.RegisterCount("1014_C4", GetC4ExecuteCount);
        // 到期时仍是同一份环：只读取本人当前能量；续时由表里的AddTurn执行并同步绑定子状态。
        BattleSpecialRules.RegisterCondition("Barbara_T2", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Status?.StatusID2 == "ST_Barbara_Ring"
            && context.Caster?.EntityID == 1014 && context.Caster.IsAlive
            && context.Status.Caster == context.Caster && context.Status.IsActive
            && !context.Status.IsPermanent && context.Status.RemainingPhaseCount <= 0
            && context.StatusHost == context.Caster && context.Caster.HasStatusInstance(context.Status)
            && context.Caster.CurrentEnergy < context.Caster.MaxEnergy);
        BarbaraRevivalHooks.Register();
    }

    private sealed class SkillScope : IDisposable
    {
        internal readonly BattleEntity Caster;
        internal readonly HashSet<BattleEntity> HitTargets = new HashSet<BattleEntity>();
        private readonly SkillScope previous;
        private readonly IDisposable subscription;
        private bool disposed;

        internal SkillScope(BattleEntity caster)
        {
            Caster = caster;
            previous = Current.Value;
            Current.Value = this;
            try
            {
                if (caster != null && caster.EntityID == 1014 && caster.ConstellationLevel >= 4)
                    subscription = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                        RememberHit, 0, () => !disposed && ReferenceEquals(Current.Value, this));
            }
            catch { Current.Value = previous; throw; }
        }

        private void RememberHit(BattleEventHeader header, DamageEventData damage)
        {
            DamageSourceInfo source = damage.Source;
            BattleEntity target = damage.Subject;
            if (!damage.HitLanded || target == null || target.Side == Caster.Side
                || source.SourceEntity != Caster || source.SourceKind != ReactionSourceKind.CharacterSkill
                || source.SourceSkillID != HeavySkillID || source.SourceEffectID != HeavyEffectID
                || damage.DamageType != "Heavy") return;
            // 命中事实已经提交：护盾全吸收、命中后死亡仍计入，不重新查询存活/场上位置。
            HitTargets.Add(target);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription?.Dispose();
            Current.Value = previous;
        }
    }

    // 所有子技能都建立隔离作用域，非芭芭拉技能也不能借用外层记录。
    public static IDisposable BeginSkill(BattleEntity caster) => new SkillScope(caster);

    public static int GetC4ExecuteCount(BattleEntity caster)
        => caster != null && caster.EntityID == 1014 && caster.ConstellationLevel >= 4
            && Current.Value?.Caster == caster ? Current.Value.HitTargets.Count : 0;
}
