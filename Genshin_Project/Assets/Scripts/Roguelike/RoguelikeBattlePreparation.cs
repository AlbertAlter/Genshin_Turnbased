using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>传给战斗运行器的完整构筑，不把突破阶段压成 bool，也不把圣遗物只转换成套装件数。</summary>
[Serializable]
public sealed class RoguelikeBattleAllyBuild
{
    public int Slot;
    public RoguelikeCharacterProgressData Character;
    public RoguelikeWeaponInstanceData Weapon;
    public List<RoguelikeArtifactInstanceData> Artifacts = new List<RoguelikeArtifactInstanceData>();

    /// <summary>给角色构筑器的独立输入；整数突破阶段与武器参数保留，圣遗物完整实例仍在Artifacts。</summary>
    public CharacterProfile CreateCharacterProfile() => CharacterProfile.FromProgress(Character, Weapon?.Weapon);
}

[Serializable]
public sealed class RoguelikeBattleSetup
{
    public string RunId;
    public string StepId;
    public string BattleId;
    public string NodeId;
    public string ContentId;
    public int ChapterId;
    public int BackgroundId;
    public int RunSeed;
    public int RandomStep;
    public List<RoguelikeBattleAllyBuild> Allies = new List<RoguelikeBattleAllyBuild>();
    public List<StageEnemySetup> Enemies = new List<StageEnemySetup>();
}

/// <summary>只解析已经确认的战斗内容 ID，不在此重新抽关卡，不使用测试关卡作为兜底。</summary>
public interface IRoguelikeEncounterCatalog
{
    RoguelikeSaveResult<StageSetupData> Resolve(string contentId);
}

public interface IRoguelikeEnemyCatalog
{
    bool ContainsEnemy(int enemyId);
}

public sealed class DataManagerRoguelikeEnemyCatalog : IRoguelikeEnemyCatalog
{
    private readonly DataManager data;
    public DataManagerRoguelikeEnemyCatalog(DataManager data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }
    public bool ContainsEnemy(int enemyId)
    {
        return data.IsLoaded && data.EnemyMainDict.TryGetValue(enemyId, out var enemy) && enemy != null;
    }
}

/// <summary>
/// 无 UI 的内容注册入口，供未来关卡加载器注入；自身不创建任何关卡。
/// 复用 StageSetupData 的敌方及背景字段，忽略其中测试用 Allies 和 InitialEnergy 设置。
/// 注册/读取都复制，不修改配置原对象。
/// </summary>
public sealed class RoguelikeEncounterRegistry : IRoguelikeEncounterCatalog
{
    private readonly Dictionary<string, StageSetupData> encounters =
        new Dictionary<string, StageSetupData>(StringComparer.Ordinal);

    public void Register(string contentId, StageSetupData stage)
    {
        if (string.IsNullOrWhiteSpace(contentId)) throw new ArgumentException("内容 ID 不能为空。", nameof(contentId));
        if (stage == null) throw new ArgumentNullException(nameof(stage));
        if (encounters.ContainsKey(contentId)) throw new InvalidOperationException("战斗内容重复注册：" + contentId);
        encounters.Add(contentId, RoguelikeSaveJson.DeepClone(stage));
    }

    public RoguelikeSaveResult<StageSetupData> Resolve(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId) || !encounters.TryGetValue(contentId, out var stage))
            return RoguelikeSaveResult<StageSetupData>.Failure(RoguelikeSaveErrorCode.NotFound,
                "尚未注册对应战斗内容：" + contentId);
        return RoguelikeSaveResult<StageSetupData>.Success(RoguelikeSaveJson.DeepClone(stage));
    }
}

/// <summary>只组装数据，不创建实体、不读取场景、也不修改队伍配置或存档。</summary>
public sealed class RoguelikeBattlePreparation
{
    private readonly IRoguelikeEncounterCatalog encounters;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly IRoguelikeEnemyCatalog enemies;

    public RoguelikeBattlePreparation(IRoguelikeEncounterCatalog encounters, IRoguelikeEquipmentCatalog equipment,
        IRoguelikeEnemyCatalog enemies)
    {
        this.encounters = encounters ?? throw new ArgumentNullException(nameof(encounters));
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
    }

