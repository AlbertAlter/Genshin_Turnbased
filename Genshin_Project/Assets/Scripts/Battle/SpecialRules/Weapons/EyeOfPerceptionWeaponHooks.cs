using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>3137昭心：首击加4跳；逐跳重新选存活目标，允许重复，复用原独立伤害结算。</summary>
internal static class EyeOfPerceptionWeaponHooks
{
    internal static void Register()
        => BattleSpecialRules.RegisterStatusDamage("Hook(431370101)", Execute);

    private static bool CanHit(BattleEntity target, BattleEntity owner, BattleManager battle)
        => target != null && target.IsAlive && target.Side != owner.Side && target.SlotPosition > 0
            && ReferenceEquals(battle?.Field?.GetSlot(target.Side, target.SlotPosition)?.Occupant, target);

    private static async Task Execute(StatusDamageExecution context)
    {
        var effect = context.Effect; var status = context.Status;
        var owner = WeaponCountLifetime.RequireOwner(status, context.Host, 3137);
        if (status.StatusID2 != "ST_Weapon_4313701" || effect.StatusEffectID != 431370101
            || effect.Param1 != "Index(1)*TotalATK,0" || effect.Element != "Physical"
            || !string.IsNullOrEmpty(effect.DamageType) || !string.IsNullOrWhiteSpace(effect.Param2))
            throw new InvalidOperationException("昭心弹射必须保留本武器来源、独立物理零附着与空DamageType。");
        var battle = BattleManager.Instance;
        if (context.Targets.Count != 1 || !CanHit(context.Targets[0], owner, battle)) return;
        BattleEntity target = context.Targets[0]; // 首击由既有PostSelfDamage只选一次。
        for (int jump = 0; jump < 5; jump++)
        {
            BattleExecution.ThrowIfCancelled();
            if (battle == null || battle.IsBattleOver || !EquipmentStatusLifetime.CanExecute(status)) return;
            if (jump > 0)
            {
                var candidates = new List<BattleEntity>();
                foreach (var field in battle.Field.EnemySlots)
                    if (CanHit(field.Occupant, owner, battle) && !candidates.Contains(field.Occupant)) candidates.Add(field.Occupant);
                if (candidates.Count == 0) return;
                target = candidates[candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count)];
            }
            if (!CanHit(target, owner, battle)) return;
            context.Targets = new List<BattleEntity> { target };
            LogManager.Log(LogCategory.StatusDamage, $"昭心跳#{jump + 1} target={target.EntityID} slot={target.SlotPosition}");
            // 同一EffectExecutionID，新逻辑hit；Play/RunInline先完成该段及原响应再返回。
            await context.ExecuteDefault(effect);
            await BattleExecution.Flush();
        }
    }
}
