/// <summary>月绽放保留普通草原核及其伤害，只在真实生成核心后获得1点共享草露。</summary>
public static class LunarBloomReactionHandler
{
    internal static void GrantDew(ReactionOccurrence occurrence)
    {
        var core = occurrence?.CreatedBloomCore;
        if (occurrence?.Type != ReactionType.LunarBloom || core == null || core.LunarDewGranted) return;
        // 核心属于创建时的战斗，旧结果不得在重置后给新一场草露。
        var team = core.CreationTeam;
        if (team == null || team.IsDisposed || !team.Contains(occurrence.SourceEntity)) return;
        // 必须继承本次真实反应的因果身份，不能把稍后一次反应当作创建来源。
        if (occurrence.Declaration == null)
            throw new System.InvalidOperationException("月绽放获草露缺少本次反应声明身份。");
        core.LunarDewGranted = true;
        using (BattleEventHub.Enter(occurrence.Declaration))
            team.VerdantDew.Gain(1, occurrence.SourceEntity);
    }
}
