using System;
using System.Collections.Generic;

/// <summary>装备来源的生命期身份；不把普通角色技能/反应统一改成“来源阵亡即消失”。</summary>
public sealed class EquipmentEffectSource
{
    public BattleEntity Owner { get; }
    private readonly long deathVersion;
    public EquipmentEffectSource(BattleEntity owner)
    { Owner = owner; deathVersion = owner != null ? owner.EquipmentDeathVersion : 0; }
    public bool IsAvailable => Owner != null && Owner.IsAlive && Owner.EquipmentDeathVersion == deathVersion;
}

/// <summary>武器/圣遗物共用：来源阵亡，清理其全部实体/场地状态；不执行自然到期OnEnd。</summary>
public static class EquipmentStatusLifetime
{
    internal static EquipmentEffectSource Resolve(string id, BattleEntity caster, WeaponRuntimeContext weapon,
        EquipmentEffectSource inherited)
    {
        if (inherited != null) return inherited;
        if (weapon?.Equipper != null) return new EquipmentEffectSource(weapon.Equipper);
        if (caster != null && (id?.StartsWith("ST_Artifact_", StringComparison.Ordinal) == true
            || id?.StartsWith("ST_Weapon_", StringComparison.Ordinal) == true)) return new EquipmentEffectSource(caster);
        return null;
    }

    public static bool CanExecute(StatusInstance status)
        => status == null || status.EquipmentSource == null || status.EquipmentSource.IsAvailable;
    public static bool CanExecute(DamageSourceInfo source)
        => source == null || source.EquipmentSource == null || source.EquipmentSource.IsAvailable;

    internal static void OnDeath(BattleEntity owner)
    {
        if (owner == null || owner.IsAlive) return;
        owner.EquipmentDeathVersion++; // 旧请求保留旧身份；即使之后复活，也不能重新生效。
        var candidates = new List<(StatusInstance status, BattleEntity entity, FieldPosition field)>();
        var seen = new HashSet<StatusInstance>();
        void CollectEntity(BattleEntity entity)
        {
            if (entity == null) return;
            foreach (var status in entity.GetStatusList())
                if (status.EquipmentSource?.Owner == owner && seen.Add(status)) candidates.Add((status, entity, null));
        }
        void CollectField(FieldPosition field)
        {
            if (field == null) return;
            foreach (var status in new List<StatusInstance>(field.StatusList))
                if (status.EquipmentSource?.Owner == owner && seen.Add(status)) candidates.Add((status, null, field));
        }
        CollectEntity(owner);
        CollectField(owner.Position);
        var battle = BattleManager.Instance;
        if (battle != null)
        {
            // 队伍挂载不会解除装备者生命期；先按实例收集，统一沿既有施加顺序清理。
            foreach (var team in new[] { battle.AllyTeam, battle.EnemyTeam })
                foreach (var status in team.GetStatusList())
                    if (status.EquipmentSource?.Owner == owner && seen.Add(status)) candidates.Add((status, null, null));
            foreach (var ally in battle.Allies) CollectEntity(ally?.Entity);
            foreach (var enemy in battle.Enemies) CollectEntity(enemy?.Entity);
            if (battle.Field != null)
            {
                foreach (var field in battle.Field.AllySlots) CollectField(field);
                foreach (var field in battle.Field.EnemySlots) CollectField(field);
            }
        }
        candidates.Sort((left, right) => left.status.ApplyOrder.CompareTo(right.status.ApplyOrder));
        foreach (var item in candidates)
            if (item.entity != null) item.entity.RemoveStatusInstance(item.status, "EquipmentOwnerDied");
            else if (item.field != null) item.field.RemoveStatusInstance(item.status, "EquipmentOwnerDied");
            else StatusHostAccess.Remove(item.status.Host, item.status, "EquipmentOwnerDied");
        // 正式移除会清理绑定、护盾、Count、阶段登记和订阅；绝不模拟OnEnd爆炸或转化。
    }
}
