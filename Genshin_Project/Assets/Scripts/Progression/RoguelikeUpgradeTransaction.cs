using System;
using System.Collections.Generic;

/// <summary>
/// 养成草稿：材料消耗、经验、突破及实例成长作为一个结果导出，由流程服务统一保存。
/// 不修改战斗实体，不处理装备吞噬或尚未定义的摩拉费用。
/// </summary>
public sealed class RoguelikeUpgradeTransaction
{
    private RoguelikeProgressSnapshot snapshot;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly RoguelikeItemCatalog items;
    private readonly LevelUpRequirementsTable requirements;
    private readonly ArtifactGenerationConfig artifactConfig;
    private readonly RoguelikeUpgradeLimits limits;
    private readonly CountingRandom random;
    private bool closed;
    private bool failed;

    public RoguelikeUpgradeTransaction(RoguelikeProgressSnapshot source, IRoguelikeEquipmentCatalog equipment,
        RoguelikeItemCatalog items, LevelUpRequirementsTable requirements, ArtifactGenerationConfig artifactConfig,
        int unlockedAscensions, IBattleRandomSource randomSource = null)
    {
        snapshot = new RoguelikeChapterRuntime(source).ExportSnapshot();
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        this.requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        this.artifactConfig = artifactConfig ?? throw new ArgumentNullException(nameof(artifactConfig));
        limits = new RoguelikeUpgradeLimits(unlockedAscensions);
        random = new CountingRandom(randomSource ?? BattleRandom.Source);
    }

    public RoguelikeUpgradePreview Preview(RoguelikeUpgradeTarget target,
        IReadOnlyList<RoguelikeItemStackData> selected)
    {
        EnsureOpen();
        var state = Resolve(snapshot, target);
        var costs = Sum(selected);
        int gained = 0;
        int smallest = int.MaxValue;
        foreach (var cost in costs)
        {
            int value = ExperienceValue(cost.ItemId, target.Kind);
            if (ItemCount(snapshot, cost.ItemId) < cost.Quantity)
                throw new InvalidOperationException("升级材料不足：" + cost.ItemId);
            gained = checked(gained + checked(value * cost.Quantity));
            smallest = Math.Min(smallest, value);
        }
        int needed = LevelUpCalculator.GetExperienceToReach(requirements, target.Kind,
            state.Level, state.Experience, state.Cap, state.Star);
        // 允许最后一件导致溢出；如果拿掉任意一件后仍能达到上限，就属于多选材料。
        if (gained > 0 && (needed == 0 || (long)gained - smallest >= needed))
            throw new InvalidOperationException("材料只能选择到刚好达到或溢出当前等级上限的数量。");
        return new RoguelikeUpgradePreview
        {
            LevelCap = state.Cap, ExperienceToCap = needed, GainedExperience = gained,
            Costs = costs.AsReadOnly(),
            Result = LevelUpCalculator.ApplyExperience(requirements, target.Kind, state.Level,
                state.Experience, gained, state.Cap, state.Star),
        };
    }

    /// <summary>在已有选择基础上，此种材料还允许增加多少件；同时受库存和刚好溢出限制。</summary>
    public int GetAdditionalMaterialLimit(RoguelikeUpgradeTarget target,
        IReadOnlyList<RoguelikeItemStackData> selected, string itemId)
    {
        var preview = Preview(target, selected);
        int value = ExperienceValue(itemId, target.Kind);
        long remaining = (long)preview.ExperienceToCap - preview.GainedExperience;
        if (remaining <= 0) return 0;
        int used = 0;
        int smallest = value;
        foreach (var cost in preview.Costs)
        {
            if (cost.ItemId == itemId) used = cost.Quantity;
            smallest = Math.Min(smallest, ExperienceValue(cost.ItemId, target.Kind));
        }
        // 混用大小经验道具时，也不能让已选的小道具变成可以拿掉的多余材料。
        long selectable = (remaining + smallest - 1) / value;
        return (int)Math.Min(ItemCount(snapshot, itemId) - used, selectable);
    }

