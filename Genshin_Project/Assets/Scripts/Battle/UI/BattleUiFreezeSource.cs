using UnityEngine;

/// <summary>Formal battle adapter; thaw targets its bound entity and uses the shared input/modal gate.</summary>
public sealed class BattleUiFreezeSource : MonoBehaviour, IBattleUiFreezeSource, IBattleUiControlReleaseSource, IBattleUiStunSource
{
    public BattleManager battle;
    public BattleInputController input;
    public BattleEntity Entity { get; private set; }
    public void Bind(BattleEntity entity, BattleManager manager, BattleInputController controller)
    { Entity = entity; battle = manager; input = controller; }
    public static bool IsStunned(BattleEntity entity) => entity?.IsAlive == true &&
        entity.HasStatus(PublicControlStates.EnemyStun);
    public bool Stunned => isActiveAndEnabled && battle != null && IsStunned(Entity);
    public BattleUiFreezeState GetFreeze()
    {
        if (!isActiveAndEnabled || battle == null || Entity == null || !Entity.IsAlive) return default;
        ReactionStateSystem.TryGetEntityState(Entity, ReactionType.Frozen, out var state);
        return new BattleUiFreezeState { Instance = state, CanInteract = state != null &&
            Entity.Side == BattleSide.Ally && input != null && input.CanThawAlly(Entity) };
    }
    public AllyControlState GetControl() => isActiveAndEnabled && battle != null
        ? AllyControlRelease.GetOldest(Entity) : default;
    public bool CanClickControl => isActiveAndEnabled && input != null && input.CanClickControlAlly(Entity);
    public AllyControlClickResult ClickControl()
    {
        if (!CanClickControl) return AllyControlClickResult.Ignored;
        input.UIClickControlAlly(Entity, out var result);
        return result;
    }

    public FrozenThawClickResult ClickThaw()
    {
        if (!GetFreeze().CanInteract) return FrozenThawClickResult.Ignored;
        input.UIThaw(Entity, out var result); return result;
    }
}
