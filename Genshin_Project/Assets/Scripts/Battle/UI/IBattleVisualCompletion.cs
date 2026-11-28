/// <summary>Finite presentation tails that must finish before battle rewards. Persistent status visuals do not hold this gate.</summary>
public interface IBattleVisualCompletion
{
    bool HasPendingVisuals { get; }
}
