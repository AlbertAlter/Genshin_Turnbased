using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class BattleManager
{
    private readonly List<EnemyBattleController> summonedEnemies = new List<EnemyBattleController>();
    private int withdrawnEnemyCount;

    /// <summary>兼容单个敌人调用；角色/敌人技能与状态统一由Withdrawn效果执行。</summary>
    public bool WithdrawEnemy(EnemyBattleController enemy)
        => enemy != null && WithdrawnEffect.Execute(new[] { enemy.Entity }) > 0;

    internal bool ContainsBattleEntity(BattleEntity entity)
        => _allies.Any(c => c?.Entity == entity) || _enemies.Any(c => c?.Entity == entity);

    internal void DetachWithdrawnEntity(BattleEntity entity)
    {
        if (entity.EnemyCtrl != null && _enemies.Remove(entity.EnemyCtrl)) withdrawnEnemyCount++;
        // 角色保留编队记录及出战标记，沿用阵亡后的免费换人/全员失败判断。
        if (entity.Position?.Occupant == entity) entity.Position.Occupant = null;
        entity.Position = null;
    }

    /// <summary>只占用空位或已死亡的位置；普通初始化与场地注册保持同一路径。</summary>
    public EnemyBattleController SummonEnemy(int enemyID, int level, int position, BattleEntity summoner = null)
    {
        var field = Field?.GetSlot(BattleSide.Enemy, position);
        if (field == null || field.Occupant?.IsAlive == true) return null;
        if (DataManager.Instance == null || !DataManager.Instance.EnemyMainDict.ContainsKey(enemyID))
            throw new InvalidOperationException("召唤敌人未配置：" + enemyID);
        var instance = new GameObject("SummonedEnemy_" + enemyID);
        instance.transform.SetParent(transform, false);
        var controller = instance.AddComponent<EnemyBattleController>();
        try { controller.InitEnemy(enemyID, level); }
        catch { if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance); throw; }
        // 保留旧实体供仍存活的场地状态读取来源，但它不再占用队列中的同一位置。
        _enemies.RemoveAll(old => old?.Entity != null && old.Entity.SlotPosition == position && !old.Entity.IsAlive);
        if (field.Occupant != null) field.Occupant.Position = null;
        controller.Entity.Summoner = summoner;
        controller.Entity.SlotPosition = position;
        RegisterEnemy(controller);
        summonedEnemies.Add(controller);
        controller.ApplyInitiateStatuses();
        return controller;
    }

    private void ClearSummonedEnemies()
    {
        withdrawnEnemyCount = 0;
        foreach (var enemy in summonedEnemies)
            if (enemy != null)
            {
                if (Application.isPlaying) Destroy(enemy.gameObject);
                else DestroyImmediate(enemy.gameObject);
            }
        summonedEnemies.Clear();
    }
}
