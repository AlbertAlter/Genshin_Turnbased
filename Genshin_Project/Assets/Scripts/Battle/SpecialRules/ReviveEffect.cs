using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>独立复活操作，不借用Heal；补回初始化状态，不恢复死亡前的临时效果或旧请求。</summary>
public static class ReviveEffect
{
    public static float ParseAmount(string value, out bool usesTargetHP)
    {
        string raw = Regex.Replace(value ?? string.Empty, @"\s+", "");
        const string suffix = "*Target.TotalHP";
        usesTargetHP = raw.EndsWith(suffix, StringComparison.Ordinal);
        if (usesTargetHP) raw = raw.Substring(0, raw.Length - suffix.Length);
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            || float.IsNaN(number) || float.IsInfinity(number) || number <= 0f)
            throw new FormatException("Revive的Param1必须为有限正数生命值或正系数*Target.TotalHP。");
        return number;
    }

    public static void Execute(string formula, BattleEntity source, IReadOnlyList<BattleEntity> targets,
        StatusInstance sourceStatus = null)
    {
        float value = ParseAmount(formula, out bool proportional);
        using var resolution = BattleEventHub.BeginResolution();
        using var declarations = BattleResolutionContext.BeginCollection();
        foreach (BattleEntity target in targets)
        {
            if (target == null || target.IsAlive || !EquipmentStatusLifetime.CanExecute(sourceStatus)) continue;
            var battle = BattleManager.Instance;
            if (battle != null && (battle.IsBattleOver || battle.GetEntityByPosition(target.Side, target.SlotPosition) != target)) continue;
            float maximum = target.GetFinalHP();
            float amount = proportional ? value * maximum : value;
            if (maximum <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) continue;
            float before = target.CurrentHP;
            target.CurrentHP = Mathf.Clamp(amount, 0f, maximum);
            if (!target.IsAlive) continue;
            // 先恢复存活资格，让装备来源/绑定沿正常入口初始化；整个过程仍在同一声明收集边界内。
            // 此处只补初始化状态，不重新读取构筑或累加命座；统一等级查询继续使用已加载的自身等级。
            target.CharacterCtrl?.RestoreInitializationStatuses();
            if (!target.IsAlive) continue;
            // 百分比复活按初始化后的最终上限计算；不能沿用死亡时丢失HP增益后的旧上限。
            maximum = target.GetFinalHP();
            target.CurrentHP = Mathf.Clamp(proportional ? value * maximum : value, 0f, maximum);
            if (!target.IsAlive) continue;
            BattleDeathResolution.ReportRevival(sourceStatus, target);
            BattleEventHub.Publish(new HPChangedEventData(target, before, target.CurrentHP, "Revive"));
            LogManager.Log(LogCategory.Effect, $"复活 {target.EntityID}：HP {before:F1}→{target.CurrentHP:F1}，来源 {source?.EntityID ?? 0}");
        }
    }
}
