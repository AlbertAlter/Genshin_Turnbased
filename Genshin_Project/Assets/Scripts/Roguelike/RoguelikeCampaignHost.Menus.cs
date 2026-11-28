using System;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed partial class RoguelikeCampaignHost
{
    private TMP_Text appearanceCaption;
    private void Menu()
    {
        menu = true;
        var saved = Game.ReadActiveSummary();
        string summary = saved.Succeeded ? L("btn_main_chapter", saved.Data.ChapterId) + " · " +
            L("ui_main_current_floor", saved.Data.StageIndex + 1) + " · " + SavedTime(saved.Data.UpdatedAt) : "";
        screen.Menu(summary);
        if (saved.Succeeded) screen.Choice(L("btn_main_continue"), () => { menu = false; Progress(); });
        screen.Choice(L("btn_main_new_game"), () =>
        {
            var initial = RoguelikeStartingLoadout.Load(DataManager.Instance);
            BeginRun(id => Check(Game.ReplaceWithNewGame(initial, id)));
        });
        screen.Choice(L("btn_main_start_from_chapter"), Chapters);
        screen.Choice(L("btn_main_exit"), ExitGame);
        var appearance = Game.Appearance.Read();
        string appearanceTitle = appearance.Succeeded && appearance.Data.AvailableCardBackChoices > 0
            ? L("btn_main_appearance_choices", appearance.Data.AvailableCardBackChoices) : L("btn_main_appearance");
        appearanceCaption = screen.Button(screen.Root, appearanceTitle, new Vector2(0, 0), 44, 36, 190, 54, Appearance).GetComponentInChildren<TMP_Text>();
        screen.Button(screen.Root, L("btn_settings_open"), new Vector2(1, 0), -44, 36, 190, 54, OpenSettings);
        if (!saved.Succeeded && saved.ErrorCode != RoguelikeSaveErrorCode.NotFound && saved.ErrorCode != RoguelikeSaveErrorCode.ActiveRunRequired)
        {
            screen.Notice(saved.Message);
            if (saved.ErrorCode == RoguelikeSaveErrorCode.BackupAvailable)
                screen.Choice(L("btn_main_recover_active_backup"), () => Confirm("ui_main_restore_backup_title", "ui_main_restore_backup_body",
                    "btn_main_recover_backup", () => { Check(Game.RestoreActiveBackup(true)); Menu(); }, screen.CloseWindow));
        }
    }

    private void Appearance()
    {
        var body = screen.Window(L("ui_appearance_title"), "", screen.CloseWindow, true);
        var gallery = body.gameObject.AddComponent<UICardBackSelection>();
        gallery.Bind(body, Game.Appearance, font, framePrefab, english, count =>
        {
            if (appearanceCaption != null) appearanceCaption.text = count > 0 ? L("btn_main_appearance_choices", count) : L("btn_main_appearance");
        });
    }

    private void OpenSettings()
    {
        if (settings?.IsOpen == true) return;
        float? mapPosition = screen.MapScrollPosition;
        screen.MapScrollPosition = mapPosition; // 停止路线滚动惯性，覆盖层关闭后保留原位置。
        settings = ProjectSettingsOverlay.Open(settingsPrefab, (RectTransform)screen.Root.parent, () =>
        {
            settings = null;
            bool nextEnglish = ProjectUserSettings.English(english);
            if (nextEnglish == english) return;
            english = nextEnglish;
            if (menu) Menu(); else Progress();
            screen.MapScrollPosition = mapPosition;
        });
    }

    private string SavedTime(string value) => DateTimeOffset.TryParse(value, out var time)
        ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : L("ui_main_unknown_save_time");
    private void Confirm(string title, string body, string confirm, Action yes, Action no, params object[] arguments)
        => screen.Confirmation(L(title), L(body, arguments), L(confirm), L("btn_cancel"), yes, no);

    private void BeginRun(Action<string> start, Action cancelled = null)
    {
        void Start(string expected) { start(expected); menu = false; Progress(); }
        var active = Game.ReadActiveSummary();
        if (active.Succeeded) Confirm("ui_main_discard_active_title", "ui_main_discard_active_body", "btn_main_discard_and_start",
            () => Start(active.Data.RunId), cancelled ?? screen.CloseWindow);
        else if (active.ErrorCode == RoguelikeSaveErrorCode.NotFound || active.ErrorCode == RoguelikeSaveErrorCode.ActiveRunRequired) Start(null);
        else Check(active);
    }

    private void Chapters()
    {
        var body = screen.Window(L("ui_main_select_start_chapter"), "", screen.CloseWindow);
        for (int chapter = 2; chapter <= 7; chapter++)
        {
            int number = chapter;
            screen.Button(body, L("btn_main_chapter", number), new Vector2(0, 1), (number - 2) * 160, -70, 148, 80, () => Slots(false, number));
        }
    }

    private void Slots(bool writing, int chapter)
    {
        var result = Game.ListChapterSlots(chapter); Check(result);
        var body = screen.Window(L(writing ? "ui_main_save_clear_title" : "ui_main_start_chapter_title", writing ? chapter - 1 : chapter),
            L(writing ? "ui_main_clear_unlocks_chapter" : "ui_main_select_previous_clear", writing ? chapter : chapter - 1),
            writing ? (Action)Menu : Chapters);
        string step = writing ? Read().Flow.StepId : null;
        foreach (var slot in result.Data)
        {
            void Save()
            {
                Check(Game.SaveCompletedChapterToSlot(step, slot.SlotIndex, slot.RequiresOverwriteConfirmation));
                Menu(); screen.Notice(L("ui_main_clear_saved", chapter - 1, chapter));
            }
            void Select()
            {
                if (writing)
                {
                    if (slot.RequiresOverwriteConfirmation) Confirm("ui_main_overwrite_clear_title", "ui_main_overwrite_clear_body",
                        "btn_main_confirm_overwrite", Save, () => Slots(true, chapter), chapter - 1, slot.SlotIndex);
                    else Save();
                }
                else
                {
                    if (!routes.HasChapter(chapter)) throw new InvalidOperationException(L("tip_main_chapter_unavailable", chapter));
                    Check(Game.ReadChapterSlot(chapter, slot.SlotIndex));
                    BeginRun(id => Check(Game.ReplaceWithChapterSlot(chapter, slot.SlotIndex, id)), () => Slots(false, chapter));
                }
            }
            string summary = slot.Summary != null ? L("ui_main_clear_saved_at", SavedTime(slot.Summary.UpdatedAt)) :
                L(slot.Exists ? "tip_main_clear_unreadable" : "ui_main_clear_empty");
            if (slot.CanRestoreBackup) summary = L("ui_main_backup_label") + " · " + summary;
            screen.Slot(body, slot.SlotIndex, L("ui_main_clear_slot", slot.SlotIndex), summary,
                slot.Summary?.PartyCharacterIds?.ToArray() ?? Array.Empty<int>(),
                L(writing ? slot.Exists ? "btn_main_overwrite_save" : "btn_main_save_here" : "btn_main_start_chapter", chapter),
                writing || slot.CanLoad ? Select : null,
                L("btn_main_archive_details"), slot.CanLoad ? () => Archive(chapter, slot.SlotIndex, writing) : null,
                L("btn_main_recover_backup"), slot.CanRestoreBackup ? () => Confirm("ui_main_restore_backup_title", "ui_main_restore_backup_body",
                    "btn_main_recover_backup", () => { Check(Game.RestoreChapterBackup(chapter, slot.SlotIndex, true)); Slots(writing, chapter); },
                    () => Slots(writing, chapter)) : null);
        }
    }

    private void Archive(int chapter, int slot, bool writing)
    {
        var saved = Game.ReadChapterSlot(chapter, slot); Check(saved);
        var body = screen.Window(L("ui_main_archive_title", chapter - 1, slot), "", () => Slots(writing, chapter), true);
        var mount = RoguelikeCampaignScreen.Rect("Read-only archive", body);
        RoguelikeCampaignScreen.Place(mount, Vector2.one * .5f, 0, 0, 1920, 1080);
        mount.localScale = Vector3.one * .63f;
        var cards = mount.gameObject.AddComponent<UICardFactory>();
        if (Game.Appearance.IsReady) cards.BindAppearance(Game.Appearance);
        var view = mount.gameObject.AddComponent<RoguelikeRosterView>();
        view.SetEnglish(english);
        view.Bind(mount, new RoguelikeRosterSession(DataManager.Instance, Game.Flow, new RoguelikeSavedUpgradeGate(), saved.Data.Snapshot), cards, font, framePrefab);
        view.ShowCharacterList();
        screen.SetModalChildBack(view.TryNavigateBack);
        screen.Button(body, L("btn_characters"), new Vector2(0, 1), 0, 55, 180, 50, view.ShowCharacterList);
        screen.Button(body, L("btn_inventory"), new Vector2(0, 1), 194, 55, 180, 50, view.ShowInventory);
    }

    private void ExitOptions()
    {
        var context = Read();
        var body = screen.Window(L("ui_main_exit_options"), "", screen.CloseWindow);
        screen.Button(body, L("btn_main_restart_chapter"), Vector2.one * .5f, 0, 185, 430, 64,
            () => Confirm("ui_main_restart_chapter_title", "ui_main_restart_chapter_body", "btn_main_restart",
                () => { Check(Game.RestartChapter(context.RunId, RoguelikeStartingLoadout.Load(DataManager.Instance))); Progress(); }, ExitOptions));
        screen.Button(body, L("btn_main_suspend"), Vector2.one * .5f, 0, 100, 430, 64,
            () => { Check(Game.SaveCurrentProgress()); Menu(); }, true);
        screen.Button(body, L("btn_main_abandon_chapter"), Vector2.one * .5f, 0, 15, 430, 64,
            () => Confirm("ui_main_abandon_chapter_title", "ui_main_abandon_chapter_body", "btn_main_confirm_discard",
                () => { Check(Game.Abandon(context.RunId, true)); Menu(); }, ExitOptions));
        screen.Button(body, L("btn_cancel"), Vector2.one * .5f, 0, -70, 430, 64, screen.CloseWindow);
    }

    private void ExitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
