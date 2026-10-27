using System;

/// <summary>
/// 仅供以下三个已明确“整窗刷新”的圣遗物使用，不是Count的通用生命周期。
/// 每个具体类型明确给出自己的初值、范围及窗口；独立计时的真实状态不经过这里。
/// </summary>
public abstract class RefreshingArtifactCount : CountStatusHandler
{
    private readonly float initial, maximum;
    private readonly int duration;

    protected RefreshingArtifactCount(float initial, float maximum, int duration)
    { this.initial = initial; this.maximum = maximum; this.duration = duration; }

    public override void OnCreated(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        status.RuntimeValue = initial;
        Refresh(status);
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        RequireOwner(status, host);
        status.RuntimeValue = Math.Min(maximum, Math.Max(0f, status.RuntimeValue) + 1f);
        Refresh(status); // 已达上限仍刷新；不重建、不改变来源、ApplyOrder或行动账本。
        return status;
    }

    private void Refresh(StatusInstance status)
    {
        status.StackCount = 1;
        status.IsPermanent = false;
        status.RemainingPhaseCount = duration;
        status.AddInPhase = 1;
        status.TriggerPhase = 0;
        // 到期由既有状态计时入口移除；不把每一点拆成独立计时。
    }

    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; }

    internal static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.CharacterCtrl == null)
            throw new InvalidOperationException("圣遗物计数必须施加在穿戴者本人。");
        return owner;
    }
}

/// <summary>545乐园：初值1、范围0～4、获点刷新整个3回合；单次效果额度仍由原Action管理。</summary>
public sealed class ParadiseCountHandler : RefreshingArtifactCount
{
    public const string StatusID = "ST_Artifact_454505";
    public ParadiseCountHandler() : base(1f, 4f, 3) { }
}

/// <summary>548逐影：初值1、范围0～3、真实HP升降后加点并刷新整个1回合。</summary>
public sealed class MarechausseeCountHandler : RefreshingArtifactCount
{
    public const string StatusID = "ST_Artifact_454803";
    public MarechausseeCountHandler() : base(1f, 3f, 1) { }
}

/// <summary>552谐律：初值1、范围0～3、真实生命之契升降后加点并刷新整个2回合。</summary>
public sealed class WhimsyCountHandler : RefreshingArtifactCount
{
    public const string StatusID = "ST_Artifact_455203";
    public WhimsyCountHandler() : base(1f, 3f, 2) { }
}
