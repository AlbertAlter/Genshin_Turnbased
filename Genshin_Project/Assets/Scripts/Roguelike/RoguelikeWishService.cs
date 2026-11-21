using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class RoguelikeWishFivePlan
{
    public int Sequence;
    public int Draw;
    public bool IsUp;

    internal bool IsValid => Sequence >= 0 && Draw > 0;
}

[Serializable]
public sealed class RoguelikeWishPlanningData
{
    public int Seed;
    public RoguelikeWishFivePlan Character;
    public RoguelikeWishFivePlan Weapon;

    internal bool IsValid => Seed > 0 && Character?.IsValid == true && Weapon?.IsValid == true;
}

[Serializable]
public sealed class RoguelikeWishProgress
{
    public int CharacterSinceFive;
    public int CharacterSinceFour;
    public int WeaponSinceFive;
    public int WeaponSinceFour;
    public int FivePlanSeed;
    public RoguelikeWishFivePlan CharacterFivePlan;
    public RoguelikeWishFivePlan WeaponFivePlan;
    public bool CharacterFourUpGuaranteed;
    public bool WeaponFourUpGuaranteed;
    public List<RoguelikeWishHistoryEntry> History = new List<RoguelikeWishHistoryEntry>();

    internal int SinceFive(RoguelikeWishKind kind) => kind == RoguelikeWishKind.Character ? CharacterSinceFive : WeaponSinceFive;
    internal int SinceFour(RoguelikeWishKind kind) => kind == RoguelikeWishKind.Character ? CharacterSinceFour : WeaponSinceFour;
    internal RoguelikeWishFivePlan FivePlan(RoguelikeWishKind kind)
        => kind == RoguelikeWishKind.Character ? CharacterFivePlan : WeaponFivePlan;
    internal bool FourUpGuaranteed(RoguelikeWishKind kind)
        => kind == RoguelikeWishKind.Character ? CharacterFourUpGuaranteed : WeaponFourUpGuaranteed;

    internal void Record(RoguelikeWishKind kind, int stars, bool isUp, RoguelikeWishRules rules)
    {
        var next = stars == 5 ? rules.PlanFive(FivePlanSeed, kind, checked(FivePlan(kind).Sequence + 1), forceUp: !isUp) : null;
        int sinceFour = stars == 4 ? 0 : (int)Math.Min(rules.FourStarLimit - 1, SinceFour(kind) + 1L);
        if (kind == RoguelikeWishKind.Character)
        {
            CharacterSinceFive = stars == 5 ? 0 : checked(CharacterSinceFive + 1);
            CharacterSinceFour = sinceFour;
            if (stars == 5) CharacterFivePlan = next;
            if (stars == 4) CharacterFourUpGuaranteed = !isUp;
        }
        else
        {
            WeaponSinceFive = stars == 5 ? 0 : checked(WeaponSinceFive + 1);
            WeaponSinceFour = sinceFour;
            if (stars == 5) WeaponFivePlan = next;
            if (stars == 4) WeaponFourUpGuaranteed = !isUp;
        }
    }
}

/// <summary>可由玩法装配传入项目数值。概率为未保底时的单次绝对概率。</summary>
public sealed class RoguelikeWishRules
{
    internal const float UpRate = .75f;
    public int CharacterLimit { get; }
    public int WeaponLimit { get; }
    public int FourStarLimit { get; }
    public float CharacterFiveRate { get; }
    public float CharacterFourRate { get; }
    public float WeaponFiveRate { get; }
    public float WeaponFourRate { get; }
    public int FourStarGlitter { get; }
    public int FiveStarGlitter { get; }
    public int ThreeStarStardust { get; }
    public int FullFourStarGlitter { get; }
    public int FullFiveStarGlitter { get; }

