using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Game-wide cosmetics, never restored or deleted with a roguelike run/entry slot.</summary>
[Serializable]
public sealed class PlayerAppearanceData
{
    public int SchemaVersion = 1;
    public string SelectedCardBackId = "";
    public List<string> UnlockedCardBackIds = new List<string>();
    public int SpentCardBackChoices;
    public int AvailableCardBackChoices => Math.Max(0, GrantedCompletions.Count - SpentCardBackChoices);
    public List<AppearanceFrameProgress> Characters = new List<AppearanceFrameProgress>();
    public List<AppearanceFrameProgress> Weapons = new List<AppearanceFrameProgress>();
    public List<string> GrantedCompletions = new List<string>();
    public int CharacterFrameLevel(int id) => Characters.Find(x => x.TemplateId == id)?.Level ?? 0;
    public int WeaponFrameLevel(int id) => Weapons.Find(x => x.TemplateId == id)?.Level ?? 0;
}

[Serializable]
public sealed class AppearanceFrameProgress
{
    public int TemplateId;
    public int Level;
}

[Serializable]
public sealed class AppearanceBattleParticipant
{
    public int CharacterId;
    public int WeaponId;
}

/// <summary>Persisted in the victorious run before delivery to the independent cosmetic save.</summary>
[Serializable]
public sealed class AppearanceBossAward
{
    public string CompletionId;
    public List<AppearanceBattleParticipant> Participants = new List<AppearanceBattleParticipant>();
}

public static class PlayerAppearanceRules
{
    // 0 = ordinary, 1 = gold, 2 = prismatic. This is cosmetic only, not item rarity or upgrade level.
    public const int MaximumFrameLevel = 2;
    public static RoguelikeSaveResult Validate(PlayerAppearanceData data)
    {
        if (data == null || data.SchemaVersion != 1)
            return RoguelikeSaveResult.Failure(data != null && data.SchemaVersion > 1 ? RoguelikeSaveErrorCode.UnsupportedSchema : RoguelikeSaveErrorCode.ValidationFailed, "外观存档版本无效。");
        // Older cosmetic saves have neither field. Preserve their already selected card back.
        data.UnlockedCardBackIds ??= new List<string>();
        if (!string.IsNullOrWhiteSpace(data.SelectedCardBackId) && !data.UnlockedCardBackIds.Contains(data.SelectedCardBackId))
            data.UnlockedCardBackIds.Add(data.SelectedCardBackId);
        if (!ValidFrames(data.Characters) || !ValidFrames(data.Weapons) || data.GrantedCompletions == null ||
            data.GrantedCompletions.Any(string.IsNullOrWhiteSpace) || data.GrantedCompletions.Distinct(StringComparer.Ordinal).Count() != data.GrantedCompletions.Count)
            return Invalid("外观存档包含无效等级或重复身份。");
        if (data.SpentCardBackChoices < 0 || data.SpentCardBackChoices > data.GrantedCompletions.Count ||
            data.UnlockedCardBackIds.Any(string.IsNullOrWhiteSpace) || data.UnlockedCardBackIds.Distinct(StringComparer.Ordinal).Count() != data.UnlockedCardBackIds.Count)
            return Invalid("卡背解锁记录或剩余选择次数无效。");
        return RoguelikeSaveResult.Success();
    }
    public static RoguelikeSaveResult Validate(AppearanceBossAward award)
    {
        if (award == null || string.IsNullOrWhiteSpace(award.CompletionId) || award.Participants == null ||
            award.Participants.Any(x => x == null || x.CharacterId <= 0 || x.WeaponId < 0) ||
            award.Participants.Select(x => x.CharacterId).Distinct().Count() != award.Participants.Count)
            return Invalid("Boss外观奖励必须包含唯一通关身份和真实参战角色/武器记录。");
        return RoguelikeSaveResult.Success();
    }
    private static bool ValidFrames(List<AppearanceFrameProgress> values) => values != null &&
        values.All(x => x != null && x.TemplateId > 0 && x.Level > 0 && x.Level <= MaximumFrameLevel) && values.Select(x => x.TemplateId).Distinct().Count() == values.Count;
    internal static bool Apply(PlayerAppearanceData data, AppearanceBossAward award)
    {
        var valid = Validate(award); if (!valid.Succeeded) throw new ArgumentException(valid.Message);
        if (data.GrantedCompletions.Contains(award.CompletionId)) return false;
        foreach (int id in award.Participants.Select(x => x.CharacterId).Distinct()) Increment(data.Characters, id);
        foreach (int id in award.Participants.Select(x => x.WeaponId).Where(x => x > 0).Distinct()) Increment(data.Weapons, id);
        data.GrantedCompletions.Add(award.CompletionId); return true;
    }
    private static void Increment(List<AppearanceFrameProgress> list, int id)
    {
        var entry = list.Find(x => x.TemplateId == id);
        if (entry == null) list.Add(new AppearanceFrameProgress { TemplateId = id, Level = 1 });
        else if (entry.Level < MaximumFrameLevel) entry.Level++;
    }
    private static RoguelikeSaveResult Invalid(string message) => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, message);
}
