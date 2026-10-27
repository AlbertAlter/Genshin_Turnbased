using System;
using System.Collections.Generic;

public enum BattlePresentationKind { Effect, Hit, Heal, Status, Reaction }

/// <summary>一次表现步骤。只描述来源、目标和已提交结果；播放器不得修改战斗规则。</summary>
public sealed class BattlePresentationRequest
{
    private static long _nextID;
    private readonly List<DamageResolvedEvent> _damage = new List<DamageResolvedEvent>();
    private readonly List<HealResolvedEvent> _healing = new List<HealResolvedEvent>();
    private readonly List<ReactionOccurrence> _reactions = new List<ReactionOccurrence>();
    public long ID { get; } = ++_nextID;
    public BattlePresentationKind Kind { get; }
    public BattleEntity Source { get; }
    public string EffectID { get; }
    public string StatusID { get; }
    public ReactionType Reaction { get; }
    public int SegmentIndex { get; }
    public IReadOnlyList<BattleEntity> Targets { get; }
    public IReadOnlyList<DamageResolvedEvent> Damage => _damage.AsReadOnly();
    public IReadOnlyList<HealResolvedEvent> Healing => _healing.AsReadOnly();
    public IReadOnlyList<ReactionOccurrence> Reactions => _reactions.AsReadOnly();

    public BattlePresentationRequest(BattlePresentationKind kind, BattleEntity source,
        string effectID, int segmentIndex = 0, IEnumerable<BattleEntity> targets = null,
        string statusID = null, ReactionType reaction = ReactionType.None)
    {
        Kind = kind;
        Source = source;
        EffectID = effectID ?? string.Empty;
        StatusID = statusID ?? string.Empty;
        Reaction = reaction;
        SegmentIndex = segmentIndex;
        Targets = new List<BattleEntity>(targets ?? Array.Empty<BattleEntity>()).AsReadOnly();
    }

    internal void Record(DamageResolvedEvent result) => _damage.Add(result.Snapshot());
    internal void Record(HealResolvedEvent result) => _healing.Add(result);
    internal void Record(ReactionOccurrence result)
        => _reactions.Add(new ScriptHookContext { ReactionEvent = result }.Snapshot().ReactionEvent);
}

/// <summary>
/// 回调必须在 Unity 主线程调用。Begin 完成前摇，ShowResult 展示已提交结果。
/// 无对应资源时立即回调；Cancel 只清理表现，禁止借此执行规则或推进新的战斗。
/// 整段已提交结果的时间调度由可选扩展 IBattleResultPresentationPlayer 接管，不靠逐hit阻塞实现。
/// </summary>
public interface IBattlePresentationPlayer
{
    void Begin(BattlePresentationRequest request, Action reachedResolutionPoint);
    void ShowResult(BattlePresentationRequest request, Action readyToContinue);
    void Cancel(BattlePresentationRequest request);
}

/// <summary>默认无动画，不计时、不等待帧，也不依赖场景组件。</summary>
public sealed class ImmediateBattlePresentationPlayer : IBattlePresentationPlayer
{
    public void Begin(BattlePresentationRequest request, Action reachedResolutionPoint) => reachedResolutionPoint();
    public void ShowResult(BattlePresentationRequest request, Action readyToContinue) => readyToContinue();
    public void Cancel(BattlePresentationRequest request) { }
}
