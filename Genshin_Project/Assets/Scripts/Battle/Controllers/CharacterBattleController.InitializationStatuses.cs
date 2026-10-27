using System.Collections.Generic;

public partial class CharacterBattleController
{
    private enum InitialStatusKind { Character, Weapon, Artifact }

    private sealed class InitialStatus
    {
        internal string ID;
        internal StatusMainData Main;
        internal int Duration, Phase;
        internal InitialStatusKind Kind;
        internal WeaponRuntimeContext Weapon;
        internal StatusInstance Instance;
    }

    // 只记录真正施加过的初始化输入；不记录技能临时状态，也不重放构筑中的技能修改/加级。
    private readonly List<InitialStatus> _initialStatuses = new List<InitialStatus>();

    internal void RecordCharacterInitializationStatus(StatusInstance status, int duration)
        => RecordInitializationStatus(status, duration, 2, InitialStatusKind.Character);

    private void RecordInitializationStatus(StatusInstance status, int duration, int phase, InitialStatusKind kind)
    {
        if (status == null) return;
        InitialStatus entry = _initialStatuses.Find(item => item.Kind == kind && item.ID == status.StatusID2);
        if (entry == null)
        {
            entry = new InitialStatus();
            _initialStatuses.Add(entry);
        }
        entry.ID = status.StatusID2;
        entry.Main = status.MainData;
        entry.Duration = duration;
        entry.Phase = phase;
        entry.Kind = kind;
        entry.Weapon = status.WeaponContext;
        entry.Instance = status;
    }

    /// <summary>
    /// 仅供正式复活入口调用：按原登记顺序补回缺失的初始化状态，使用新的ApplyOrder。
    /// Count走新建初值，绑定和OnApply走正常入口；已有实例不重施，不恢复临时状态。
    /// 不重建面板，不重置技能等级、能量、冷却、次数或每场预算，也不把当前生命填满。
    /// </summary>
    internal void RestoreInitializationStatuses()
    {
        if (Entity == null || !Entity.IsAlive) return;
        using var declarations = BattleResolutionContext.BeginCollection();
        foreach (InitialStatus entry in _initialStatuses.ToArray())
        {
            if (!Entity.IsAlive) break;
            if (Entity.HasStatusInstance(entry.Instance)) continue;
            entry.Instance = Entity.AddStatus(entry.ID, Entity, entry.Duration, entry.Phase, 0, entry.Main,
                entry.Weapon, null, entry.Kind == InitialStatusKind.Character ? null : new EquipmentEffectSource(Entity));
            // 保持圣遗物的去重缓存指向本次新实例，避免之后查询构筑时再次重复挂载。
            if (entry.Kind == InitialStatusKind.Artifact) _artifactInitiateStatuses[entry.ID] = entry.Instance;
        }
    }
}
