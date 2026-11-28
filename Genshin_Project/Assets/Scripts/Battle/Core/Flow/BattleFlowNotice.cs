using System;
using System.Threading;
using System.Threading.Tasks;

public enum BattleFlowNoticeKind { BattleStart, WaveStart, Victory, Defeat, EnemyRefresh }

/// <summary>Presentation snapshot, not an Action/Execution or a gameplay declaration.</summary>
public readonly struct BattleFlowNotice
{
    public readonly BattleFlowNoticeKind Kind;
    public readonly int Turn, Wave, WaveCount;
    public BattleFlowNotice(BattleFlowNoticeKind kind, int turn, int wave = 1, int waveCount = 1)
    {
        if (turn < 1 || wave < 1 || waveCount < wave) throw new ArgumentOutOfRangeException(nameof(wave));
        Kind = kind; Turn = turn; Wave = wave; WaveCount = waveCount;
    }
}

/// <summary>Optional scene-owned presentation wait. A scene without a subscriber continues immediately.</summary>
public sealed class BattleFlowPresentationChannel : IDisposable
{
    public event Func<BattleFlowNotice, CancellationToken, Task> Requested;
    public event Action<Exception> Failed;
    private CancellationTokenSource cancellation;
    public int Generation { get; private set; }
    public void BeginSession() { Cancel(); cancellation = new CancellationTokenSource(); }
    public void Cancel()
    {
        Generation++;
        var old = cancellation; cancellation = null;
        if (old != null) { old.Cancel(); old.Dispose(); }
    }
    public async Task PresentAsync(BattleFlowNotice notice)
    {
        var current = cancellation; if (current == null) return;
        var token = current.Token;
        var handlers = Requested?.GetInvocationList(); if (handlers == null) return;
        foreach (Func<BattleFlowNotice, CancellationToken, Task> handler in handlers)
        {
            token.ThrowIfCancellationRequested();
            try { await (handler(notice, token) ?? Task.CompletedTask); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { } // Disabled optional view releases its gate.
            catch (Exception error) when (!(error is OperationCanceledException)) { Failed?.Invoke(error); }
        }
        token.ThrowIfCancellationRequested();
    }
    public void Dispose() { Cancel(); Requested = null; Failed = null; }
}

public readonly struct BattleFlowBannerFrame
{
    public readonly float Alpha, Offset, Scale;
    public BattleFlowBannerFrame(float alpha, float offset, float scale) { Alpha = alpha; Offset = offset; Scale = scale; }
    public static BattleFlowBannerFrame Sample(float time, float fadeIn, float hold, float fadeOut)
    {
        float Clamp(float n) => Math.Max(0, Math.Min(1, n));
        float enter = fadeIn <= 0 ? 1 : Clamp(time / fadeIn);
        enter = 1 - (1 - enter) * (1 - enter) * (1 - enter);
        float exit = fadeOut <= 0 ? (time >= fadeIn + hold ? 1 : 0) : Clamp((time - fadeIn - hold) / fadeOut);
        return new BattleFlowBannerFrame(Math.Min(enter, 1 - exit), (1 - enter) * 18 - exit * 12, 1 + (1 - enter) * .035f);
    }
}