    public RoguelikeWishRules(int characterLimit = 90, int weaponLimit = 80, int fourStarLimit = 10,
        float characterFiveRate = .006f, float characterFourRate = .051f,
        float weaponFiveRate = .007f, float weaponFourRate = .06f,
        int fourStarGlitter = 2, int fiveStarGlitter = 10, int threeStarStardust = 15,
        int fullFourStarGlitter = 5, int fullFiveStarGlitter = 25)
    {
        if (characterLimit < 1 || weaponLimit < 1 || fourStarLimit < 1 ||
            fourStarGlitter < 0 || fiveStarGlitter < 0 || threeStarStardust < 0 ||
            fullFourStarGlitter < 0 || fullFiveStarGlitter < 0 ||
            !ValidRates(characterFiveRate, characterFourRate) || !ValidRates(weaponFiveRate, weaponFourRate))
            throw new ArgumentException("祈愿概率、保底次数或货币奖励无效。");
        CharacterLimit = characterLimit;
        WeaponLimit = weaponLimit;
        FourStarLimit = fourStarLimit;
        CharacterFiveRate = characterFiveRate;
        CharacterFourRate = characterFourRate;
        WeaponFiveRate = weaponFiveRate;
        WeaponFourRate = weaponFourRate;
        FourStarGlitter = fourStarGlitter;
        FiveStarGlitter = fiveStarGlitter;
        ThreeStarStardust = threeStarStardust;
        FullFourStarGlitter = fullFourStarGlitter;
        FullFiveStarGlitter = fullFiveStarGlitter;
    }

    private static bool ValidRates(float five, float four) => five >= 0f && four >= 0f && five + four <= 1f;
    internal int Limit(RoguelikeWishKind kind) => kind == RoguelikeWishKind.Character ? CharacterLimit : WeaponLimit;

    internal float FiveRate(RoguelikeWishKind kind, int draw)
    {
        if (draw >= Limit(kind)) return 1f;
        bool character = kind == RoguelikeWishKind.Character;
        int softStart = character ? 74 : 63;
        float rate = character ? CharacterFiveRate : WeaponFiveRate;
        return Math.Min(1f, rate + Math.Max(0, draw - softStart + 1) * (character ? .06f : .07f));
    }

    internal float FourRateWhenNoFive(RoguelikeWishKind kind, RoguelikeWishProgress progress)
    {
        int sinceFour = progress.SinceFour(kind);
        if (sinceFour >= FourStarLimit - 1) return 1f;
        float four = kind == RoguelikeWishKind.Character ? CharacterFourRate : WeaponFourRate;
        if (sinceFour == FourStarLimit - 2) four += .5f;
        float remaining = 1f - FiveRate(kind, progress.SinceFive(kind) + 1);
        return remaining <= four ? 1f : four / remaining;
    }

    internal RoguelikeWishFivePlan PlanFive(int seed, RoguelikeWishKind kind, int sequence, int sinceFive = 0, bool forceUp = false)
    {
        // 预定序列与卡池、战斗随机步数及开档次数无关。
        int planSeed = unchecked(seed ^ (sequence * 16777619) ^ ((int)kind + 1) * 486187739);
        IBattleRandomSource rng = new SystemBattleRandomSource(planSeed);
        int draw = Math.Min(sinceFive, Limit(kind) - 1) + 1;
        for (; draw < Limit(kind); draw++)
        {
            float rate = FiveRate(kind, draw);
            if (rate >= 1f || rng.NextFloat01() < rate) break;
        }
        return new RoguelikeWishFivePlan { Sequence = sequence, Draw = draw, IsUp = forceUp || rng.NextFloat01() < UpRate };
    }
}

internal enum RoguelikeWishIssue
{
    None, NoSave, LoadFailed, NoBanners, BannerUnavailable, ReadOnly, StepUnavailable, ItemDataMissing,
    ThreeStarPoolMissing, RewardDataMissing, InvalidCount, InsufficientFates, RetryRequired, Submitting,
    StalePage, StaleResult, SaveFailed, DrawFailed, ResultsUnavailable
}

