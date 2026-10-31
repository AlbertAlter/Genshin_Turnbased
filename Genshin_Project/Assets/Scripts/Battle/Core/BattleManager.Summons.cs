using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleManager
{
    private readonly List<EnemyBattleController> summonedEnemies = new List<EnemyBattleController>();

    /// <summary>只占用空位或已死亡的位置；普通初始化与场地注册保持同一路径。</summary>
    public EnemyBattleController SummonEnemy(int enemyID, int level, int position)
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
        controller.Entity.SlotPosition = position;
        RegisterEnemy(controller);
        summonedEnemies.Add(controller);
        controller.ApplyInitiateStatuses();
        return controller;
    }

    private void ClearSummonedEnemies()
    {
        foreach (var enemy in summonedEnemies)
            if (enemy != null)
            {
                if (Application.isPlaying) Destroy(enemy.gameObject);
                else DestroyImmediate(enemy.gameObject);
            }
        summonedEnemies.Clear();
    }
}