    public LevelUpResult UpgradeCharacter(int characterId, IReadOnlyList<RoguelikeItemStackData> materials)
        => Upgrade(RoguelikeUpgradeTarget.Character(characterId), materials);
    public LevelUpResult UpgradeWeapon(string instanceId, IReadOnlyList<RoguelikeItemStackData> materials)
        => Upgrade(RoguelikeUpgradeTarget.Weapon(instanceId), materials);
    public LevelUpResult UpgradeArtifact(string instanceId, IReadOnlyList<RoguelikeItemStackData> materials)
        => Upgrade(RoguelikeUpgradeTarget.Artifact(instanceId), materials);

    public LevelUpResult Upgrade(RoguelikeUpgradeTarget target, IReadOnlyList<RoguelikeItemStackData> materials)
    {
        LevelUpResult result = null;
        Mutate(() =>
        {
            var preview = Preview(target, materials);
            var candidate = Consume(snapshot, preview.Costs);
            var state = Resolve(candidate, target);
            result = preview.Result;
            if (state.Character != null)
            {
                state.Character.Level = result.Level;
                state.Character.Experience = result.Experience;
                state.Character.Ascension = Math.Max(state.Character.Ascension, limits.GetAscension(result.Level));
            }
            else if (state.Weapon != null)
            {
                state.Weapon.Weapon.Level = result.Level;
                state.Weapon.Experience = result.Experience;
                state.Weapon.Weapon.Ascension = Math.Max(state.Weapon.Weapon.Ascension, limits.GetAscension(result.Level));
            }
            else
            {
                int before = random.Draws;
                new ArtifactGenerator(artifactConfig, random).UpgradeToLevel(state.Artifact.Artifact, result.Level);
                candidate.RandomStep = checked(candidate.RandomStep + random.Draws - before);
                state.Artifact.Experience = result.Experience;
            }
            Accept(candidate);
        });
        return result;
    }

    /// <summary>
    /// 查询四项天赋之一的升级费用。targetBaseLevel 必须是培养等级，不是加上命座后的显示等级。
    /// 各阶段基础上限按节点解锁提供的 2/3/4/6/8/9/10 执行，不额外套用原作等级限制。
    /// </summary>
    public RoguelikeSkillUpgradePreview PreviewSkillUpgrade(int characterId, int skillIndex, int targetBaseLevel)
    {
        EnsureOpen();
        if (skillIndex < 0 || skillIndex >= 4) throw new ArgumentOutOfRangeException(nameof(skillIndex));
        var character = FindCharacter(snapshot, characterId);
        int current = character.SkillLevels[skillIndex];
        if (current < 1 || current > 10)
            throw new InvalidOperationException("持有记录必须保存 1～10 的天赋基础等级，不能把额外加级计入。");
        if (targetBaseLevel <= current || targetBaseLevel > limits.SkillBaseLevel)
            throw new ArgumentOutOfRangeException(nameof(targetBaseLevel),
                "目标基础等级必须高于当前等级，且不超过已解锁的天赋上限 " + limits.SkillBaseLevel + "。");

        var totals = LevelUpCalculator.GetSkillMaterialsToReach(requirements, current, targetBaseLevel);
        var costs = new List<RoguelikeItemStackData>();
        bool affordable = true;
        // 顺序固定，界面和日志不会因字典枚举顺序不同而改变材料显示顺序。
        var types = new List<int>(totals.Keys);
        types.Sort();
        foreach (int type in types)
        {
            if (!items.TryGetTalentMaterial(type, out var definition))
                throw new InvalidOperationException("天赋材料类型未配置：" + type);
            int count = totals[type];
            costs.Add(new RoguelikeItemStackData { ItemId = definition.ItemId, Quantity = count });
            if (ItemCount(snapshot, definition.ItemId) < count) affordable = false;
        }
        return new RoguelikeSkillUpgradePreview
        {
            CharacterId = characterId, SkillIndex = skillIndex, PreviousBaseLevel = current,
            TargetBaseLevel = targetBaseLevel, BaseLevelCap = limits.SkillBaseLevel,
            CanAfford = affordable, Costs = costs.AsReadOnly(),
        };
    }