public sealed class RoguelikeWishOffer
{
    public RoguelikeWishPool Pool { get; }
    public int UntilFive { get; }
    public int UntilFour { get; }
    public string Nation { get; }
    public string BlockReason { get; }
    internal RoguelikeWishIssue Issue { get; }
    public string Element { get; }
    public int WeaponType { get; }
    internal IReadOnlyList<RoguelikeWishPrize> ConstantFive { get; }
    internal IReadOnlyList<RoguelikeWishPrize> ConstantFour { get; }
    internal IReadOnlyList<RoguelikeWishPrize> ThreeStars { get; }

    internal RoguelikeWishOffer(RoguelikeWishPool pool, int untilFive, int untilFour, string nation,
        RoguelikeWishIssue issue, string blockReason, string element, int weaponType, ILookup<int, RoguelikeWishPrize> constants)
    {
        Pool = pool; UntilFive = untilFive; UntilFour = untilFour; Nation = nation; BlockReason = blockReason;
        Issue = issue;
        Element = element; WeaponType = weaponType;
        ConstantFive = constants[5].ToArray();
        ConstantFour = constants[4].ToArray();
        ThreeStars = constants[3].ToArray();
    }
}

public sealed class RoguelikeWishPage
{
    public string StepId { get; }
    public int Balance { get; }
    public IReadOnlyList<RoguelikeWishOffer> Offers { get; }
    public string RetryPool { get; }
    public int RetryCount { get; }
    public int? NodesUntilNextPool { get; }

    internal RoguelikeWishPage(string stepId, int balance, RoguelikeWishOffer[] offers, string retryPool, int retryCount,
        int? nodesUntilNextPool = null)
    {
        StepId = stepId; Balance = balance; Offers = Array.AsReadOnly(offers);
        RetryPool = retryPool; RetryCount = retryCount;
        NodesUntilNextPool = nodesUntilNextPool;
    }

    public bool CanDraw(RoguelikeWishOffer offer, int count)
        => DrawIssue(offer, count) == RoguelikeWishIssue.None;

    internal RoguelikeWishIssue DrawIssue(RoguelikeWishOffer offer, int count)
    {
        if (count != 1 && count != 10) return RoguelikeWishIssue.InvalidCount;
        if (offer == null || !Offers.Contains(offer)) return RoguelikeWishIssue.BannerUnavailable;
        if (offer.Issue != RoguelikeWishIssue.None) return offer.Issue;
        if (RetryPool != null && (RetryPool != offer.Pool.Id || RetryCount != count))
            return RoguelikeWishIssue.RetryRequired;
        return Balance < count ? RoguelikeWishIssue.InsufficientFates : RoguelikeWishIssue.None;
    }
}

public sealed class RoguelikeWishReward
{
    public RoguelikeWishPrize Prize { get; }
    public bool IsNew { get; }
    public string ConstellationItemId { get; }
    public string WeaponInstanceId { get; }
    public int ConvertedGlitter { get; }
    public int Starglitter { get; }
    public int Stardust { get; }

    internal RoguelikeWishReward(RoguelikeWishPrize prize, bool isNew, string constellationItemId = null,
        string weaponInstanceId = null, int convertedGlitter = 0, int starglitter = 0, int stardust = 0)
    {
        Prize = prize; IsNew = isNew; ConstellationItemId = constellationItemId;
        WeaponInstanceId = weaponInstanceId; ConvertedGlitter = convertedGlitter;
        Starglitter = checked(starglitter + convertedGlitter); Stardust = stardust;
    }
}

public sealed class RoguelikeWishBatch
{
    public RoguelikeWishPool Pool { get; }
    public IReadOnlyList<RoguelikeWishReward> Rewards { get; }
    public int Starglitter { get; }
    public int Stardust { get; }
    public string CommittedStepId { get; internal set; }

    internal RoguelikeWishBatch(RoguelikeWishPool pool, RoguelikeWishReward[] rewards, int starglitter, int stardust)
    {
        Pool = pool; Rewards = Array.AsReadOnly(rewards); Starglitter = starglitter;
        Stardust = stardust;
    }
}

