using System;

/// <summary>3049 弥坚骨：本状态自己负责 18 次、逐 hit/目标消耗、刷新与归零移除。</summary>
public sealed class SturdyBoneCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Weapon_4304903";

    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = host as BattleEntity;
        ValidateOwner(owner, status.WeaponContext);
        status.StackCount = 1;
        status.RuntimeValue = 18f;
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        ValidateOwner(host, application.WeaponContext);
        // 不继承旧剩余次数/时间，不叠加；重新上场创建一份新的 18 次状态。
        host.RemoveStatus(status.StatusID2, -1);
        return application.ApplyTo(host);
    }

    private static void ValidateOwner(BattleEntity owner, WeaponRuntimeContext context)
    {
        if (owner == null || context == null || context.WeaponID != 3049 || context.Equipper != owner
            || context.ResolvedParams == null || !context.ResolvedParams.ContainsKey(1))
            throw new InvalidOperationException("弥坚骨 Count 必须施加给本武器的装备者，并携带有效的武器精炼参数1。");
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        status.RuntimeValue = 0f;
    }

    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
    {
        var owner = host as BattleEntity;
        if (owner == null || owner != context.Attacker || !owner.IsAlive || status.RuntimeValue < 1f
            || !string.Equals(context.DamageType, "Normal", StringComparison.OrdinalIgnoreCase)
            || context.SourceKind == ReactionSourceKind.DerivedReaction
            || context.Target.Side == owner.Side || !ReferenceEquals(owner.GetStatus(StatusID), status)) return null;

        float attack = owner.CharacterCtrl != null
            ? owner.CharacterCtrl.GetFinalATK(context.EffectID, context.SkillType, context.Element)
            : (owner.TotalATK + owner.WeaponATK) * (1f + owner.WeaponATKBonus);
        float bonus = status.WeaponContext.ResolveParam(1) * attack;
        if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0f)
            throw new InvalidOperationException("弥坚骨基础伤害加成不是有效的非负数。");

        // 仅在真实伤害准备中预占；同一 hit 预计算多个目标时也不能超用。
        // 数值读取与日志不经过此入口；未实际命中、异常或取消时由作用域归还。
        status.RuntimeValue -= 1f;
        return new CountDamageContribution(bonus,
            result =>
            {
                if (ReferenceEquals(owner.GetStatus(StatusID), status) && status.RuntimeValue <= 0f)
                    owner.RemoveStatus(StatusID, -1);
            },
            () =>
            {
                if (ReferenceEquals(owner.GetStatus(StatusID), status)) status.RuntimeValue += 1f;
            });
    }
}
