using System;

/// <summary>522如雷：每次合法重施加1点；获点的Action/1由常驻反应监听管理。</summary>
public sealed class ThunderingFuryCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_452207";
    public const string PayoutEffectID = "STE_Artifact_45220601";
    public const int Threshold = 4;

    public override void OnCreated(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        status.RuntimeValue = 1f;
        status.StackCount = 1;
        status.IsPermanent = true;
        status.RemainingPhaseCount = 0;
        status.TriggerPhase = 0;
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        BattleEntity owner = RequireOwner(status, host);
        if (application.Caster != owner)
            throw new InvalidOperationException("如雷计数只能由穿戴者自身反应获得。");
        if (float.IsNaN(status.RuntimeValue) || status.RuntimeValue < 0f
            || status.RuntimeValue >= Threshold || status.RuntimeValue != (int)status.RuntimeValue)
            throw new InvalidOperationException("如雷计数必须为0到3的整数。");
        int next = (int)status.RuntimeValue + 1;
        if (next < Threshold)
        {
            status.RuntimeValue = next;
            return status;
        }

        // 读取 Cooldown 配表的有符号增减量，通过公共冷却入口执行，不另改字典。
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusEffectDict.TryGetValue(PayoutEffectID, out var payout)
            || payout.EffectType != "Cooldown" || payout.TargetType != "Self"
            || !ResetCooldownTarget.IsValid(payout.Param1)
            || !CooldownParameters.TryParseAmount(payout.Param2, out int rounds))
            throw new InvalidOperationException("如雷缺少合法的Cooldown配置：" + PayoutEffectID);
        status.RuntimeValue = next - Threshold; // 先扣4，再提交冷却变化，子响应不能重复消费。
        if (!owner.CharacterCtrl.ChangeSkillCooldown(payout.Param1, rounds))
            throw new InvalidOperationException("如雷无法解析穿戴者当前战技：" + payout.Param1);
        return status; // 归零保留；不重建、不清除反应监听或其行动额度。
    }

    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;

    private static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.CharacterCtrl == null)
            throw new InvalidOperationException("如雷Count必须施加在穿戴者本人。");
        return owner;
    }
}