/// <summary>祈愿只在完整结果保存成功后返回结果。同一步骤落盘失败时重试同一份结果。</summary>
public sealed class RoguelikeWishService
{
    public const string FateItemId = "670101";
    public const string StarglitterItemId = "680101";
    public const string StardustItemId = "680102";
    private readonly RoguelikeFlowService flow;
    private readonly RoguelikeInventoryService inventory;
    private readonly RoguelikeWishCatalog catalog;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly IRoguelikeItemCatalog items;
    private readonly IRoguelikeCharacterAcquisitionCatalog characters;
    private readonly RoguelikeWishPrize[] legacyThreeStars;
    private readonly Func<RoguelikeProgressSnapshot, Version> selectPhase;
    private readonly Func<int, string> nation;
    private readonly Func<int, CharacterOverviewData> characterOverview;
    private readonly Dictionary<int, int> weaponTypes = new Dictionary<int, int>();
    private readonly RoguelikeWishRules rules;
    private readonly IBattleRandomSource random;
    private bool committing;
    private PendingWish pending;

    private sealed class PendingWish
    {
        public RoguelikeProgressSnapshot Snapshot;
        public RoguelikeWishBatch Batch;
    }

    public event Action Changed;
    public RoguelikeWishRules Rules => rules;

    public RoguelikeWishService(RoguelikeFlowService flow, RoguelikeInventoryService inventory,
        RoguelikeWishCatalog catalog, IRoguelikeEquipmentCatalog equipment, IRoguelikeItemCatalog items,
        IRoguelikeCharacterAcquisitionCatalog characters, IEnumerable<WeaponAttributesData> weapons,
        Func<RoguelikeProgressSnapshot, Version> selectPhase = null, Func<int, string> nation = null,
        RoguelikeWishRules rules = null, IBattleRandomSource random = null,
        Func<int, CharacterOverviewData> characterOverview = null)
    {
        this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
        this.rules = rules ?? new RoguelikeWishRules();
        this.selectPhase = selectPhase ?? (snapshot => catalog.PhaseAt(snapshot.CurrentChapterId, snapshot.CurrentStageIndex));
        this.nation = nation;
        this.random = random;
        this.characterOverview = characterOverview;
        var weaponTemplates = (weapons ?? throw new ArgumentNullException(nameof(weapons))).Where(weapon => weapon != null).ToArray();
        foreach (var weapon in weaponTemplates) weaponTypes[weapon.WeaponID] = weapon.WeaponType;
        legacyThreeStars = catalog.HasConstantPools ? Array.Empty<RoguelikeWishPrize>() : weaponTemplates.Where(weapon => weapon.Star == 3)
            .OrderBy(weapon => weapon.WeaponID)
            .Select(weapon => new RoguelikeWishPrize(RoguelikeWishKind.Weapon, weapon.WeaponID, 3, weapon.WeaponName))
            .Where(HasTemplate).ToArray();
    }

    public RoguelikeSaveResult<RoguelikeWishPage> Read()
    {
        using var transaction = flow.BeginSaveTransaction();
        try
        {
            var current = flow.Resume();
            return current.Succeeded ? RoguelikeSaveResult<RoguelikeWishPage>.Success(BuildPage(current.Data.Snapshot))
                : Failure<RoguelikeWishPage>(current);
        }
        catch (Exception error) { return Invalid<RoguelikeWishPage>(error.Message, error); }
    }

#if UNITY_EDITOR
    internal Version[] DebugPhases => catalog.Pools.Select(pool => pool.Phase).Distinct().OrderBy(phase => phase).ToArray();

    public RoguelikeSaveResult<RoguelikeWishPage> ReadDebugBanners(Version phase)
    {
        using var transaction = flow.BeginSaveTransaction();
        try
        {
            var current = flow.Resume();
            var snapshot = current.Succeeded ? current.Data.Snapshot : null;
            int balance = snapshot == null ? 0 : new RoguelikeInventoryView(snapshot).GetItemCount(FateItemId);
            var offers = BuildOffers(phase, snapshot?.WishProgress ?? new RoguelikeWishProgress(),
                RoguelikeWishIssue.ReadOnly, "当前为只读卡池。");
            return RoguelikeSaveResult<RoguelikeWishPage>.Success(new RoguelikeWishPage(null, balance, offers, null, 0));
        }
        catch (Exception error) { return Invalid<RoguelikeWishPage>(error.Message, error); }
    }
#endif

