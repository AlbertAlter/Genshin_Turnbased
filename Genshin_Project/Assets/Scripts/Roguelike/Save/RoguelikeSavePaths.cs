using System;
using System.IO;
using UnityEngine;

public sealed class RoguelikeSavePaths
{
    public const string ProjectSaveDirectoryName = "SaveData";
    public const string SaveDirectoryName = "RoguelikeSaves";
    public string RootDirectory { get; private set; }
    public string ActiveRunPath { get { return Path.Combine(RootDirectory, "active_run.json"); } }

    public RoguelikeSavePaths()
        : this(Path.Combine(Application.dataPath, ProjectSaveDirectoryName, SaveDirectoryName))
    {
    }

    public RoguelikeSavePaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("存档根目录不能为空。", "rootDirectory");
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string GetChapterDirectory(int entryChapterId)
    {
        EnsureValidEntryChapter(entryChapterId);
        return Path.Combine(RootDirectory, "chapters", "chapter_" + entryChapterId.ToString("D3"));
    }

    public string GetChapterSlotPath(int entryChapterId, int slotIndex)
    {
        EnsureValidEntryChapter(entryChapterId);
        EnsureValidSlot(slotIndex);
        return Path.Combine(GetChapterDirectory(entryChapterId), "slot_" + slotIndex + ".json");
    }

    public static string GetTemporaryPath(string formalPath)
    {
        return formalPath + ".tmp";
    }

    public static string GetBackupPath(string formalPath)
    {
        return formalPath + ".bak";
    }

    public static void EnsureValidEntryChapter(int entryChapterId)
    {
        if (entryChapterId < 2)
            throw new ArgumentOutOfRangeException("entryChapterId", "永久入口章节必须大于等于 2。第一章没有永久槽位。");
    }

    public static void EnsureValidSlot(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex > 3)
            throw new ArgumentOutOfRangeException("slotIndex", "永久入口槽位只能是 1、2、3。");
    }
}
