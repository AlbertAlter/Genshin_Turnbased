using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared cosmetic gallery. Uses the global appearance transaction, never edits the current run.</summary>
public sealed class UICardBackSelection : MonoBehaviour
{
    private PlayerAppearanceService appearance;
    private RoguelikeCampaignScreen view;
    private bool english;
    private int page;
    private Action<int> choicesChanged;
    private const int PageSize = 12;
    private string L(string key, params object[] args) => DataManager.Instance.GetLangText(key, english, args);

    public void Bind(RectTransform mount, PlayerAppearanceService service, TMP_FontAsset font, UIAdaptiveFrame frame, bool useEnglish, Action<int> onChoicesChanged = null)
    {
        appearance = service; english = useEnglish;
        choicesChanged = onChoicesChanged;
        view = new RoguelikeCampaignScreen(mount, font, Dispatch, frame);
        Draw();
    }
    private void Dispatch(Action action)
    {
        try { action(); }
        catch (Exception error) { view.Notice(error.Message); }
    }
    private void Commit(RoguelikeSaveResult result)
    { if (!result.Succeeded) { Debug.LogWarning(result.Message, this); view.Notice(L("tip_appearance_operation_failed")); return; } Draw(); }
    private void Draw()
    {
        var loaded = appearance.Read(); if (!loaded.Succeeded) throw new InvalidOperationException(loaded.Message);
        var data = loaded.Data; var all = appearance.GetAvailableCardBacks();
        choicesChanged?.Invoke(data.AvailableCardBackChoices);
        int pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize); page = Mathf.Clamp(page, 0, pages - 1);
        view.Clear(); var root = view.Root;
        var heading = RoguelikeCampaignScreen.Rect("Card back choices", root);
        RoguelikeCampaignScreen.Place(heading, new Vector2(0, 1), 20, -4, 950, 52);
        view.Label(heading, L("ui_appearance_choices", data.AvailableCardBackChoices), 27, TextAlignmentOptions.MidlineLeft);
        view.Button(root, L("btn_appearance_default"), Vector2.one, -12, -4, 220, 48, () => Commit(appearance.SelectCardBack("")));
        var hint = RoguelikeCampaignScreen.Rect("Unlock rule", root);
        RoguelikeCampaignScreen.Place(hint, new Vector2(0, 1), 20, -58, 1200, 34);
        view.Label(hint, L("ui_appearance_unlock_rule"), 21, TextAlignmentOptions.MidlineLeft);
        for (int index = page * PageSize; index < Math.Min(all.Count, (page + 1) * PageSize); index++)
        {
            var card = all[index]; int slot = index % PageSize;
            bool unlocked = card.Id == CardBackCatalog.DefaultId || data.UnlockedCardBackIds.Contains(card.Id);
            bool selected = data.SelectedCardBackId == card.Id || string.IsNullOrEmpty(data.SelectedCardBackId) && card.Id == CardBackCatalog.DefaultId;
            var tile = RoguelikeCampaignScreen.Rect(card.Id, root);
            RoguelikeCampaignScreen.Place(tile, new Vector2((slot % 6 + .5f) / 6, 1), 0, -110 - slot / 6 * 260, 164, 260);
            tile.pivot = new Vector2(.5f, 1);
            var imageRect = RoguelikeCampaignScreen.Rect("Card back", tile);
            RoguelikeCampaignScreen.Place(imageRect, new Vector2(.5f, 1), 0, 0, 124, 190); imageRect.pivot = new Vector2(.5f, 1);
            var image = imageRect.gameObject.AddComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
            image.color = unlocked ? Color.white : new Color(.65f, .65f, .65f, .8f);
            var art = imageRect.gameObject.AddComponent<StreamingSpriteReference>(); art.SetResolvedPath(card.RelativePath);
            var name = RoguelikeCampaignScreen.Rect("Name", tile);
            RoguelikeCampaignScreen.Place(name, new Vector2(.5f, 1), 0, -192, 160, 26); name.pivot = new Vector2(.5f, 1);
            view.Label(name, L("ui_appearance_card_back_name", index + 1), 17);
            string actionKey = selected ? "ui_appearance_equipped" : unlocked ? "btn_appearance_use" : "btn_appearance_unlock";
            var button = view.Button(tile, L(actionKey), new Vector2(.5f, 1), 0, -222, 158, 36,
                () => Commit(unlocked ? appearance.SelectCardBack(card.Id) : appearance.UnlockCardBack(card.Id)), selected);
            button.GetComponent<RectTransform>().pivot = new Vector2(.5f, 1);
            button.interactable = !selected && (unlocked || data.AvailableCardBackChoices > 0);
        }
        view.Button(root, "‹", new Vector2(.5f, 0), -150, 4, 80, 44, () => { page--; Draw(); }).interactable = page > 0;
        var count = RoguelikeCampaignScreen.Rect("Page", root); RoguelikeCampaignScreen.Place(count, new Vector2(.5f, 0), 0, 4, 150, 44);
        view.Label(count, (page + 1) + " / " + pages, 23);
        view.Button(root, "›", new Vector2(.5f, 0), 150, 4, 80, 44, () => { page++; Draw(); }).interactable = page + 1 < pages;
    }
}