    /// <summary>Reads the active save's recorded draws, newest first, optionally filtered by pool kind.</summary>
    public RoguelikeSaveResult<IReadOnlyList<RoguelikeWishHistoryEntry>> ReadHistory(RoguelikeWishKind? poolKind = null)
    {
        using var transaction = flow.BeginSaveTransaction();
        try
        {
            var current = flow.Resume();
            if (!current.Succeeded) return Failure<IReadOnlyList<RoguelikeWishHistoryEntry>>(current);
            var history = current.Data.Snapshot.WishProgress?.History;
            var ordered = history == null ? Array.Empty<RoguelikeWishHistoryEntry>()
                : Enumerable.Reverse(history).Where(entry => !poolKind.HasValue || entry.PoolKind == poolKind.Value).ToArray();
            return RoguelikeSaveResult<IReadOnlyList<RoguelikeWishHistoryEntry>>.Success(Array.AsReadOnly(ordered));
        }
        catch (Exception error) { return Invalid<IReadOnlyList<RoguelikeWishHistoryEntry>>(error.Message, error); }
    }

    public RoguelikeSaveResult<RoguelikeWishBatch> Draw(string stepId, string wishId, int count)
        => Draw(stepId, wishId, count, out _);

    internal RoguelikeSaveResult<RoguelikeWishBatch> Draw(string stepId, string wishId, int count,
        out RoguelikeWishIssue issue)
    {
        using var transaction = flow.BeginSaveTransaction();
        issue = RoguelikeWishIssue.Submitting;
        if (committing) return Invalid<RoguelikeWishBatch>("祈愿正在提交。");
        committing = true;
        try
        {
            var current = flow.Resume();
            if (!current.Succeeded)
            {
                issue = ReadIssue(current);
                return Failure<RoguelikeWishBatch>(current);
            }
            var page = BuildPage(current.Data.Snapshot);
            if (string.IsNullOrWhiteSpace(stepId) || page.StepId != stepId)
            {
                issue = RoguelikeWishIssue.StalePage;
                return Invalid<RoguelikeWishBatch>("祈愿页面已过期，请刷新。");
            }
            var offer = page.Offers.FirstOrDefault(entry => entry.Pool.Id == wishId);
            issue = page.DrawIssue(offer, count);
            if (issue != RoguelikeWishIssue.None)
                return Invalid<RoguelikeWishBatch>(issue switch
                {
                    RoguelikeWishIssue.InsufficientFates => $"纠缠之缘不足，需 {count} 个，当前有 {page.Balance} 个。",
                    RoguelikeWishIssue.RetryRequired => $"请重试卡池 {page.RetryPool} 的 {page.RetryCount} 次祈愿。",
                    RoguelikeWishIssue.InvalidCount => "祈愿次数必须为 1 或 10。",
                    _ => offer?.BlockReason ?? "卡池未开放。",
                });
            pending ??= Prepare(current.Data.Snapshot, offer, count);
            var result = flow.CommitProgressAction(stepId, pending.Snapshot, RoguelikeSaveReason.GachaCompleted,
                out string committedStepId);
            if (!result.Succeeded)
            {
                issue = RoguelikeWishIssue.SaveFailed;
                return Failure<RoguelikeWishBatch>(result);
            }
            var batch = pending.Batch;
            batch.CommittedStepId = committedStepId;
            pending = null;
            if (Changed != null)
                foreach (Action listener in Changed.GetInvocationList())
                    try { listener(); } catch (Exception error) { Debug.LogException(error); }
            return RoguelikeSaveResult<RoguelikeWishBatch>.Success(batch);
        }
        catch (Exception error)
        {
            issue = RoguelikeWishIssue.DrawFailed;
            return Invalid<RoguelikeWishBatch>(error.Message, error);
        }
        finally { committing = false; }
    }

