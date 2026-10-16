using System;
using System.Collections.Generic;

// 场地位置：场地上的一个格子
// 位置可以挂状态（EnemyField 目标），也可以站单位（占位）
public class FieldPosition
{
    public BattleField OwnerField;
    public BattleSide Side;          // 所属阵营
    public int SlotIndex;            // 位置编号（我方从右往左1-4，敌方从右往左1-5）
    public BattleEntity Occupant;    // 当前站着的单位（可空）
    public List<StatusInstance> StatusList = new List<StatusInstance>(); // 挂在该位置上的状态

    public FieldPosition(BattleField ownerField, BattleSide side, int slot)
    {
        OwnerField = ownerField;
        Side = side;
        SlotIndex = slot;
    }

    public bool IsOccupied => Occupant != null && !Occupant.IsDead;

    /// <summary>场地状态也按实例独立计时；MaxStack/WhenMax 与实体采用相同规则。</summary>
    public StatusInstance AddStatus(string id, BattleEntity caster, int duration, int addPhase,
        int triggerPhase, StatusMainData main, WeaponRuntimeContext weaponContext = null, string param2 = null,
        HashSet<string> bindingAncestry = null)
    {
        CountStatusSystem.Validate(main);
        var matches = StatusList.FindAll(status => status != null && status.StatusID2 == id);
        matches.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        if (CountStatusSystem.IsCount(main) && matches.Count > 0)
            throw new NotSupportedException("场地 Count 的再次施加需要该 Count 专用的场地处理入口：" + id);
        if (main != null && main.MaxStack > 0 && matches.Count >= main.MaxStack)
        {
            if (main.WhenMax == "1") return matches[0];
            int removeCount = matches.Count - main.MaxStack + 1;
            for (int i = 0; i < removeCount; i++) RemoveStatusInstance(matches[i]);
        }
        var instance = new StatusInstance
        {
            StatusID2 = id, StackCount = 1, RemainingPhaseCount = duration,
            IsPermanent = duration <= 0, AddInPhase = addPhase, TriggerPhase = triggerPhase,
            MainData = main, WeaponContext = weaponContext, IsActive = true,
            ApplyOrder = ++BattleEntity._applyOrderCounter
        };
        instance.RecordSource(caster);
        CountStatusSystem.OnCreated(instance, this, new CountStatusApplication
        {
            Caster = caster, Duration = duration, AddInPhase = addPhase, TriggerPhase = triggerPhase,
            MainData = main, WeaponContext = weaponContext, Param2 = param2
        });
        StatusList.Add(instance);
        StatusBindingSystem.ApplyBindings(instance, this, bindingAncestry);
        caster?.CharacterCtrl?.OnStatusApplied(instance, this);
        if (!StatusList.Contains(instance)) return instance;
        BattleManager.Instance?.RegisterStatusTick(instance, null, this);
        PreDamageHookSystem.RegisterPreDamageHook(instance, this);
        KillHookSystem.Register(instance, this);
        return instance;
    }

    public bool RemoveStatusInstance(StatusInstance instance)
    {
        if (instance == null || !StatusList.Remove(instance)) return false;
        instance.IsActive = false;
        StatusBindingSystem.RemoveBindings(instance);
        instance.Caster?.CharacterCtrl?.OnStatusChangeControlRemoved(instance);
        BattleManager.Instance?.UnregisterStatusTick(instance);
        PreDamageHookSystem.UnregisterPreDamageHook(instance);
        KillHookSystem.Unregister(instance);
        CountStatusSystem.OnRemoved(instance, this);
        return true;
    }

    public void RemoveStatus(string id)
    {
        foreach (StatusInstance instance in new List<StatusInstance>(StatusList))
            if (instance != null && instance.StatusID2 == id) RemoveStatusInstance(instance);
    }

    public override string ToString()
    {
        return $"{Side}:{SlotIndex}({(IsOccupied ? Occupant.EntityID.ToString() : "空")})";
    }
}
