using System.Linq;

internal sealed class RoguelikeWishLanguage
{
    internal bool English { get; set; }
    internal DataManager Names { get; set; }

    internal string Text(string chinese, string english) => English ? english : chinese;

    internal string Label(string key, string chinese, string english, params object[] arguments)
    {
        if (!string.IsNullOrEmpty(key) && Names?.LangDict.ContainsKey(key) == true)
            return Names.GetLangText(key, English, arguments);
        return LangTextFormatter.Format(Text(chinese, english), arguments, context: key);
    }

    internal string WeaponType(int type) => type switch
    {
        0 => Label("ui_weapon_type_sword", "单手剑", "Sword"),
        1 => Label("ui_weapon_type_claymore", "双手剑", "Claymore"),
        2 => Label("ui_weapon_type_bow", "弓", "Bow"),
        3 => Label("ui_weapon_type_polearm", "长柄武器", "Polearm"),
        4 => Label("ui_weapon_type_catalyst", "法器", "Catalyst"),
        _ => ""
    };

    internal string Element(string element) => element switch
    {
        "Pyro" => Label("ui_element_pyro", "#1火元素#", "#1Pyro#"),
        "Hydro" => Label("ui_element_hydro", "#2水元素#", "#2Hydro#"),
        "Anemo" => Label("ui_element_anemo", "#5风元素#", "#5Anemo#"),
        "Electro" => Label("ui_element_electro", "#3雷元素#", "#3Electro#"),
        "Dendro" => Label("ui_element_dendro", "#7草元素#", "#7Dendro#"),
        "Cryo" => Label("ui_element_cryo", "#4冰元素#", "#4Cryo#"),
        "Geo" => Label("ui_element_geo", "#6岩元素#", "#6Geo#"),
        _ => ""
    };

    internal string Nation(string nation) => nation switch
    {
        "蒙德" => Label("ui_nation_mondstadt", "蒙德", "Mondstadt"),
        "璃月" => Label("ui_nation_liyue", "璃月", "Liyue"),
        "稻妻" => Label("ui_nation_inazuma", "稻妻", "Inazuma"),
        "须弥" => Label("ui_nation_sumeru", "须弥", "Sumeru"),
        "枫丹" => Label("ui_nation_fontaine", "枫丹", "Fontaine"),
        "纳塔" => Label("ui_nation_natlan", "纳塔", "Natlan"),
        "挪德卡莱" => Label("ui_nation_nod_krai", "挪德卡莱", "Nod-Krai"),
        "至冬" => Label("ui_nation_snezhnaya", "至冬", "Snezhnaya"),
        _ => nation
    };

    internal string Name(RoguelikeWishPrize prize) => Name(prize.Kind, prize.Id, prize.Name);

