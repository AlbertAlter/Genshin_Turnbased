using System.Collections;

/// <summary>Owns and drives the single root battle-flow routine.</summary>
public interface IBattleFlowScheduler
{
    bool IsRunning { get; }
    void Start(IEnumerator routine);
    void Stop();
}
