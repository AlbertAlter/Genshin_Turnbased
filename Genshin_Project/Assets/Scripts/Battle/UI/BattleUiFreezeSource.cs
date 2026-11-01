using UnityEngine;

/// <summary>Formal battle adapter; thaw targets its bound entity and uses the shared input/modal gate.</summary>
public sealed class BattleUiFreezeSource : MonoBehaviour, IBattleUiFreezeSource
{
    public BattleManager battle;
    public BattleInputController input;
    public BattleEntity Entity { get; private set; }
    public void Bind(BattleEntity entity, BattleManager manager, BattleInputController controller)
    { Entity = entity; battle = manager; input = controller; }
    public BattleUiFreezeState GetFreeze()
    {
        if (!isActiveAndEnabled || battle == null || Entity == null || !Entity.IsAlive) return default;
        ReactionStateSystem.TryGetEntityState(Entity, ReactionType.Frozen, out var state);
        return new BattleUiFreezeState { Instance = state, CanInteract = state != null &&
            Entity.Side == BattleSide.Ally && input != null && input.CanThawAlly(Entity) };
    }
    public FrozenThawClickResult ClickThaw()
    {
        if (!GetFreeze().CanInteract) return FrozenThawClickResult.Ignored;
        input.UIThaw(Entity, out var result); return result;
    }
}
