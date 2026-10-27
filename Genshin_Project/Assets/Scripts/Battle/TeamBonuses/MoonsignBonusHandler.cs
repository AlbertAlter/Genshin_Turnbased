using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class MoonsignBonusHandler
{
    private readonly TeamBonusSystem team;
    private readonly TeamBonusParameters data;
    private readonly HashSet<int> members;
    public int Level { get; }
    public TeamBonusState Bonus { get; private set; }
    public float DamageBonus => Bonus?.IsActive == true ? Bonus.Value : 0f;
    public MoonsignBonusHandler(TeamBonusSystem team, TeamBonusParameters data)
    {
        this.team = team; this.data = data; members = data.CharacterIDs();
        if (!data.Enabled) return;
        int count = team.CountMembers(members);
        Level = count >= data.Int("AscendantCount") ? 2 : count >= data.Int("GleamCount") ? 1 : 0;
    }
    public void OnAction(BattleEventHeader header, ActionEventData action)
    {
        BattleEntity actor = action.Subject;
        if (Level < 2 || !team.Contains(actor) || members.Contains(actor.EntityID)
            || (action.ActionType != "Skill" && action.ActionType != "Burst")) return;
        float value;
        switch (team.ElementOf(actor))
        {
            case "Pyro": case "Electro": case "Cryo":
                float attack = actor.CharacterCtrl != null ? actor.CharacterCtrl.GetFinalATK("", 0, "") : actor.TotalATK;
                value = attack / data.Number("ATKStep") * data.Number("ATKBonus"); break;
            case "Hydro": value = actor.GetFinalHP() / data.Number("HPStep") * data.Number("HPBonus"); break;
            case "Geo": value = actor.GetFinalDEF() / data.Number("DEFStep") * data.Number("DEFBonus"); break;
            case "Anemo": case "Dendro": value = actor.GetFinalEM() / data.Number("EMStep") * data.Number("EMBonus"); break;
            default: return;
        }
        // 单一实例替换：后来者较弱也覆盖，不叠加、不取最大值。
        Bonus = new TeamBonusState("MoonsignLunarBonus", actor, data.Int("Duration"), team.Phase)
            { Value = Mathf.Clamp(value, 0f, data.Number("BonusCap")) };
        LogManager.Log(LogCategory.TeamBonus, "月兆全队增伤覆盖：source=" + actor.EntityID + " bonus=" + Bonus.Value);
    }
    public void EnterPhase(int phase) { Bonus?.Tick(phase); }
}