    private RoguelikeWishPage BuildPage(RoguelikeProgressSnapshot snapshot)
    {
        string stepId = snapshot.Flow.StepId;
        if (pending?.Snapshot.Flow.StepId != stepId) pending = null;
        var state = snapshot.WishProgress ?? new RoguelikeWishProgress();
        var phase = selectPhase(RoguelikeSaveJson.DeepClone(snapshot));
        var view = new RoguelikeInventoryView(snapshot);
        var block = RoguelikeWishIssue.None;
        string blocked = null;
        if (snapshot.Flow.Step == RoguelikeFlowStep.Battle || snapshot.Flow.Step == RoguelikeFlowStep.ChapterCompleted)
        {
            block = RoguelikeWishIssue.StepUnavailable;
            blocked = "当前步骤不能祈愿。";
        }
        else if (!HasItem(FateItemId, RoguelikeItemType.Stack) || !HasItem(StarglitterItemId, RoguelikeItemType.Stack) ||
            !HasItem(StardustItemId, RoguelikeItemType.Stack))
        {
            block = RoguelikeWishIssue.ItemDataMissing;
            blocked = "道具表缺少纠缠之缘、星辉或星尘。";
        }
        return new RoguelikeWishPage(stepId, view.GetItemCount(FateItemId), BuildOffers(phase, state, block, blocked),
            pending?.Batch.Pool.Id, pending?.Batch.Rewards.Count ?? 0,
            catalog.NodesUntilNextOpening(snapshot.CurrentChapterId, snapshot.CurrentStageIndex));
    }

    private RoguelikeWishOffer[] BuildOffers(Version phase, RoguelikeWishProgress state, RoguelikeWishIssue block, string blocked)
        => catalog.ForPhase(phase).Select(pool =>
        {
            var featured = pool.Featured;
            var issue = block;
            string error = blocked;
            var constants = (catalog.HasConstantPools ? catalog.ConstantPrizes(pool).Select(ResolveConstantName) : legacyThreeStars).ToArray();
            if (error == null && !constants.Any(prize => prize.Stars == 3))
            {
                issue = RoguelikeWishIssue.ThreeStarPoolMissing;
                error = "没有可获得的 3★ 奖励。";
            }
            if (error == null)
            {
                var missing = new[] { featured }.Concat(pool.FourStars).Concat(constants).Where(prize => !HasTemplate(prize))
                    .Select(prize => $"{prize.Name}（{prize.Id}）").ToArray();
                if (missing.Length > 0)
                {
                    issue = RoguelikeWishIssue.RewardDataMissing;
                    error = "卡池缺少可发放模板：" + string.Join("、", missing) + "。";
                }
            }
            bool isCharacter = featured.Kind == RoguelikeWishKind.Character;
            var overview = isCharacter ? characterOverview?.Invoke(featured.Id) : null;
            int weaponType = isCharacter ? overview?.WeaponType ?? -1 :
                weaponTypes.TryGetValue(featured.Id, out int type) ? type : -1;
            return new RoguelikeWishOffer(pool, Math.Max(1, rules.Limit(featured.Kind) - state.SinceFive(featured.Kind)),
                Math.Max(1, rules.FourStarLimit - state.SinceFour(featured.Kind)),
                isCharacter ? overview?.Nation ?? nation?.Invoke(featured.Id) : null, issue, error, overview?.Element, weaponType,
                constants.ToLookup(prize => prize.Stars));
        }).ToArray();

    private RoguelikeWishPrize ResolveConstantName(RoguelikeWishPrize prize)
    {
        string name = prize.Kind == RoguelikeWishKind.Character ? characterOverview?.Invoke(prize.Id)?.Name
            : equipment.TryGetWeapon(prize.Id, out var weapon) ? weapon?.WeaponName : null;
        return new RoguelikeWishPrize(prize.Kind, prize.Id, prize.Stars, name ?? prize.Id.ToString());
    }

