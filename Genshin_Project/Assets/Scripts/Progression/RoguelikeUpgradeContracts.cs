using System;
using System.Collections.Generic;

/// <summary>由节点解锁模块提供已通过的突破门槛数（0～6）；不根据角色等级或章节 ID 猜测。</summary>
public interface IRoguelikeUpgradeGate
{
    int GetUnlockedAscensions(RoguelikeProgressSnapshot snapshot);
}

public sealed class RoguelikeUpgradeLimits
{
    private static readonly int[] LevelCaps = { 20, 40, 50, 60, 70, 80, 90 };
    private static readonly int[] ArtifactCaps = { 4, 8, 12, 16, 20, 20, 20 };
    private static readonly int[] SkillCaps = { 2, 3, 4, 6, 8, 9, 10 };
    private static readonly int[] Thresholds = { 20, 40, 50, 60, 70, 80 };
    public int UnlockedAscensions { get; }
    public int CharacterAndWeaponLevel => LevelCaps[UnlockedAscensions];
    public int ArtifactLevel => ArtifactCaps[UnlockedAscensions];
    // 只限制材料培养的基础等级，命座、状态等额外加级不占用这一上限。
    public int SkillBaseLevel => SkillCaps[UnlockedAscensions];

    public RoguelikeUpgradeLimits(int unlockedAscensions)
    {
        if (unlockedAscensions < 0 || unlockedAscensions > 6)
            throw new ArgumentOutOfRangeException(nameof(unlockedAscensions));
        UnlockedAscensions = unlockedAscensions;
    }

    /// <summary>门槛已解锁且本体达到对应等级时才自动生效，包括此后才练到门槛的实例。</summary>
    public int GetAscension(int level)
    {
        int result = 0;
        while (result < UnlockedAscensions && level >= Thresholds[result]) result++;
        return result;
    }
}

/// <summary>角色按 CharacterID 唯一定位，装备必须按独立 InstanceId 定位。</summary>
public sealed class RoguelikeUpgradeTarget
{
    public LevelUpTarget Kind { get; }
    public int CharacterId { get; }
    public string InstanceId { get; }
    private RoguelikeUpgradeTarget(LevelUpTarget kind, int characterId, string instanceId)
    { Kind = kind; CharacterId = characterId; InstanceId = instanceId; }

    public static RoguelikeUpgradeTarget Character(int characterId)
    {
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        return new RoguelikeUpgradeTarget(LevelUpTarget.Character, characterId, null);
    }
    public static RoguelikeUpgradeTarget Weapon(string instanceId) => Equipment(LevelUpTarget.Weapon, instanceId);
    public static RoguelikeUpgradeTarget Artifact(string instanceId) => Equipment(LevelUpTarget.Artifact, instanceId);
    private static RoguelikeUpgradeTarget Equipment(LevelUpTarget kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("装备实例 ID 不能为空。", nameof(id));
        return new RoguelikeUpgradeTarget(kind, 0, id);
    }
}

/// <summary>只预览经验和等级，不预抽圣遗物词条。预览对象的修改不影响正式升级。</summary>
public sealed class RoguelikeUpgradePreview
{
    public LevelUpResult Result;
    public int LevelCap;
    public int ExperienceToCap;
    public int GainedExperience;
    public IReadOnlyList<RoguelikeItemStackData> Costs;
}

/// <summary>天赋升级按目标基础等级精确计件，不喂经验、不换算低级书；材料不足仍可查询费用。</summary>
public sealed class RoguelikeSkillUpgradePreview
{
    public int CharacterId;
    // 0 普攻、1 重击、2 战技、3 爆发，与持有实例 SkillLevels 的下标一致。
    public int SkillIndex;
    public int PreviousBaseLevel;
    public int TargetBaseLevel;
    public int BaseLevelCap;
    public bool CanAfford;
    public IReadOnlyList<RoguelikeItemStackData> Costs;
}
