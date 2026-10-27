using System;
using System.Collections.Generic;

public partial class CharacterBattleController
{
    private readonly Dictionary<string, StatusInstance> _artifactInitiateStatuses =
        new Dictionary<string, StatusInstance>();

    /// <summary>
    /// 完整编队注册后的战斗构筑阶段调用，参数为每个套装的实际穿戴件数；与武器登记先后不影响开战收尾。
    /// 两件套与四件套的初始状态直接挂在穿戴者本体上，Caster 同样为穿戴者，
    /// 因此 Self、来源属性和状态事件均沿用穿戴者的上下文。
    /// 再次调用只增删发生变化的初始状态，不重复叠加已有套装效果。
    /// </summary>
    public bool InitializeArtifactSetStatuses(IReadOnlyDictionary<int, int> equippedSetCounts)
    {
        if (equippedSetCounts == null) throw new ArgumentNullException(nameof(equippedSetCounts));
        if (Entity == null || !Entity.IsAlive) return false;
        DataManager manager = DataManager.Instance;
        if (manager == null) return false;
        BattleAttributeInitialization.BeginBuild(Entity);

        var desired = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var setIDs = new List<int>(equippedSetCounts.Keys);
        setIDs.Sort();
        foreach (int setID in setIDs)
        {
            foreach (string key in manager.GetArtifactInitiateStatuses(setID, equippedSetCounts[setID]))
            {
                if (!manager.StatusMainDict.ContainsKey(key))
                {
                    LogManager.LogWarning(LogCategory.Build, $"圣遗物初始化状态不存在: {key}");
                    return false;
                }
                if (seen.Add(key)) desired.Add(key);
            }
        }

        foreach (string key in new List<string>(_artifactInitiateStatuses.Keys))
        {
            if (seen.Contains(key)) continue;
            Entity.RemoveStatusInstance(_artifactInitiateStatuses[key]);
            _artifactInitiateStatuses.Remove(key);
            _initialStatuses.RemoveAll(entry => entry.Kind == InitialStatusKind.Artifact && entry.ID == key);
        }

        foreach (string key in desired)
        {
            if (_artifactInitiateStatuses.TryGetValue(key, out StatusInstance previous)
                && Entity.HasStatusInstance(previous)) continue;

            // 初始状态永久存在于穿戴者本人，效果产生的子状态持续时间仍由配表决定。
            _artifactInitiateStatuses[key] = Entity.AddStatus(key, Entity, 0, 2, 0,
                manager.StatusMainDict[key], null, null, new EquipmentEffectSource(Entity));
            RecordInitializationStatus(_artifactInitiateStatuses[key], 0, 2, InitialStatusKind.Artifact);
        }
        return true;
    }
}
