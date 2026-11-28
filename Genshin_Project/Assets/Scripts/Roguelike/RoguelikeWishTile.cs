using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>公用祈愿图格。Prefab 选择用作侧栏、当期名单或结果，并保存各层布局。</summary>
public sealed class RoguelikeWishTile : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private StreamingSpriteReference icon;
    [SerializeField, FormerlySerializedAs("rarityBackdrop")] private StreamingSpriteReference weaponBackdrop;
    [SerializeField] private Graphic selection, selectionMark;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private RectTransform cardMount;
    [SerializeField] private RoguelikeWishCardScatter cover;
    private UICardFactory factory;
    private UICardView card;
    private Action selected;
    private RoguelikeWishPrize displayedPrize;
    private RoguelikeWishReward displayedReward;
    private bool hasCover;

    public const string WeaponIconFolder = "art_assets/Weapon_Icon/";

    public static string WeaponBackdropPath(int stars) => UICardFactory.WeaponArtFolder + "Background/" +
        (stars == 5 ? "5_Gold.png" : stars == 4 ? "4_Purple.png" : "3_Blue.png");

    internal static string IconPath(RoguelikeWishKind kind, int id) => kind == RoguelikeWishKind.Weapon
        ? $"{WeaponIconFolder}{id}_01.png"
        : "art_assets/" + CharacterCardArtManifest.PortraitFile(id);

    internal void BindIcon(RoguelikeWishPrize prize, Action onSelected)
    {
        bool weapon = prize.Kind == RoguelikeWishKind.Weapon;
        if (icon != null) icon.SetResolvedPath(IconPath(prize.Kind, prize.Id));
        if (weaponBackdrop != null) weaponBackdrop.SetResolvedPath(weapon ? WeaponBackdropPath(prize.Stars) : null);
        selected = onSelected;
        if (button != null)
        {
            button.interactable = selected != null;
            button.onClick.AddListener(Select);
        }
    }

    internal void SetSelected(bool value)
    {
        if (selection != null) selection.enabled = value;
        if (selectionMark != null) selectionMark.gameObject.SetActive(value);
    }

    internal void BindCard(UICardFactory cardFactory, RoguelikeWishPrize prize, RoguelikeWishLanguage language, Action onSelected)
    {
        factory = cardFactory;
        if (cardMount != null) card = RequestCard(factory, cardMount, prize);
        displayedPrize = prize;
        RefreshLanguage(language);
        selected = onSelected;
        if (button != null)
        {
            button.interactable = selected != null;
            button.onClick.AddListener(Select);
        }
    }

    internal void BindResult(UICardFactory cardFactory, RoguelikeWishReward reward, RoguelikeWishLanguage language, Action onSelected)
    {
        displayedReward = reward;
        BindCard(cardFactory, reward.Prize, language, onSelected);
        if (button != null) button.interactable = false;
        if (caption != null) caption.enabled = false;
        if (card != null) card.gameObject.SetActive(false);
        if (cover != null) cover.gameObject.SetActive(true);
    }

    internal void RefreshLanguage(RoguelikeWishLanguage language)
    {
        if (caption == null || displayedPrize == null) return;
        var reward = displayedReward;
        if (reward == null) caption.text = language.Name(displayedPrize);
        else if (reward.IsNew) caption.text = language.Label("ui_wish_new_reward", "NEW", "NEW");
        else if (reward.ConstellationItemId != null)
            caption.text = language.Label("ui_wish_stella_reward", "命之座 ×{0}", "Stella Fortuna ×{0}", 1);
        else if (reward.ConvertedGlitter > 0)
            caption.text = language.Label("ui_wish_glitter_reward", "星辉 ×{0}", "Starglitter ×{0}", reward.ConvertedGlitter);
        else caption.text = string.Empty;
    }

    internal void SetCover(Sprite sprite)
    {
        if (cover == null) return;
        hasCover = sprite != null;
        cover.SetCover(sprite);
        cover.gameObject.SetActive(hasCover);
    }

    internal void Reveal(bool animate)
    {
        // The back sits above the complete front. Dissolving it uncovers both art and frame.
        if (card != null) card.gameObject.SetActive(true);
        if (animate && hasCover && cover != null && cover.Play(displayedPrize.Stars, ShowFront)) return;
        ShowFront();
    }

    internal bool Revealing => cover != null && cover.IsPlaying;
    internal bool FrontPrepared => card == null || card.Ready || card.Error != null;

    private void ShowFront()
    {
        if (cover != null)
        {
            cover.gameObject.SetActive(false);
            cover.SetCover(null);
        }
        hasCover = false;
        if (caption != null) caption.enabled = !string.IsNullOrEmpty(caption.text);
        if (button != null) button.interactable = selected != null;
    }

    private void Select() => selected?.Invoke();

    internal static UICardView RequestCard(UICardFactory factory, RectTransform mount, RoguelikeWishPrize prize)
        => prize.Kind == RoguelikeWishKind.Weapon ? factory.RequestWeapon(mount, prize.Id)
            : factory.Request(mount, BattleSide.Ally, prize.Id);

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Select);
        if (factory != null && card != null) factory.Release(card);
    }
}
