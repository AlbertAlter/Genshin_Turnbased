using UnityEngine;

/// <summary>Formal battle adapter; mouse thaw uses the same input/modal gate as the existing keyboard command.</summary>
public sealed class BattleUiFreezeSource : MonoBehaviour, IBattleUiFreezeSource
{
    public BattleManager battle;
    public BattleInputController input;
    public BattleUiFreezeState GetFreeze()
    {
        if (!isActiveAndEnabled || battle == null) return default;
        var entity = battle.GetActiveAlly()?.Entity;
        ReactionStateSystem.TryGetEntityState(entity, ReactionType.Frozen, out var state);
        return new BattleUiFreezeState { Instance = state, CanInteract = input != null && input.CanThaw };
    }
    public FrozenThawClickResult ClickThaw()
    {
        if (!isActiveAndEnabled || input == null) return FrozenThawClickResult.Ignored;
        input.UIThaw(out var result); return result;
    }
}