    /// <summary>消耗各档配表材料并更新选定的基础技能等级，与本草稿其他养成操作统一保存。</summary>
    public RoguelikeSkillUpgradePreview UpgradeSkill(int characterId, int skillIndex, int targetBaseLevel)
    {
        RoguelikeSkillUpgradePreview preview = null;
        Mutate(() =>
        {
            preview = PreviewSkillUpgrade(characterId, skillIndex, targetBaseLevel);
            // Consume 先检查所有材料；任意一种不足都不导出部分扣款结果。
            var candidate = Consume(snapshot, preview.Costs);
            FindCharacter(candidate, characterId).SkillLevels[skillIndex] = targetBaseLevel;
            Accept(candidate);
        });
        return preview;
    }

    /// <summary>每个对应命之座激活一命，最多六命；不改培养技能等级，角色自身等级查询据此解析命座加级。</summary>
    public void ActivateConstellation(int characterId)
    {
        Mutate(() =>
        {
            var character = FindCharacter(snapshot, characterId);
            if (character.ConstellationLevel >= 6) throw new InvalidOperationException("角色已满命。");
            string id = RoguelikeItemIds.Constellation(characterId);
            if (!items.TryGetItem(id, out var definition) || definition.Category != "Constellation" ||
                definition.ReferenceID != characterId)
                throw new InvalidOperationException("对应命之座道具未配置。");
            var candidate = Consume(snapshot, new[] { new RoguelikeItemStackData { ItemId = id, Quantity = 1 } });
            FindCharacter(candidate, characterId).ConstellationLevel++;
            Accept(candidate);
        });
    }

    /// <summary>通过突破节点后调用：不用追加道具即可结算已有溢出经验，并启用已达等级的突破。</summary>
    public void ReconcileUnlockedProgress()
    {
        Mutate(() =>
        {
            var targets = new List<RoguelikeUpgradeTarget>();
            foreach (var character in snapshot.Characters) targets.Add(RoguelikeUpgradeTarget.Character(character.CharacterID));
            foreach (var weapon in snapshot.Inventory.Weapons) targets.Add(RoguelikeUpgradeTarget.Weapon(weapon.InstanceId));
            foreach (var artifact in snapshot.Inventory.Artifacts) targets.Add(RoguelikeUpgradeTarget.Artifact(artifact.InstanceId));
            foreach (var target in targets) Upgrade(target, new RoguelikeItemStackData[0]);
        });
    }

    public RoguelikeProgressSnapshot ExportSnapshot()
    {
        EnsureOpen();
        return RoguelikeSaveJson.DeepClone(snapshot);
    }

    public void Close() { closed = true; }