    public RoguelikeSaveResult<RoguelikeBattleSetup> Prepare(RoguelikeFlowContext context)
    {
        if (context == null) return Invalid("战斗上下文不能为空。");
        var validation = new RoguelikeSaveValidator().Validate(context.Snapshot);
        if (!validation.Succeeded) return Failure(validation);
        var snapshot = RoguelikeSaveJson.DeepClone(context.Snapshot);
        var flow = snapshot.Flow;
        if (flow == null || flow.Step != RoguelikeFlowStep.Battle ||
            context.ResumePoint != RoguelikeResumePoint.BeforeBattle ||
            string.IsNullOrWhiteSpace(context.RunId) || string.IsNullOrWhiteSpace(context.BattleId))
            return Invalid("只能从已保存的战前恢复点准备战斗。");
        if (snapshot.PartyCharacterIds.Count < 1 || snapshot.PartyCharacterIds.Count > 4)
            return Invalid("进入战斗前必须确认 1～4 名出战角色。");

        var stageResult = encounters.Resolve(flow.Node.ContentId);
        if (!stageResult.Succeeded) return Failure(stageResult);
        var stage = stageResult.Data;
        if (stage == null || stage.Enemies == null || stage.Enemies.Count < 1 || stage.Enemies.Count > 5)
            return Invalid("战斗内容必须包含 1～5 名敌人。");
        var slots = new HashSet<int>();
        foreach (var enemy in stage.Enemies)
            if (enemy == null || enemy.Level < 1 || enemy.Slot < 1 || enemy.Slot > 5 ||
                !slots.Add(enemy.Slot) || !enemies.ContainsEnemy(enemy.EnemyID))
                return Invalid("敌人模板、等级或位置无效，或敌方位置重复。");

        var setup = new RoguelikeBattleSetup
        {
            RunId = context.RunId, StepId = flow.StepId, BattleId = context.BattleId,
            NodeId = flow.Node.NodeId, ContentId = flow.Node.ContentId, ChapterId = snapshot.CurrentChapterId,
            BackgroundId = stage.BattleID, RunSeed = snapshot.RunSeed, RandomStep = snapshot.RandomStep,
            Enemies = stage.Enemies,
        };
        var characters = snapshot.Characters.ToDictionary(item => item.CharacterID);
        var weapons = snapshot.Inventory.Weapons.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var artifacts = snapshot.Inventory.Artifacts.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        foreach (int characterId in snapshot.PartyCharacterIds)
        {
            if (!equipment.TryGetCharacterWeaponType(characterId, out int weaponType) || weaponType < 0 || weaponType > 4)
                return Invalid("角色模板或武器类型尚未配置：" + characterId);
            var character = characters[characterId];
            var build = new RoguelikeBattleAllyBuild { Slot = setup.Allies.Count + 1, Character = character };
            if (!string.IsNullOrWhiteSpace(character.EquippedWeaponInstanceId))
            {
                build.Weapon = weapons[character.EquippedWeaponInstanceId];
                if (!equipment.TryGetWeapon(build.Weapon.Weapon.WeaponID, out var definition) || definition == null ||
                    definition.WeaponID != build.Weapon.Weapon.WeaponID || definition.WeaponType != weaponType)
                    return Invalid("武器模板不存在或与角色类型不匹配：" + characterId);
            }
            foreach (var equipped in character.EquippedArtifacts.OrderBy(item => item.Slot))
                build.Artifacts.Add(artifacts[equipped.ArtifactInstanceId]);
            setup.Allies.Add(build);
        }
        return RoguelikeSaveResult<RoguelikeBattleSetup>.Success(RoguelikeSaveJson.DeepClone(setup));
    }

    private static RoguelikeSaveResult<RoguelikeBattleSetup> Invalid(string message)
    {
        return RoguelikeSaveResult<RoguelikeBattleSetup>.Failure(RoguelikeSaveErrorCode.ValidationFailed, message);
    }
    private static RoguelikeSaveResult<RoguelikeBattleSetup> Failure(RoguelikeSaveResult result)
    {
        return RoguelikeSaveResult<RoguelikeBattleSetup>.Failure(result.ErrorCode, result.Message, result.Exception);
    }
}
