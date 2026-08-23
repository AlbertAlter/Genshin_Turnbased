using System.Collections.Generic;

/// <summary>CharacterBattleController 的统一目标解析适配层。</summary>
public partial class CharacterBattleController
{
    private TargetResolutionResult _lastSkillTargetResult;
    private TargetResolutionResult _lastStatusTargetResult;

    private TargetResolutionResult ResolveSkillTargetResult(SkillEffectData effect)
    {
        var battle = BattleManager.Instance;
        if (effect == null || battle == null)
            return InvalidTargetResult("技能效果或战场为空");

        // 随机模式的全体高亮只是 UI 演出，不是实际强制目标。
        IList<int> forced = effect.TargetConsecutive >= 2 && effect.TargetConsecutive <= 4
            ? null
            : ForcedTargetPositions;

        var result = BattleTargetResolver.Resolve(
            battle,
            Entity,
            Entity,
            effect.TargetType,
            effect.TargetNumber,
            effect.TargetConsecutive,
            effect.TargetOverride,
            null,
            forced,
            _lastSkillTargetResult);

        LogTargetResult(effect.SkillEffectID2, result);
        if (result.IsValid)
        {
            _lastSkillTargetResult = result;
            _lastTargetPositions.Clear();
            _lastTargetPositions.AddRange(result.Positions);
        }
        return result;
    }

    private List<BattleEntity> ResolveTargets(SkillEffectData effect)
    {
        var result = ResolveSkillTargetResult(effect);
        return result.IsValid
            ? BattleTargetResolver.GetEntities(BattleManager.Instance, result)
            : new List<BattleEntity>();
    }

    private List<FieldPosition> ResolveTargetFields(SkillEffectData effect)
    {
        var result = ResolveSkillTargetResult(effect);
        return result.IsValid
            ? BattleTargetResolver.GetFields(BattleManager.Instance, result)
            : new List<FieldPosition>();
    }

    private void BeginStatusTargetSequence()
    {
        _lastStatusTargetResult = null;
    }

    private TargetResolutionResult ResolveStatusTargetResult(
        StatusEffectData effect,
        StatusInstance sourceStatus,
        object host)
    {
        var battle = BattleManager.Instance;
        if (effect == null || battle == null)
            return InvalidTargetResult("状态效果或战场为空");

        BattleEntity source = sourceStatus != null && sourceStatus.Caster != null
            ? sourceStatus.Caster
            : Entity;

        // 状态表用 TargetSelect 代替手动 TargetNumber；无 TargetSelect 时取该 TargetType 的全部范围。
        int targetNumber = string.IsNullOrWhiteSpace(effect.TargetSelect)
            && effect.TargetType != "Self"
            ? -1
            : 0;

        var result = BattleTargetResolver.Resolve(
            battle,
            source,
            host,
            effect.TargetType,
            targetNumber,
            effect.TargetConsecutive,
            effect.TargetOverride,
            effect.TargetSelect,
            null,
            _lastStatusTargetResult,
            selfUsesOrigin: true);

        LogTargetResult(effect.StatusEffectID2, result);
        if (result.IsValid) _lastStatusTargetResult = result;
        return result;
    }

    private List<BattleEntity> ResolveStatusTargets(
        StatusEffectData effect,
        StatusInstance sourceStatus,
        object host)
    {
        var result = ResolveStatusTargetResult(effect, sourceStatus, host);
        return result.IsValid
            ? BattleTargetResolver.GetEntities(BattleManager.Instance, result)
            : new List<BattleEntity>();
    }

    private List<FieldPosition> ResolveStatusTargetFields(
        StatusEffectData effect,
        StatusInstance sourceStatus,
        object host)
    {
        var result = ResolveStatusTargetResult(effect, sourceStatus, host);
        return result.IsValid
            ? BattleTargetResolver.GetFields(BattleManager.Instance, result)
            : new List<FieldPosition>();
    }

    private static TargetResolutionResult InvalidTargetResult(string error)
    {
        return new TargetResolutionResult { Error = error };
    }

    private static void LogTargetResult(string effectID2, TargetResolutionResult result)
    {
        if (result == null) return;
        if (!result.IsValid)
        {
            LogManager.LogWarning(LogCategory.Select, $"[目标解析] {effectID2}: {result.Error}");
            return;
        }

        LogManager.Log(
            LogCategory.Select,
            $"[目标解析] {effectID2}: {result.Side}/{result.Domain} [{string.Join(",", result.Positions)}]");
    }
}
