using System.Collections.Generic;

/// <summary>EnemyBattleController 的统一目标解析适配层。</summary>
public partial class EnemyBattleController
{
    private TargetResolutionResult _lastEnemyTargetResult;

    private TargetResolutionResult ResolveEnemyTargetResult(EnemySkillEffectData effect)
    {
        var battle = BattleManager.Instance;
        if (effect == null || battle == null)
            return new TargetResolutionResult { Error = "敌人效果或战场为空" };

        var result = BattleTargetResolver.Resolve(
            battle,
            Entity,
            Entity,
            effect.TargetType,
            effect.TargetNumber,
            effect.TargetConsecutive,
            effect.TargetOverride,
            null,
            null,
            _lastEnemyTargetResult,
            deadOnly: effect.EffectType == "Revive",
            // 是否受嘲讽由目标模式决定，与执行次数、次数处理器及hit数无关。
            // 公共解析器仅对0/1应用嘲讽；2/3/4始终保持各自的随机规则。
            respectTaunt: effect.EffectType == "Damage",
            weightProvider: position => battle.GetEntityByPosition(BattleSide.Ally, position)?.HitWeight ?? 0f);

        if (!result.IsValid)
        {
            LogManager.LogWarning(LogCategory.Enemy, $"[目标解析] {effect.SkillEffectID2}: {result.Error}");
            return result;
        }

        _lastEnemyTargetResult = result;
        LogManager.Log(
            LogCategory.Enemy,
            $"[目标解析] {effect.SkillEffectID2}: {result.Side}/{result.Domain} [{string.Join(",", result.Positions)}]");
        return result;
    }

    private List<BattleEntity> ResolveEnemyTargets(EnemySkillEffectData effect)
    {
        var result = ResolveEnemyTargetResult(effect);
        return result.IsValid
            ? BattleTargetResolver.GetEntities(BattleManager.Instance, result, effect.EffectType == "Revive")
            : new List<BattleEntity>();
    }

    private List<FieldPosition> ResolveEnemyTargetFields(EnemySkillEffectData effect)
    {
        var result = ResolveEnemyTargetResult(effect);
        return result.IsValid
            ? BattleTargetResolver.GetFields(BattleManager.Instance, result)
            : new List<FieldPosition>();
    }
}
