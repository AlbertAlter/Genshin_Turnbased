/// <summary>
/// 主动行为的唯一判定入口。
/// Skill.ActionType 非空时，该 Skill 才属于“主动行为”或“角色行动”；为空时不属于。
/// 其他系统不得根据按钮来源、调用来源或 SkillType 自行推断。
/// </summary>
public static class ActiveActionQuery
{
    public static bool IsActiveAction(BattleEntity actor, SkillMainData skill)
    {
        return skill != null && !string.IsNullOrWhiteSpace(skill.ActionType);
    }
}