    private State Resolve(RoguelikeProgressSnapshot data, RoguelikeUpgradeTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        var state = new State();
        switch (target.Kind)
        {
            case LevelUpTarget.Character:
                state.Character = FindCharacter(data, target.CharacterId);
                state.Level = state.Character.Level; state.Experience = state.Character.Experience;
                state.Cap = limits.CharacterAndWeaponLevel;
                break;
            case LevelUpTarget.Weapon:
                state.Weapon = data.Inventory.Weapons.Find(value => value.InstanceId == target.InstanceId);
                if (state.Weapon == null) throw new InvalidOperationException("找不到武器实例：" + target.InstanceId);
                if (!equipment.TryGetWeapon(state.Weapon.Weapon.WeaponID, out var definition) || definition == null ||
                    definition.WeaponID != state.Weapon.Weapon.WeaponID)
                    throw new InvalidOperationException("武器模板未配置。");
                state.Star = definition.Star;
                state.Level = state.Weapon.Weapon.Level; state.Experience = state.Weapon.Experience;
                state.Cap = limits.CharacterAndWeaponLevel;
                break;
            case LevelUpTarget.Artifact:
                state.Artifact = data.Inventory.Artifacts.Find(value => value.InstanceId == target.InstanceId);
                if (state.Artifact == null) throw new InvalidOperationException("找不到圣遗物实例：" + target.InstanceId);
                var artifact = state.Artifact.Artifact;
                state.Star = artifact.Star; state.Level = artifact.Level; state.Experience = state.Artifact.Experience;
                int maximum = requirements.GetMaximumLevel(target.Kind, state.Star);
                if (artifact.MaxLevel != maximum || artifactConfig.GetMaxLevel(state.Star) != maximum)
                    throw new InvalidOperationException("圣遗物实例与经验表、成长表的最高等级不一致。");
                state.Cap = limits.ArtifactLevel;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(target));
        }
        state.Cap = Math.Min(state.Cap, requirements.GetMaximumLevel(target.Kind, state.Star));
        if (state.Level > state.Cap) throw new InvalidOperationException("当前实例等级超过注入的解锁上限，不能降级或继续喂材料。");
        return state;
    }

    private RoguelikeProgressSnapshot Consume(RoguelikeProgressSnapshot source, IReadOnlyList<RoguelikeItemStackData> costs)
    {
        var inventory = new RoguelikeInventoryTransaction(source, equipment, items);
        try { inventory.ConsumeItems(costs); return inventory.ExportSnapshot(); }
        finally { inventory.Close(); }
    }

    private int ExperienceValue(string itemId, LevelUpTarget target)
    {
        if (!items.TryGetExperienceValue(itemId, target, out int value) || value <= 0)
            throw new InvalidOperationException("此道具不能用于该类型的经验升级：" + itemId);
        return value;
    }

    private static RoguelikeCharacterProgressData FindCharacter(RoguelikeProgressSnapshot data, int id)
    {
        var character = data.Characters.Find(value => value.CharacterID == id);
        if (character == null) throw new InvalidOperationException("尚未持有角色：" + id);
        return character;
    }

    private static int ItemCount(RoguelikeProgressSnapshot data, string id)
        => data.Inventory.Items.Find(item => item.ItemId == id)?.Quantity ?? 0;

    private static List<RoguelikeItemStackData> Sum(IReadOnlyList<RoguelikeItemStackData> materials)
    {
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var material in materials)
        {
            if (material == null || string.IsNullOrWhiteSpace(material.ItemId) || material.Quantity <= 0)
                throw new ArgumentException("材料必须包含有效 ID 和正数量。", nameof(materials));
            sums.TryGetValue(material.ItemId, out int count);
            sums[material.ItemId] = checked(count + material.Quantity);
        }
        var result = new List<RoguelikeItemStackData>();
        foreach (var pair in sums) result.Add(new RoguelikeItemStackData { ItemId = pair.Key, Quantity = pair.Value });
        return result;
    }

    private void Accept(RoguelikeProgressSnapshot candidate)
    {
        var validation = new RoguelikeSaveValidator().Validate(candidate);
        if (!validation.Succeeded) throw new InvalidOperationException(validation.Message);
        snapshot = candidate;
    }

    private void Mutate(Action operation)
    {
        EnsureOpen();
        try { operation(); }
        catch { failed = true; throw; }
    }

    private void EnsureOpen()
    {
        if (closed || failed) throw new InvalidOperationException("养成草稿已结束或有失败操作，必须重新创建。");
    }

    private sealed class State
    {
        public int Level, Experience, Star, Cap;
        public RoguelikeCharacterProgressData Character;
        public RoguelikeWeaponInstanceData Weapon;
        public RoguelikeArtifactInstanceData Artifact;
    }

    private sealed class CountingRandom : IBattleRandomSource
    {
        private readonly IBattleRandomSource source;
        public int Draws { get; private set; }
        public CountingRandom(IBattleRandomSource source) { this.source = source; }
        public int NextInt(int minInclusive, int maxExclusive)
        { int value = source.NextInt(minInclusive, maxExclusive); Draws++; return value; }
        public float NextFloat01() { float value = source.NextFloat01(); Draws++; return value; }
    }
}
