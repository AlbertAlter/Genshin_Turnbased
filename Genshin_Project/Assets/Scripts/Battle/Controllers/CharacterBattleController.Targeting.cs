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
        // Self 是固定指向施放者，不能被本次技能序列保留的手选位置覆盖。
        IList<int> forced = effect.TargetType == "Self"
            || (effect.TargetConsecutive >= 2 && effect.TargetConsecutive <= 4)
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

        // Pre/Post Damage 钩子的 TargetSelect 表示“从钩子返回的目标池中选几个”，
        // 不是状态表常规的相对位置表达式。Pre 读取主动行为预解析池，Post 读取完整 Damage 结算结果池。
        if (ScriptHookParser.TryParse(effect.TargetOverride, out string overrideHook, out _)
            && (overrideHook == "PreAlliesDamage"
                || overrideHook == "PreSelfDamage"
                || overrideHook == "PostAlliesDamage"
                || overrideHook == "PostSelfDamage"))
        {
            int hookTargetNumber = -1;
            if (!string.IsNullOrWhiteSpace(effect.TargetSelect)
                && (!int.TryParse(effect.TargetSelect.Trim(), out hookTargetNumber) || hookTargetNumber <= 0))
            {
                return InvalidTargetResult($"{overrideHook} 的 TargetSelect 必须是正整数: {effect.TargetSelect}");
            }

            var hookResult = BattleTargetResolver.Resolve(
                battle,
                source,
                host,
                effect.TargetType,
                hookTargetNumber,
                effect.TargetConsecutive,
                effect.TargetOverride,
                null,
                null,
                _lastStatusTargetResult,
                selfUsesOrigin: true);

            LogTargetResult(effect.StatusEffectID2, hookResult);
            if (hookResult.IsValid) _lastStatusTargetResult = hookResult;
            return hookResult;
        }

        // 武器事件效果的 TargetSelect=1 表示“本次事件命中的单个目标”。
        if (sourceStatus?.WeaponContext != null
            && host is BattleEntity eventTarget
            && effect.TargetType == "Enemy"
            && effect.TargetSelect?.Trim() == "1")
        {
            return new TargetResolutionResult
            {
                Side = eventTarget.Side,
                Domain = BattleTargetDomain.Unit,
                Positions = new List<int> { eventTarget.SlotPosition }
            };
        }

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
