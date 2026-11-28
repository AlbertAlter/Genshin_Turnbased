using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>独立 UGUI 祈愿页面。场景负责挂载与布局，公共服务负责所有卡池和抽取规则。</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeWishView : MonoBehaviour
{
    public const string GradientPath = "art_assets/UI/Wish/PosterGradient.png";
    public const string EmblemPath = "art_assets/UI/Wish/PaimonEmblem.png";
    public const string ProbabilityIconPath = "art_assets/UI/Wish/WishIcon.png";
    public const string HistoryIconPath = "art_assets/UI/Wish/WishHistoryIcon.png";
    public const string FateIconPath = "art_assets/ItemIcon/670101.png";
    public const string GlitterIconPath = "art_assets/ItemIcon/680101.png";
    private static readonly string[] Nations = { "蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱" };

    [Serializable]
    private struct LocalizedLabel
    {
        public TMP_Text text;
        public string key;
        public string chinese;
        public string english;
    }

    [SerializeField] private GameObject poolPage, resultPage, resultDrawActions;
    [SerializeField] private GameObject poolHeader, skipControls;
    [SerializeField] private Transform poolList, singleResultMount, resultList;
    [SerializeField] private RoguelikeWishTile poolTemplate, singleResultTemplate, resultTemplate;
    [SerializeField] private RoguelikeWishCardStrip featuredFour;
    [SerializeField] private RoguelikeWishInfoView information;
    [SerializeField] private RoguelikeWishDetailView detail;
    [SerializeField] private RoguelikeWishMusic music;
    [SerializeField] private RoguelikeWishArt art;
    [SerializeField] private RectTransform musicControls;
    [SerializeField] private UICardFactory cards;
    [SerializeField] private RectTransform featuredCardMount;
    [SerializeField] private StreamingSpriteReference posterBackground;
    [SerializeField] private TMP_Text title, poolKind, featuredName, featuredStars, featuredType, pity, balance;
    [SerializeField] private TMP_Text resultTitle, resultSubtitle, starglitter, stardust;
    [SerializeField] private GameObject glitterReward, dustReward;
    [SerializeField] private Button once, ten, skip, returnToPools, probability, history, featuredButton;
    [SerializeField] private Button resultOnce, resultTen;
    [SerializeField] private LocalizedLabel[] localizedLabels = Array.Empty<LocalizedLabel>();
    [SerializeField, Min(0f)] private float revealInterval = .15f;
    [SerializeField, Min(0f)] private float cardBackHold = .2f;

    private readonly List<RoguelikeWishTile> tabs = new List<RoguelikeWishTile>();
    private readonly List<RoguelikeWishTile> rewards = new List<RoguelikeWishTile>();
    private readonly RoguelikeWishLanguage language = new RoguelikeWishLanguage();
    private IRoguelikeGameService game;
    private RoguelikeWishService service;
    private RoguelikeWishPage page;
    private RoguelikeWishOffer selection;
    private RoguelikeWishPool resultPool;
    private string selectedId, resultStepId;
    private UICardView featuredCard;
    private UIFloatingTip tip;
    private RoguelikeWishIssue pageIssue;
    private readonly UICardBack cardBack = new UICardBack();
    private Coroutine reveal;
    private bool showingResults;
    private bool submitting;
    private int resultCount;
    public bool Ready => page == null ? game != null && pageIssue != RoguelikeWishIssue.None :
        selection == null || (featuredCard != null && featuredCard.Ready);
#if UNITY_EDITOR
    private Version previewPhase;
    private string liveSelectionId;
    private readonly Dictionary<Version, string> previewSelections = new Dictionary<Version, string>();
    public Version[] DebugPhases => service?.DebugPhases ?? Array.Empty<Version>();
    public Version DebugPhase => previewPhase;
    public bool DebugEnglish => language.English;
    public bool CanChangeDebugPhase => service != null && !showingResults && !submitting && !DetailOpen;

    public void SetDebugPhase(Version phase)
    {
        if (!CanChangeDebugPhase || Equals(previewPhase, phase) || (phase != null && !DebugPhases.Contains(phase))) return;
        if (previewPhase == null) liveSelectionId = selection?.Pool.Id;
        if (selection != null) previewSelections[selection.Pool.Phase] = selection.Pool.Id;
        previewPhase = phase;
        selectedId = phase == null ? liveSelectionId :
            previewSelections.TryGetValue(phase, out var remembered) ? remembered : null;
        information?.Close();
        Refresh();
    }

#endif

    public void Bind(IRoguelikeGameService source, DataManager names = null)
    {
        if (submitting) throw new InvalidOperationException("祈愿提交过程中不能更换页面数据。");
        if (game != null) game.Changed -= OnProgressChanged;
        ClearPresentation();
        game = source;
        service = source?.Wishes;
#if UNITY_EDITOR
        previewPhase = null;
        liveSelectionId = null;
        previewSelections.Clear();
#endif
        language.Names = names;
        if (tip == null) tip = UIFloatingTip.Create((RectTransform)transform, title != null ? title.font : null, names);
        BindAppearance();
        if (game != null && isActiveAndEnabled) game.Changed += OnProgressChanged;
        if (isActiveAndEnabled)
        {
            Refresh();
            RefreshLanguage();
        }
    }

    private void Start() => music?.BuildControls(musicControls, featuredType, art);

    private void OnEnable()
    {
        if (once != null) once.onClick.AddListener(DrawOnce);
        if (ten != null) ten.onClick.AddListener(DrawTen);
        if (resultOnce != null) resultOnce.onClick.AddListener(DrawOnce);
        if (resultTen != null) resultTen.onClick.AddListener(DrawTen);
        if (skip != null) skip.onClick.AddListener(Skip);
        if (returnToPools != null) returnToPools.onClick.AddListener(ReturnToPools);
        if (probability != null) probability.onClick.AddListener(OpenProbability);
        if (history != null) history.onClick.AddListener(OpenHistory);
        if (featuredButton != null) featuredButton.onClick.AddListener(OpenFeatured);
        if (game != null)
        {
            game.Changed += OnProgressChanged;
            BindAppearance();
            Refresh();
        }
        RefreshLanguage();
    }

    private void OnDisable()
    {
        if (game != null) game.Changed -= OnProgressChanged;
        if (once != null) once.onClick.RemoveListener(DrawOnce);
        if (ten != null) ten.onClick.RemoveListener(DrawTen);
        if (resultOnce != null) resultOnce.onClick.RemoveListener(DrawOnce);
        if (resultTen != null) resultTen.onClick.RemoveListener(DrawTen);
        if (skip != null) skip.onClick.RemoveListener(Skip);
        if (returnToPools != null) returnToPools.onClick.RemoveListener(ReturnToPools);
        if (probability != null) probability.onClick.RemoveListener(OpenProbability);
        if (history != null) history.onClick.RemoveListener(OpenHistory);
        if (featuredButton != null) featuredButton.onClick.RemoveListener(OpenFeatured);
        ClearPresentation();
        if (cards != null) cards.BindAppearance(null);
    }

    private void BindAppearance()
    {
        if (cards != null) cards.BindAppearance(game?.Appearance?.IsReady == true ? game.Appearance : null);
    }

    internal void ShowBindingError()
    {
        pageIssue = RoguelikeWishIssue.LoadFailed;
        ShowTip(pageIssue);
    }

    private void OnProgressChanged()
    {
        if (submitting || !isActiveAndEnabled) return;
        if (!showingResults) Refresh();
        else if (reveal == null)
        {
            pageIssue = ReadResultOffer();
            UpdateButtons();
        }
    }

    public void Refresh()
    {
        if (!isActiveAndEnabled || showingResults || submitting) return;
        if (poolPage != null) poolPage.SetActive(true);
        if (resultPage != null) resultPage.SetActive(false);
        if (poolHeader != null) poolHeader.SetActive(true);
        Clear(tabs);
        selection = null;
        page = null;
        var result = ReadPage();
        if (result != null && result.Succeeded)
        {
            page = result.Data;
            foreach (var offer in page.Offers)
            {
                if (poolTemplate == null || poolList == null) break;
                var tab = Spawn(poolTemplate, poolList);
                tab.BindIcon(offer.Pool.Featured, () => Select(offer.Pool.Id));
                tabs.Add(tab);
            }
        }
        var active = page?.Offers.FirstOrDefault(offer => offer.Pool.Id == selectedId) ?? page?.Offers.FirstOrDefault();
        pageIssue = active != null ? RoguelikeWishIssue.None : result?.Succeeded == true
            ? RoguelikeWishIssue.NoBanners : RoguelikeWishService.ReadIssue(result);
        ShowOffer(active);
    }

    private RoguelikeSaveResult<RoguelikeWishPage> ReadPage()
    {
#if UNITY_EDITOR
        if (previewPhase != null) return service?.ReadDebugBanners(previewPhase);
#endif
        return service?.Read();
    }

    public void Select(string wishId)
    {
        if (!isActiveAndEnabled || page == null || showingResults || submitting || DetailOpen) return;
        var offer = page.Offers.FirstOrDefault(item => item.Pool.Id == wishId);
        if (offer != null && offer != selection) ShowOffer(offer);
    }

    private void ShowOffer(RoguelikeWishOffer offer)
    {
        selection = offer;
        selectedId = offer?.Pool.Id ?? selectedId;
        ReleaseFeatured();
        var prize = offer?.Pool.Featured;
        if (music != null && (information == null || !information.isActiveAndEnabled)) music.SetFeatured(prize);
        WriteOfferLabels();
        SetText(balance, (page?.Balance ?? 0).ToString());
        tip?.Hide();
        if (posterBackground != null) posterBackground.SetResolvedPath(BackgroundPath(offer?.Nation));
        if (offer != null && cards != null && cards.isActiveAndEnabled && featuredCardMount != null)
            featuredCard = RoguelikeWishTile.RequestCard(cards, featuredCardMount, prize);
        if (featuredFour != null) featuredFour.Bind(cards, offer?.Pool, language, OpenDetail);
        if (featuredButton != null) featuredButton.interactable = featuredCard != null;
        for (int index = 0; index < tabs.Count; index++) tabs[index].SetSelected(page.Offers[index] == offer);
        UpdateButtons();
    }

    public void DrawOnce() => Draw(1);
    public void DrawTen() => Draw(10);

    private void Draw(int count)
    {
        if (!isActiveAndEnabled || reveal != null || submitting || DetailOpen || information?.isActiveAndEnabled == true)
            return;
        var issue = pageIssue != RoguelikeWishIssue.None ? pageIssue :
            page?.DrawIssue(selection, count) ?? RoguelikeWishIssue.BannerUnavailable;
        if (issue != RoguelikeWishIssue.None)
        {
            ShowTip(issue, count);
            return;
        }
        if (resultPage == null || resultTemplate == null || resultList == null || singleResultTemplate == null ||
            singleResultMount == null || cards == null || !cards.isActiveAndEnabled)
        {
            ShowTip(RoguelikeWishIssue.ResultsUnavailable);
            return;
        }
        submitting = true;
        UpdateButtons();
        try
        {
            var result = service.Draw(page.StepId, selection.Pool.Id, count, out issue);
            submitting = false;
            if (!isActiveAndEnabled) return;
            if (result.Succeeded) ShowBatch(result.Data);
            else
            {
                Debug.LogWarning(result.Message, this);
                if (showingResults)
                {
                    pageIssue = ReadResultOffer();
                    ShowTip(pageIssue == RoguelikeWishIssue.None ? issue : pageIssue, count);
                }
                else
                {
                    Refresh();
                    ShowTip(page == null && pageIssue != RoguelikeWishIssue.None ? pageIssue : issue, count);
                }
            }
        }
        finally
        {
            submitting = false;
            UpdateButtons();
        }
    }

    private void ShowBatch(RoguelikeWishBatch batch)
    {
        showingResults = true;
        resultPool = batch.Pool;
        resultStepId = batch.CommittedStepId;
        resultCount = batch.Rewards.Count;
        CloseDetail();
        if (information != null) information.Close();
        ReleaseFeatured();
        Clear(rewards);
        if (featuredFour != null) featuredFour.Clear();
        Clear(tabs);
        if (poolPage != null) poolPage.SetActive(false);
        if (poolHeader != null) poolHeader.SetActive(false);
        if (glitterReward != null) glitterReward.SetActive(false);
        if (dustReward != null) dustReward.SetActive(false);
        resultPage.SetActive(true);
        if (resultDrawActions != null) resultDrawActions.SetActive(false);
        tip?.Hide();
        pageIssue = RoguelikeWishIssue.None;
        WriteResultTitle();
        SetText(starglitter, $"×{batch.Starglitter}");
        SetText(stardust, batch.Stardust > 0 ? $"×{batch.Stardust}" : null);
        SetText(balance, Math.Max(0, page.Balance - batch.Rewards.Count).ToString());
        bool single = batch.Rewards.Count == 1;
        foreach (var reward in batch.Rewards)
        {
            var tile = Spawn(single ? singleResultTemplate : resultTemplate, single ? singleResultMount : resultList);
            tile.BindResult(cards, reward, language, () => OpenDetail(reward.Prize));
            rewards.Add(tile);
        }
        if (skipControls != null) skipControls.SetActive(true);
        if (returnToPools != null) returnToPools.gameObject.SetActive(false);
        reveal = StartCoroutine(RevealInOrder());
    }

    private IEnumerator RevealInOrder()
    {
        yield return null;
        yield return cardBack.PrepareFromAppearance(game?.Appearance);
        cardBack.TryGetSprite(out var coverSprite);
        foreach (var tile in rewards) tile.SetCover(coverSprite);
        if (coverSprite != null && cardBackHold > 0f) yield return new WaitForSecondsRealtime(cardBackHold);
        foreach (var tile in rewards)
        {
            while (!tile.FrontPrepared) yield return null;
            tile.Reveal(true);
            if (revealInterval > 0f) yield return new WaitForSecondsRealtime(revealInterval);
        }
        while (rewards.Any(tile => tile != null && tile.Revealing)) yield return null;
        reveal = null;
        ShowReturn();
    }

    public void Skip()
    {
        if (!showingResults || reveal == null || DetailOpen) return;
        StopReveal();
        foreach (var tile in rewards) tile.Reveal(false);
        cardBack.Release();
        ShowReturn();
    }

    public void ReturnToPools()
    {
        if (!showingResults || reveal != null || DetailOpen) return;
        CloseDetail();
        showingResults = false;
        resultPool = null;
        resultStepId = null;
        Clear(rewards);
        Refresh();
    }

    private void ShowReturn()
    {
        if (skipControls != null) skipControls.SetActive(false);
        if (glitterReward != null) glitterReward.SetActive(true);
        if (dustReward != null) dustReward.SetActive(stardust != null && stardust.text.Length > 0);
        pageIssue = ReadResultOffer();
        if (returnToPools != null) returnToPools.gameObject.SetActive(true);
        if (resultDrawActions != null) resultDrawActions.SetActive(true);
        UpdateButtons();
    }

    private RoguelikeWishIssue ReadResultOffer()
    {
        var latest = ReadPage();
        page = latest?.Succeeded == true && latest.Data.StepId == resultStepId ? latest.Data : null;
        selection = page?.Offers.FirstOrDefault(offer => offer.Pool.Id == resultPool?.Id);
        if (latest?.Succeeded == true) SetText(balance, latest.Data.Balance.ToString());
        if (latest?.Succeeded != true) return RoguelikeWishService.ReadIssue(latest);
        if (page == null || selection == null) return RoguelikeWishIssue.StaleResult;
        return RoguelikeWishIssue.None;
    }

    private void UpdateButtons()
    {
        bool ready = !submitting && !showingResults;
        // A blocked draw can still explain the restriction when selected.
        if (once != null) once.interactable = ready;
        if (ten != null) ten.interactable = ready;
        bool resultReady = !submitting && showingResults && reveal == null;
        if (resultOnce != null) resultOnce.interactable = resultReady;
        if (resultTen != null) resultTen.interactable = resultReady;
        if (probability != null) probability.interactable = ready && selection != null;
        if (history != null) history.interactable = !submitting && !showingResults && service != null && !IsDebugPhase;
    }

    private void ClearPresentation()
    {
        tip?.Hide();
        StopReveal();
        if (skipControls != null) skipControls.SetActive(false);
        if (music != null) music.Pause();
        CloseDetail();
        if (information != null) information.Close();
        ReleaseFeatured();
        Clear(tabs);
        if (featuredFour != null) featuredFour.Clear();
        Clear(rewards);
        cardBack.Release();
        showingResults = false;
        resultPool = null;
        resultStepId = null;
        page = null;
        selection = null;
    }

    private void StopReveal()
    {
        if (reveal != null) StopCoroutine(reveal);
        reveal = null;
    }

    private void ReleaseFeatured()
    {
        if (cards != null && featuredCard != null) cards.Release(featuredCard);
        featuredCard = null;
    }

    private void OnDestroy() => cardBack.Dispose();

    internal static RoguelikeWishTile Spawn(RoguelikeWishTile template, Transform parent)
    {
        var tile = Instantiate(template, parent, false);
        tile.gameObject.SetActive(true);
        return tile;
    }

    internal static void Clear(List<RoguelikeWishTile> tiles)
    {
        foreach (var tile in tiles)
            if (tile != null) { tile.gameObject.SetActive(false); Destroy(tile.gameObject); }
        tiles.Clear();
    }

    internal static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
    public void SetEnglish(bool english)
    {
        if (language.English == english) return;
        language.English = english;
        if (isActiveAndEnabled) RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        foreach (var label in localizedLabels) SetText(label.text, language.Label(label.key, label.chinese, label.english));
        WriteOfferLabels();
        WriteResultTitle();
        tip?.Hide();
        if (featuredFour != null) featuredFour.RefreshLanguage(language);
        if (information != null && information.isActiveAndEnabled) information.RefreshLanguage();
        foreach (var tile in rewards) tile.RefreshLanguage(language);
        if (detail != null) detail.RefreshLanguage();
    }

    private void ShowTip(RoguelikeWishIssue issue, int count = 1)
    {
        if (tip == null)
            tip = UIFloatingTip.Create((RectTransform)transform, title != null ? title.font : null, language.Names);
        tip.Show(language.DrawFeedback(issue, page, count));
    }

    private void WriteOfferLabels()
    {
        var pool = selection?.Pool;
        var prize = pool?.Featured;
        SetText(title, pool == null ? null : language.PoolTitle(pool));
        SetText(poolKind, prize == null ? null : prize.Kind == RoguelikeWishKind.Character
            ? language.Label("ui_wish_character_event", "★ 角色活动祈愿", "★ Character Event Wish")
            : language.Label("ui_wish_weapon_event", "★ 武器活动祈愿", "★ Weapon Event Wish"));
        SetText(featuredName, prize == null ? null : language.Name(prize));
        SetText(featuredStars, prize == null ? null : new string('★', prize.Stars));
        SetText(featuredType, FeaturedType(selection));
        SetText(pity, language.Pity(selection, page?.NodesUntilNextPool));
    }

    private void WriteResultTitle()
    {
        if (resultPool == null) return;
        string banner = resultPool.Featured.Kind == RoguelikeWishKind.Character
            ? language.PoolTitle(resultPool) : language.Name(resultPool.Featured);
        string count = resultCount == 1 ? language.Label("ui_wish_single", "单次祈愿", "Single Wish")
            : language.Label("ui_wish_ten", "十次祈愿", "Ten Wishes");
        SetText(resultTitle, language.Label("ui_wish_results", "祈愿结果", "Wish Results"));
        SetText(resultSubtitle, language.Label("ui_wish_result_subtitle", "{0} · {1}", "{0} · {1}", banner, count));
    }

    private string FeaturedType(RoguelikeWishOffer offer)
    {
        if (offer == null) return null;
        string weapon = language.WeaponType(offer.WeaponType);
        string element = language.Element(offer.Element);
        return element.Length == 0 ? weapon : weapon.Length == 0 ? element : element + " · " + weapon;
    }

    private void OpenProbability()
    {
        if (!DetailOpen && selection != null && service != null && information != null)
        {
            tip?.Hide();
            music?.Pause();
            information.ShowProbability(service.Rules, page.Offers, selection, cards, poolTemplate, language, OpenDetail,
                SelectProbabilityOffer, ResumeTheme);
        }
    }

    private void OpenHistory()
    {
        if (!DetailOpen && !IsDebugPhase && service != null && information != null)
        {
            tip?.Hide();
            music?.Pause();
            information.ShowHistory(service, language, OpenDetail, ResumeTheme);
        }
    }

    private void SelectProbabilityOffer(string id)
    {
        Select(id);
        if (selection != null) information.SelectProbabilityOffer(selection);
    }

    private void ResumeTheme() => music?.SetFeatured(selection?.Pool.Featured);

    private bool IsDebugPhase
    {
        get
        {
#if UNITY_EDITOR
            return previewPhase != null;
#else
            return false;
#endif
        }
    }

    public bool ShowPageExit => !DetailOpen && (information == null || !information.isActiveAndEnabled) &&
        reveal == null && !showingResults;

    public bool TryNavigateBack()
    {
        if (submitting) return true;
        if (DetailOpen) CloseDetail();
        else if (information != null && information.isActiveAndEnabled) information.CloseAndResume();
        else if (reveal != null) Skip();
        else if (showingResults) ReturnToPools();
        else return false;
        return true;
    }

    private void OpenFeatured() => OpenDetail(selection?.Pool.Featured);

    private void OpenDetail(RoguelikeWishPrize prize)
    {
        if (isActiveAndEnabled && prize != null && cards != null && cards.isActiveAndEnabled && detail != null)
        {
            tip?.Hide();
            detail.Open(cards, prize, language);
        }
    }

    private bool DetailOpen => detail != null && detail.IsOpen;
    private void CloseDetail() { if (detail != null) detail.Close(); }

    internal static string BackgroundPath(string nation)
    {
        int index = Array.IndexOf(Nations, nation);
        return index < 0 ? null : $"art_assets/BattleBG/{index + 1}_BG.png";
    }
}
