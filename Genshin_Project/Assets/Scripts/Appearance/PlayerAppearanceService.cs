using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public interface ICardAppearanceSource
{
    event Action Changed;
    bool IsReady { get; }
    int GetCharacterFrameLevel(int characterId);
    int GetWeaponFrameLevel(int weaponId);
    string SelectedCardBackPath { get; }
}

[Serializable]
public sealed class CardBackDefinition
{
    public string Id;
    public string RelativePath;
}

/// <summary>Injectable catalog, independent of menus, scenes and unlock UI.</summary>
public sealed class CardBackCatalog
{
    public const string DefaultId = "UI_Gcg_CardBack_01";
    private readonly Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
    public CardBackCatalog(IEnumerable<CardBackDefinition> definitions = null)
    {
        foreach (var item in definitions ?? Enumerable.Empty<CardBackDefinition>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ValidPath(item.RelativePath) || entries.ContainsKey(item.Id))
                throw new ArgumentException("卡背需要唯一ID和StreamingAssets内有效的PNG相对路径。");
            entries.Add(item.Id, item.RelativePath.Replace('\\', '/'));
        }
    }
    public bool TryGetPath(string id, out string path) => entries.TryGetValue(id ?? "", out path);
    public IReadOnlyList<CardBackDefinition> GetAll() => entries.OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => new CardBackDefinition { Id = x.Key, RelativePath = x.Value }).ToArray();
    public static CardBackCatalog FromStreamingAssets()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, "art_assets", "CardBack");
        return new CardBackCatalog(Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
            .Select(x => new CardBackDefinition { Id = Path.GetFileNameWithoutExtension(x), RelativePath = "art_assets/CardBack/" + Path.GetFileName(x) }) : null);
    }
    private static bool ValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string p = path.Replace('\\', '/');
        return !Path.IsPathRooted(p) && !p.StartsWith("/", StringComparison.Ordinal) && !p.Contains(":") && !p.Split('/').Contains("..")
            && p.StartsWith("art_assets/", StringComparison.Ordinal) && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Independent game-wide cosmetic commands. No UI, battle objects, attribute changes or scene dependencies.</summary>
public sealed class PlayerAppearanceService : ICardAppearanceSource
{
    private readonly RoguelikeSaveRepository repository;
    private readonly CardBackCatalog cardBacks;
    private PlayerAppearanceData current;
    private string signature;
    public bool IsReady => current != null;
    public event Action Changed;
    public PlayerAppearanceService(RoguelikeSaveRepository repository, CardBackCatalog cardBacks = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.cardBacks = cardBacks ?? new CardBackCatalog();
    }
    public RoguelikeSaveResult<PlayerAppearanceData> Read()
    {
        var result = Refresh();
        return result.Succeeded ? RoguelikeSaveResult<PlayerAppearanceData>.Success(RoguelikeSaveJson.DeepClone(current))
            : RoguelikeSaveResult<PlayerAppearanceData>.Failure(result.ErrorCode, result.Message, result.Exception);
    }
    public RoguelikeSaveResult Refresh()
    {
        var loaded = repository.LoadAppearance();
        if (!loaded.Succeeded) return loaded;
        string nextSignature = RoguelikeSaveJson.Serialize(loaded.Data, false);
        bool changed = signature != nextSignature;
        current = loaded.Data; signature = nextSignature;
        if (changed && Changed != null)
            foreach (Action listener in Changed.GetInvocationList())
                try { listener(); } catch (Exception error) { Debug.LogException(error); }
        return RoguelikeSaveResult.Success();
    }
    public int GetCharacterFrameLevel(int characterId) { RequireReady(); return current.CharacterFrameLevel(characterId); }
    public int GetWeaponFrameLevel(int weaponId) { RequireReady(); return current.WeaponFrameLevel(weaponId); }
    public string SelectedCardBackId { get { RequireReady(); return current.SelectedCardBackId ?? ""; } }
    public string SelectedCardBackPath => cardBacks.TryGetPath(SelectedCardBackId, out string path) ? path : "";
    public IReadOnlyList<CardBackDefinition> GetAvailableCardBacks() => cardBacks.GetAll();
    /// <summary>Empty ID restores the application's default back. A selected back must exist in the supplied catalog.</summary>
    public RoguelikeSaveResult SelectCardBack(string id)
    {
        if (id == CardBackCatalog.DefaultId) id = "";
        id = id ?? "";
        if (id.Length > 0 && !cardBacks.TryGetPath(id, out _))
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "未登记的卡背ID：" + id);
        var saved = repository.SaveSelectedCardBack(id); if (!saved.Succeeded) return saved;
        var refresh = Refresh();
        return refresh.Succeeded ? saved : RoguelikeSaveResult.Success("卡背已保存；读取刷新待重试：" + refresh.Message);
    }
    /// <summary>One real, deduplicated chapter clear grants one choice. Unlock and equip are one atomic cosmetic save.</summary>
    public RoguelikeSaveResult UnlockCardBack(string id)
    {
        if (id == CardBackCatalog.DefaultId) return SelectCardBack("");
        if (!cardBacks.TryGetPath(id, out _))
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "未登记的卡背ID：" + id);
        var saved = repository.UnlockCardBack(id); if (!saved.Succeeded) return saved;
        var refresh = Refresh();
        return refresh.Succeeded ? saved : RoguelikeSaveResult.Success("卡背已解锁；读取刷新待重试：" + refresh.Message);
    }
    /// <summary>Other chapter modes may call this too. Each real clear needs a new completionId; retries reuse the original ID.</summary>
    public RoguelikeSaveResult RecordChapterBossVictory(string completionId, IReadOnlyList<AppearanceBattleParticipant> participants)
    {
        var award = new AppearanceBossAward { CompletionId = completionId, Participants = participants == null ? null : participants
            .Select(x => x == null ? null : new AppearanceBattleParticipant { CharacterId = x.CharacterId, WeaponId = x.WeaponId }).ToList() };
        var saved = repository.RecordAppearanceBossVictory(award); if (!saved.Succeeded) return saved;
        var refresh = Refresh();
        return refresh.Succeeded ? saved : RoguelikeSaveResult.Success("外观奖励已保存；读取刷新待重试：" + refresh.Message);
    }
    public RoguelikeSaveResult RestoreBackup(bool confirmed)
    {
        var result = repository.RestoreAppearanceBackup(confirmed);
        return result.Succeeded ? Refresh() : result;
    }
    private void RequireReady()
    { if (current == null) throw new InvalidOperationException("外观服务未准备好，请先成功调用Read/Refresh。"); }
}
