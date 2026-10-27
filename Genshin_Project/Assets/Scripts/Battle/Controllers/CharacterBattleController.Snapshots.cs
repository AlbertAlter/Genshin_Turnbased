using System;

public partial class CharacterBattleController
{
    // 仅状态公式作用域使用记忆；角色主动技能、目标属性和未启用快照的状态沿原入口。
    private float SourceAttribute(string name, string effect, int skillType, string element)
    {
        var memory = StatusFormulaSnapshot.For(Entity, effect);
        if (memory != null) return memory.Attribute(name, effect, element);
        switch (name)
        {
            case "ATK": case "TotalATK": return Entity.GetFinalATK(effect, GetExecutingSkillID2(skillType), skillType, element);
            case "DEF": case "TotalDEF": return Entity.GetFinalDEF(effect, GetExecutingSkillID2(skillType), skillType, element);
            case "MaxHP": case "TotalHP": return Entity.GetFinalHP(effect, GetExecutingSkillID2(skillType), skillType, element);
            default: throw new NotSupportedException(name);
        }
    }
    private int SourceSkillLevel(string effect, int type)
    {
        int live = GetEffectiveSkillLevel(type);
        var memory = StatusFormulaSnapshot.For(Entity);
        return memory?.HasPlan(effect, "") == true ? memory.SkillLevel(type, live) : live;
    }
    private float SourceValue(string field, float live)
        => StatusFormulaSnapshot.For(Entity)?.Raw(field) ?? live;
    private float SourceBonus(string part, string effect, string element, BattleEntity target, Func<float> live,
        ReactionType? reaction = null)
        => StatusFormulaSnapshot.For(Entity, effect)?.Bonus(part, effect, element, target, reaction) ?? live();
}