    private PendingWish Prepare(RoguelikeProgressSnapshot source, RoguelikeWishOffer offer, int count)
    {
        var pool = offer.Pool;
        var draft = inventory.CreateDraft(source);
        try
        {
            draft.ConsumeItems(new[] { Stack(FateItemId, count) });
            var state = RoguelikeSaveJson.DeepClone(source.WishProgress) ?? new RoguelikeWishProgress();
            if (state.FivePlanSeed == 0)
            {
                var planned = flow.GetWishPlanning(rules, random);
                if (!planned.Succeeded) throw new InvalidOperationException(planned.Message, planned.Exception);
                state.FivePlanSeed = planned.Data.Seed;
                // 旧档只知道已抽次数。用固定种子从该次之后补预定，反复读取同一旧档结果一致。
                state.CharacterFivePlan = rules.PlanFive(state.FivePlanSeed, RoguelikeWishKind.Character, 0, state.CharacterSinceFive);
                state.WeaponFivePlan = rules.PlanFive(state.FivePlanSeed, RoguelikeWishKind.Weapon, 0, state.WeaponSinceFive);
            }
            var rng = random ?? BattleRandom.Source;
            var rewards = new RoguelikeWishReward[count];
            var weaponTypesOwned = new HashSet<int>(source.Inventory.Weapons.Select(item => item.Weapon.WeaponID));
            int draws = 0;
            int glitter = 0;
            int dust = 0;
            var kind = pool.Featured.Kind;
            string drawnAtUtc = DateTime.UtcNow.ToString("O");
            string poolTitle = kind == RoguelikeWishKind.Weapon ? $"{pool.Title} · {pool.Featured.Name}" : pool.Title;
            state.History ??= new List<RoguelikeWishHistoryEntry>();
            for (int index = 0; index < count; index++)
            {
                var prize = RollPrize(offer, state, rng, ref draws, out bool isUp);
                rewards[index] = Award(draft, prize, weaponTypesOwned);
                glitter = checked(glitter + rewards[index].Starglitter);
                dust = checked(dust + rewards[index].Stardust);
                state.Record(kind, prize.Stars, isUp, rules);
                state.History.Add(new RoguelikeWishHistoryEntry
                {
                    ChapterId = source.CurrentChapterId, WishId = pool.Id, PoolTitle = poolTitle, PoolKind = kind,
                    PrizeId = prize.Id, PrizeName = prize.Name, PrizeKind = prize.Kind, Stars = prize.Stars, DrawnAtUtc = drawnAtUtc,
                });
            }
            if (glitter > 0) draft.AddItems(new[] { Stack(StarglitterItemId, glitter) });
            if (dust > 0) draft.AddItems(new[] { Stack(StardustItemId, dust) });
            var snapshot = draft.ExportSnapshot();
            snapshot.WishProgress = state;
            snapshot.RandomStep = checked(snapshot.RandomStep + draws);
            return new PendingWish { Snapshot = snapshot, Batch = new RoguelikeWishBatch(pool, rewards, glitter, dust) };
        }
        finally { draft.Close(); }
    }

    private RoguelikeWishPrize RollPrize(RoguelikeWishOffer offer, RoguelikeWishProgress progress,
        IBattleRandomSource rng, ref int draws, out bool isUp)
    {
        var pool = offer.Pool;
        var kind = pool.Featured.Kind;
        var five = progress.FivePlan(kind);
        if (progress.SinceFive(kind) >= five.Draw - 1)
        {
            isUp = five.IsUp || offer.ConstantFive.Count == 0;
            return isUp ? pool.Featured : Pick(offer.ConstantFive, rng, ref draws);
        }
        float fourRate = rules.FourRateWhenNoFive(kind, progress);
        if (fourRate >= 1f || (fourRate > 0f && NextWishValue(rng, ref draws) < fourRate))
        {
            isUp = offer.ConstantFour.Count == 0 || progress.FourUpGuaranteed(kind) ||
                NextWishValue(rng, ref draws) < RoguelikeWishRules.UpRate;
            return Pick(isUp ? pool.FourStars : offer.ConstantFour, rng, ref draws);
        }
        isUp = false;
        return Pick(offer.ThreeStars, rng, ref draws);
    }