    internal string Name(RoguelikeWishKind kind, int id, string fallback)
    {
        var nameKind = kind == RoguelikeWishKind.Character ? UiNameKind.Character : UiNameKind.Weapon;
        string name = Names?.GetUiName(nameKind, id, fallback, English);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    internal string PoolTitle(RoguelikeWishPool pool) => PoolTitle(pool.Id, pool.Title);

    internal string PoolTitle(string id, string title)
    {
        string key = $"wish_{id?.ToLowerInvariant()}_title";
        if (Names?.LangDict.ContainsKey(key) == true) return Names.GetLangText(key, English);
        if (title == "神铸赋形") return Label("ui_wish_weapon_title", "神铸赋形", "Epitome Invocation");
        if (!English) return title;
        return title switch
        {
            "杯装之诗" => "Ballad in Goblets",
            "闪焰的驻足" => "Sparkling Steps",
            _ => title
        };
    }

    internal string DrawFeedback(RoguelikeWishIssue issue, RoguelikeWishPage page, int count) => issue switch
    {
        RoguelikeWishIssue.NoSave => Label("tip_wish_no_save", "当前没有进行中的肉鸽存档。", "No active roguelike save."),
        RoguelikeWishIssue.LoadFailed => Label("tip_wish_load_failed", "祈愿加载失败。", "Could not load wishes."),
        RoguelikeWishIssue.NoBanners => Label("ui_wish_no_banners", "当前没有开放的卡池。", "No banners are available."),
        RoguelikeWishIssue.BannerUnavailable => Label("tip_wish_banner_unavailable", "当前卡池暂不可用。", "This banner is currently unavailable."),
        RoguelikeWishIssue.ReadOnly => Label("tip_wish_readonly", "当前为只读卡池。", "This banner is read-only."),
        RoguelikeWishIssue.StepUnavailable => Label("tip_wish_step_unavailable", "当前流程阶段无法祈愿。", "Wishing is unavailable at this stage."),
        RoguelikeWishIssue.ItemDataMissing => Label("tip_wish_item_data_missing", "祈愿道具配置缺失，暂无法祈愿。", "Wish item data is missing. Wishing is unavailable."),
        RoguelikeWishIssue.ThreeStarPoolMissing => Label("tip_wish_three_star_pool_missing", "当前没有可发放的三星武器，暂无法祈愿。", "No 3-star weapons are available as rewards."),
        RoguelikeWishIssue.RewardDataMissing => Label("tip_wish_reward_data_missing", "当前卡池的奖励配置不完整，暂无法祈愿。", "Reward data for this banner is incomplete."),
        RoguelikeWishIssue.InvalidCount => Label("tip_wish_invalid_count", "请选择单次祈愿或十次祈愿。", "Choose one wish or ten wishes."),
        RoguelikeWishIssue.InsufficientFates => Label("tip_wish_insufficient_fates", "纠缠之缘不足。需要 {0} 个，当前有 {1} 个。",
            "Not enough Intertwined Fate. Required: {0}. Owned: {1}.", count, page?.Balance ?? 0),
        RoguelikeWishIssue.RetryRequired => RetryFeedback(page),
        RoguelikeWishIssue.Submitting => Label("tip_wish_submitting", "祈愿正在提交，请稍候。", "Submitting your wish. Please wait."),
        RoguelikeWishIssue.StalePage => Label("tip_wish_stale_page", "祈愿进度已更新，请重新确认后再祈愿。", "Wish progress changed. Review the banner and try again."),
        RoguelikeWishIssue.StaleResult => Label("tip_wish_stale_result", "祈愿进度已更新，请返回卡池继续。", "Wish progress changed. Return to the banners to continue."),
        RoguelikeWishIssue.SaveFailed => Label("tip_wish_save_failed", "祈愿保存失败，请在原卡池重试相同次数。", "Could not save the wish. Retry the same banner and wish count."),
        RoguelikeWishIssue.DrawFailed => Label("tip_wish_draw_failed", "祈愿未能完成。", "The wish could not be completed."),
        RoguelikeWishIssue.ResultsUnavailable => Label("tip_wish_results_unavailable", "祈愿结果暂不可用。", "The wish results view is unavailable."),
        _ => null
    };

    private string RetryFeedback(RoguelikeWishPage page)
    {
        var pool = page?.Offers.FirstOrDefault(offer => offer.Pool.Id == page.RetryPool)?.Pool;
        if (pool == null)
            return Label("tip_wish_retry_required", "有一笔祈愿尚未保存，请在原卡池重试相同次数。",
                "A wish is awaiting save. Retry the same banner and wish count.");
        string banner = pool.Featured.Kind == RoguelikeWishKind.Weapon ? Name(pool.Featured) : PoolTitle(pool);
        return Label("tip_wish_retry_banner", "有一笔祈愿尚未保存，请在「{0}」重试 {1} 抽。",
            "A wish is awaiting save. Retry {1} wishes on “{0}”.", banner, page.RetryCount);
    }

    internal string Pity(RoguelikeWishOffer offer, int? nodesUntilNextPool)
    {
        if (offer == null) return null;
        bool character = offer.Pool.Featured.Kind == RoguelikeWishKind.Character;
        string kind = character ? Label("ui_wish_prize_character", "角色", "character")
            : Label("ui_wish_prize_weapon", "武器", "weapon");
        string pity = Label("ui_wish_pity", "距离 5★{0}保底还剩 {1} 抽\n距离 4★ 保底还剩 {2} 抽，遇 5★ 顺延",
            "5★ {0} guaranteed within {1} wishes\n4★ pity in {2} wishes. A 5★ delays it.", kind,
            offer.UntilFive, offer.UntilFour);
        if (!nodesUntilNextPool.HasValue) return pity;
        return pity + "\n" + Label("ui_wish_next_pool_nodes", "距下个卡池还剩{0}个节点",
            "Nodes until the next banner · {0}", nodesUntilNextPool.Value);
    }
}
