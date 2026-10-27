using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ElementalResonanceHandler : IDisposable
{
    private readonly TeamBonusSystem team;
    private readonly TeamBonusParameters data;
    private readonly HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<BattleEntity, float> hpApplied = new Dictionary<BattleEntity, float>();
    private readonly Dictionary<BattleEntity, float> hpPercentApplied = new Dictionary<BattleEntity, float>();
    private readonly Dictionary<BattleEntity, float> emApplied = new Dictionary<BattleEntity, float>();
    private readonly Dictionary<BattleEntity, TeamBonusState> geoDebuffs = new Dictionary<BattleEntity, TeamBonusState>();
    private readonly HashSet<BattleEntity> moonCageCreators = new HashSet<BattleEntity>();
    private TeamBonusState dendroA, dendroB;
    private int energyTurn = -1, energyCount;
    public ElementalResonanceHandler(TeamBonusSystem team, TeamBonusParameters data)
    {
        this.team = team; this.data = data;
        if (!data.Enabled) return;
        var types = new HashSet<string>();
        foreach (string element in new[] { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" })
        {
            if (team.CountElement(element) > 0) types.Add(element);
            if (team.CountElement(element) >= data.Int("PairCount")) active.Add(element);
        }
        if (types.Count >= data.Int("ProtectTypes")) active.Add("Protect");
        foreach (BattleEntity member in team.Members)
        {
            if (!Has("Hydro")) continue;
            float basis = member.CharacterCtrl != null ? member.CharacterCtrl.GetHPBeforePercentBonuses() : member.TotalHP;
            float delta = basis * data.Number("HydroHP");
            hpApplied.Add(member, delta);
            hpPercentApplied.Add(member, data.Number("HydroHP"));
            member.BakedHPBonus += data.Number("HydroHP");
            member.TotalHP += delta;
            // 当前HP在全队构筑完成后统一初始化，不在共鸣构造期间逐项同步。
        }
        UpdateEM();
    }
    public bool Has(string name) => active.Contains(name);
    public float ATKBonus(BattleEntity actor) => team.Contains(actor) && Has("Pyro") ? data.Number("PyroATK") : 0f;
    public int APDelta(BattleEntity actor, string operation) => team.Contains(actor) && Has("Anemo")
        && (operation == "Normal" || operation == "Heavy" || operation == "SwitchIn") ? data.Int("AnemoAP") : 0;
    public float CritBonus(BattleEntity actor, BattleEntity target) => team.Contains(actor) && Has("Cryo")
        && target != null && (ElementQuery.HasAura(target, "Cryo") || FrozenReactionHandler.IsFrozen(target))
            ? data.Number("CryoCrit") : 0f;
    public float ShieldBonus(BattleEntity actor) => team.Contains(actor) && Has("Geo") ? data.Number("GeoShield") : 0f;
    public bool GeoProtected(BattleEntity actor)
    {
        if (!team.Contains(actor) || !Has("Geo")) return false;
        if (moonCageCreators.Contains(actor)) return true;
        foreach (Shield shield in actor.Shields) if (shield != null && shield.Value > 0f) return true;
        foreach (StatusInstance status in actor.GetEffectiveStatusList())
            if (status.IsActive && status.MainData?.StatusType == "Shield") return true;
        return false;
    }
    // 敌方场地月笼的存在事实独立于双岩是否激活，551等条件可直接读取。
    public bool HasEnemyFieldMoonCage => !team.IsDisposed && moonCageCreators.Count > 0;

    // 仅供真实月笼生命周期处理器登记敌方场地上的月笼，不以“触发了月结晶”替代生成成功。
    // 按真实创建者汇总；最后一个该创建者月笼消失后才取消。不要因其离场或阵亡提前清除反应场地。
    public void SetHasMoonCage(BattleEntity creator, bool exists)
    { if (creator == null || team.IsDisposed) return; if (exists) moonCageCreators.Add(creator); else moonCageCreators.Remove(creator); }
    public float DamageBonus(BattleEntity actor) => GeoProtected(actor) ? data.Number("GeoDMG") : 0f;
    public float ResistanceBonus(BattleEntity target, string element)
    {
        if (team.Contains(target)) return Has("Protect") ? data.Number("ProtectRes") : 0f;
        return target != null && target.Side == BattleSide.Enemy && element == "Geo"
            && geoDebuffs.TryGetValue(target, out TeamBonusState state) && state.IsActive ? -state.Value : 0f;
    }
    public void OnDamage(BattleEventHeader header, DamageEventData hit)
    {
        BattleEntity source = hit.Source.SourceEntity;
        if (hit.Subject == null || hit.Subject.Side != BattleSide.Enemy || hit.DamageBeforeShield <= 0f || !GeoProtected(source)) return;
        // 不按普通OnHit限制：有来源的反应伤害也属于造成伤害。
        var state = new TeamBonusState("GeoResonance", source, data.Int("GeoDuration"), team.Phase)
            { Value = data.Number("GeoResDown") };
        geoDebuffs[hit.Subject] = state;
    }
    public void OnReaction(BattleEventHeader header, ReactionEventData reaction)
    {
        if (!team.Contains(reaction.Source)) return;
        if (Has("Electro") && Matches(data.Text("ElectroReactions"), reaction))
        {
            if (energyTurn != team.Turn) { energyTurn = team.Turn; energyCount = 0; }
            if (energyCount < data.Int("ElectroPerTurn"))
            {
                energyCount++; // 先消耗额度，防止充能声明触发的嵌套反应重入。
                foreach (BattleEntity member in team.Members)
                    if (member.IsAlive) CharacterBattleController.ApplyEnergyGain(member, data.Number("ElectroEnergy"), "Based", "Electro");
                LogManager.Log(LogCategory.TeamBonus, "双雷充能：全队Based雷元素能量，回合=" + team.Turn);
            }
        }
        if (!Has("Dendro")) return;
        if (Matches(data.Text("DendroGroupA"), reaction))
            dendroA = new TeamBonusState("DendroA", reaction.Source, data.Int("DendroDuration"), team.Phase);
        if (Matches(data.Text("DendroGroupB"), reaction))
            dendroB = new TeamBonusState("DendroB", reaction.Source, data.Int("DendroDuration"), team.Phase);
        UpdateEM();
    }
    private static bool Matches(string filter, ReactionEventData value) => ReactionFilterMatcher.Matches(filter,
        value.Reaction, value.Reaction.ToString(), value.Elements);
    public void EnterPhase(int phase)
    {
        dendroA?.Tick(phase); dendroB?.Tick(phase);
        foreach (TeamBonusState state in geoDebuffs.Values) state.Tick(phase);
        foreach (BattleEntity target in new List<BattleEntity>(geoDebuffs.Keys))
            if (target == null || !geoDebuffs[target].IsActive) geoDebuffs.Remove(target);
        UpdateEM();
    }
    private void UpdateEM()
    {
        float value = Has("Dendro") ? data.Number("DendroEM")
            + (dendroA?.IsActive == true ? data.Number("DendroEMA") : 0f)
            + (dendroB?.IsActive == true ? data.Number("DendroEMB") : 0f) : 0f;
        foreach (BattleEntity member in team.Members)
        {
            if (member == null) continue;
            emApplied.TryGetValue(member, out float old);
            member.TotalEM += value - old;
            emApplied[member] = value;
        }
    }
    public void Dispose()
    {
        foreach (var pair in emApplied) if (pair.Key != null) pair.Key.TotalEM -= pair.Value;
        foreach (var pair in hpApplied)
            if (pair.Key != null)
            {
                pair.Key.TotalHP = Mathf.Max(0f, pair.Key.TotalHP - pair.Value);
                pair.Key.BakedHPBonus -= hpPercentApplied[pair.Key];
                pair.Key.CurrentHP = Mathf.Min(pair.Key.CurrentHP, pair.Key.GetFinalHP());
            }
        emApplied.Clear(); hpApplied.Clear(); hpPercentApplied.Clear(); geoDebuffs.Clear(); moonCageCreators.Clear(); active.Clear();
    }
}