    private static float NextWishValue(IBattleRandomSource rng, ref int draws)
    {
        float value = rng.NextFloat01();
        if (!(value >= 0f && value < 1f)) throw new InvalidOperationException("祈愿随机源返回了无效概率。");
        draws++;
        return value;
    }

    private static RoguelikeWishPrize Pick(IReadOnlyList<RoguelikeWishPrize> choices, IBattleRandomSource rng, ref int draws)
    {
        if (choices.Count == 1) return choices[0];
        draws++;
        return choices[rng.NextInt(0, choices.Count)];
    }

    private RoguelikeWishReward Award(RoguelikeInventoryTransaction draft, RoguelikeWishPrize prize, HashSet<int> weaponTypesOwned)
    {
        int glitter = prize.Stars switch { 4 => rules.FourStarGlitter, 5 => rules.FiveStarGlitter, _ => 0 };
        if (prize.Kind == RoguelikeWishKind.Weapon)
            return new RoguelikeWishReward(prize, weaponTypesOwned.Add(prize.Id),
                weaponInstanceId: draft.AddWeapon(prize.Id), starglitter: glitter,
                stardust: prize.Stars == 3 ? rules.ThreeStarStardust : 0);
        bool owned = draft.TryGetCharacterProgress(prize.Id, out var character);
        string constellation = RoguelikeItemIds.Constellation(prize.Id);
        if (owned && (long)character.ConstellationLevel + draft.GetItemCount(constellation) >= 6)
            return new RoguelikeWishReward(prize, false,
                convertedGlitter: prize.Stars == 5 ? rules.FullFiveStarGlitter : rules.FullFourStarGlitter);
        if (!characters.TryGetInitialLimits(prize.Id, out float health, out float energy))
            throw new InvalidOperationException("找不到角色初始属性：" + prize.Id);
        draft.AddCharacter(prize.Id, health, energy);
        return !owned ? new RoguelikeWishReward(prize, true)
            : new RoguelikeWishReward(prize, false, constellationItemId: constellation, starglitter: glitter);
    }

    private bool HasTemplate(RoguelikeWishPrize prize)
        => prize.Kind == RoguelikeWishKind.Weapon
            ? equipment.TryGetWeapon(prize.Id, out var weapon) && weapon != null && weapon.Star == prize.Stars &&
                weapon.WeaponID == prize.Id && weapon.WeaponType >= 0 && weapon.WeaponType <= 4 &&
                HasItem(RoguelikeItemIds.Weapon(prize.Id), RoguelikeItemType.Weapon)
            : equipment.TryGetCharacterWeaponType(prize.Id, out int type) && type >= 0 && type <= 4 &&
                characters.TryGetInitialLimits(prize.Id, out _, out _) &&
                HasItem(RoguelikeItemIds.Constellation(prize.Id), RoguelikeItemType.Stack);

    private bool HasItem(string id, RoguelikeItemType type)
        => items.TryGetItem(id, out var item) && item != null && item.ItemType == type &&
            item.Stackable == (type == RoguelikeItemType.Stack);
    private static RoguelikeItemStackData Stack(string id, int quantity) => new RoguelikeItemStackData { ItemId = id, Quantity = quantity };
    internal static RoguelikeWishIssue ReadIssue(RoguelikeSaveResult result)
        => result?.ErrorCode == RoguelikeSaveErrorCode.NotFound || result?.ErrorCode == RoguelikeSaveErrorCode.ActiveRunRequired
            ? RoguelikeWishIssue.NoSave : RoguelikeWishIssue.LoadFailed;

    private static RoguelikeSaveResult<T> Failure<T>(RoguelikeSaveResult result)
        => RoguelikeSaveResult<T>.Failure(result.ErrorCode, result.Message, result.Exception);
    private static RoguelikeSaveResult<T> Invalid<T>(string message, Exception error = null)
        => RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.InvalidOperation, message, error);
}
