#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Synchronous, non-serialized preview for StreamingAssets-backed battlefield
/// art. StreamingAssets uses Unity's DefaultImporter, so these textures cannot
/// be assigned as regular Sprite assets in a saved scene.
/// </summary>
public sealed partial class BattlefieldStageViewLoader
{
    private sealed class EditorPreviewAsset
    {
        public Texture2D texture;
        public Sprite sprite;
    }

    private sealed class EditorPreviewAssignment
    {
        public Image image;
        public Sprite sprite;
    }

    [NonSerialized] private bool editorPreviewRefreshQueued;
    [NonSerialized] private Dictionary<string, EditorPreviewAsset> editorPreviewAssets;
    [NonSerialized] private List<EditorPreviewAssignment> editorPreviewAssignments;

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
        EditorApplication.projectChanged -= QueueEditorPreviewRefresh;
        EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
        EditorApplication.projectChanged += QueueEditorPreviewRefresh;

        if (!Application.isPlaying) QueueEditorPreviewRefresh();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
        EditorApplication.projectChanged -= QueueEditorPreviewRefresh;
        editorPreviewRefreshQueued = false;
        ClearEditorPreview();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) QueueEditorPreviewRefresh();
    }

    private void OnEditorPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            ClearEditorPreview();
        else if (state == PlayModeStateChange.EnteredEditMode)
            QueueEditorPreviewRefresh();
    }

    private void QueueEditorPreviewRefresh()
    {
        if (Application.isPlaying || editorPreviewRefreshQueued) return;
        editorPreviewRefreshQueued = true;
        EditorApplication.delayCall += RefreshEditorPreviewAfterDelay;
    }

    private void RefreshEditorPreviewAfterDelay()
    {
        editorPreviewRefreshQueued = false;
        if (this == null || Application.isPlaying || !isActiveAndEnabled) return;
        RefreshEditorPreview();
    }

    [ContextMenu("Refresh edit-mode preview")]
    private void RefreshEditorPreview()
    {
        if (Application.isPlaying) return;

        ClearEditorPreview();
        SetDeferredSectionsVisible(false);
        SetRosterSlotsVisible(false);
        SetEditorActionTextVisible(false);

        StageSetupData stage = ReadEditorPreviewStage();
        if (stage == null) return;

        // Load every fixed frame/button/shadow first. Dynamic placeholders are
        // deliberately skipped and resolved from the current stage below.
        foreach (StreamingSpriteReference reference in FindSceneComponents<StreamingSpriteReference>())
        {
            if (!reference.loadOnEnable || !reference.revealOnLoad ||
                !IsConcreteStreamingPath(reference.relativePath))
                continue;
            ApplyEditorPreview(reference, reference.relativePath);
        }

        PreviewBackground(stage.BattleID);
        PreviewCharacters(stage.Allies ?? new List<StageAllySetup>());
        PreviewEnemies(stage.Enemies ?? new List<StageEnemySetup>());
        Canvas.ForceUpdateCanvases();
    }

    private StageSetupData ReadEditorPreviewStage()
    {
        if (stageConfigJson == null) return null;
        try
        {
            return JsonUtility.FromJson<StageSetupData>(stageConfigJson.text);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("BattleField1 edit-mode preview cannot parse Stage: " + exception.Message, this);
            return null;
        }
    }

    private void PreviewBackground(int battleId)
    {
        PsdUiBinding binding = FindSceneComponents<PsdUiBinding>()
            .FirstOrDefault(item => item.bindingKey == "BattleBackground");
        StreamingSpriteReference reference = FindStreamingReference(binding);
        ApplyEditorPreview(reference, $"art_assets/BattleBG/{Mathf.Max(1, battleId)}_BG.png");
    }

    private void PreviewCharacters(IReadOnlyList<StageAllySetup> allies)
    {
        BindOrderedGroups("character", "CharacterCard", allies.Count,
            index => allies[index] != null && allies[index].CharacterID > 0, (group, index) =>
            {
                StageAllySetup ally = allies[index];
                PreviewBoundImage(group, "CharacterCardArt",
                    $"art_assets/Character_Whole/{ally.CharacterID}_03.png");
            });

        // Keep this paired with the runtime binding call above: the generated
        // scene uses the localized roster/container names for the HUD slots.
        Transform characterRoster = FindBestRosterContainer("CharacterAvatar");
        PreviewOrderedRoster(characterRoster, allies.Count,
            index => allies[index] != null && allies[index].CharacterID > 0, (group, index) =>
            {
                StageAllySetup ally = allies[index];
                PreviewBoundImage(group, "CharacterAvatar",
                    $"art_assets/Character_Icon/{ally.CharacterID}_02.png");
                List<string> icons = LoadCharacterSkillIcons(ally.CharacterID);
                if (icons.Count > 0 && CharacterSkillIconResolver.TryGetRelativePath(icons[icons.Count - 1], out string burstPath))
                    PreviewBoundImage(group, "BurstIcon", burstPath);
            });

        PreviewActiveCharacterSkillIcons(allies);
    }

    private void PreviewEnemies(IReadOnlyList<StageEnemySetup> enemies)
    {
        Dictionary<int, string> enemyNames = LoadEnemyPreviewNames();
        BindSlottedEnemyGroups("Enemy", "EnemyCard", enemies, (group, enemy) =>
        {
            string artPath = FindEnemyPreviewArtPath(enemy.EnemyID);
            if (!string.IsNullOrEmpty(artPath))
                PreviewBoundImage(group, "EnemyCardArt", artPath);
        });

        Transform enemyRoster = FindBestRosterContainer("EnemyAvatar");
        PreviewSlottedRoster(enemyRoster, enemies, (group, enemy) =>
        {
            PreviewBoundImage(group, "EnemyAvatar",
                $"art_assets/Enemy_Icon/{enemy.EnemyID}_01.png");
            SetEditorEnemyLabel(group, enemy, enemyNames);
        });
    }

    private void PreviewActiveCharacterSkillIcons(IReadOnlyList<StageAllySetup> allies)
    {
        StageAllySetup active = allies.FirstOrDefault(item => item != null && item.CharacterID > 0);
        if (active == null) return;
        List<string> icons = LoadCharacterSkillIcons(active.CharacterID);
        if (icons.Count == 0) return;

        Transform skillContainer = FindBestRosterContainer("SkillIcon");
        if (skillContainer == null) return;
        List<Transform> groups = Enumerable.Range(0, skillContainer.childCount)
            .Select(skillContainer.GetChild)
            .Where(child => child.GetComponentsInChildren<PsdUiBinding>(true)
                .Any(binding => binding.bindingKey == "SkillIcon"))
            .OrderBy(child => FirstNumber(child.name))
            .ToList();
        int count = Mathf.Min(groups.Count, Mathf.Max(0, icons.Count - 1));
        for (int index = 0; index < count; index++)
        {
            if (CharacterSkillIconResolver.TryGetRelativePath(icons[index], out string path))
                PreviewBoundImage(groups[index], "SkillIcon", path);
        }
    }

    private Transform FindBestRosterContainer(string bindingKey)
    {
        return FindSceneComponents<Transform>()
            .Select(item => new
            {
                transform = item,
                matchingChildren = Enumerable.Range(0, item.childCount).Count(index =>
                    item.GetChild(index).GetComponentsInChildren<PsdUiBinding>(true)
                        .Any(binding => binding.bindingKey == bindingKey))
            })
            .Where(item => item.matchingChildren > 0)
            .OrderByDescending(item => item.matchingChildren)
            .Select(item => item.transform)
            .FirstOrDefault();
    }

    private static void PreviewOrderedRoster(
        Transform container,
        int configuredCount,
        Func<int, bool> isConfigured,
        Action<Transform, int> bind)
    {
        if (container == null) return;
        List<Transform> groups = Enumerable.Range(0, container.childCount)
            .Select(container.GetChild)
            .Where(child => child.GetComponentsInChildren<PsdUiBinding>(true)
                .Any(binding => binding.bindingKey == "CharacterAvatar"))
            .OrderBy(child => FirstNumber(child.name))
            .ToList();
        for (int index = 0; index < groups.Count; index++)
        {
            bool used = index < configuredCount && isConfigured(index);
            groups[index].gameObject.SetActive(used);
            if (used) bind(groups[index], index);
        }
    }

    private static void PreviewSlottedRoster(
        Transform container,
        IReadOnlyList<StageEnemySetup> enemies,
        Action<Transform, StageEnemySetup> bind)
    {
        if (container == null) return;
        List<Transform> groups = Enumerable.Range(0, container.childCount)
            .Select(container.GetChild)
            .Where(child => child.GetComponentsInChildren<PsdUiBinding>(true)
                .Any(binding => binding.bindingKey == "EnemyAvatar"))
            .OrderBy(child => FirstNumber(child.name))
            .ToList();
        Dictionary<int, StageEnemySetup> bySlot = enemies
            .Where(enemy => enemy != null && enemy.EnemyID > 0 && enemy.Slot >= 1 && enemy.Slot <= groups.Count)
            .GroupBy(enemy => enemy.Slot)
            .ToDictionary(group => group.Key, group => group.First());
        for (int index = 0; index < groups.Count; index++)
        {
            bool used = bySlot.TryGetValue(index + 1, out StageEnemySetup enemy);
            groups[index].gameObject.SetActive(used);
            if (used) bind(groups[index], enemy);
        }
    }

    private void PreviewBoundImage(Transform group, string bindingKey, string path)
    {
        foreach (PsdUiBinding binding in group.GetComponentsInChildren<PsdUiBinding>(true))
        {
            if (binding.bindingKey == bindingKey)
                ApplyEditorPreview(FindStreamingReference(binding), path);
        }
    }

    private static StreamingSpriteReference FindStreamingReference(PsdUiBinding binding)
    {
        return binding == null
            ? null
            : binding.GetComponent<StreamingSpriteReference>() ??
              binding.GetComponentInChildren<StreamingSpriteReference>(true);
    }

    private static void SetEditorEnemyLabel(
        Transform group,
        StageEnemySetup enemy,
        IReadOnlyDictionary<int, string> enemyNames)
    {
        PsdUiBinding labelBinding = group.GetComponentsInChildren<PsdUiBinding>(true)
            .FirstOrDefault(binding => binding.bindingKey == "EnemyLevelName");
        TMP_Text label = labelBinding != null ? labelBinding.GetComponent<TMP_Text>() : null;
        if (label == null) return;
        string name = enemyNames.TryGetValue(enemy.EnemyID, out string displayName) &&
                      !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : "Enemy " + enemy.EnemyID;
        label.text = $"Lv.{enemy.Level} {name}";
    }

    private static Dictionary<int, string> LoadEnemyPreviewNames()
    {
        var names = new Dictionary<int, string>();
        string file = Path.Combine(Application.streamingAssetsPath, "Data", "EnemyAttributes.xlsx");
        if (!File.Exists(file)) return names;
        try
        {
            using var package = new ExcelPackage(new FileInfo(file));
            var sheet = package.Workbook.Worksheets["Enemy_Main"];
            if (sheet?.Dimension == null) return names;
            for (int row = 2; row <= sheet.Dimension.End.Row; row++)
            {
                if (int.TryParse(sheet.Cells[row, 1].Text, out int id) && id > 0)
                    names[id] = sheet.Cells[row, 3].Text;
            }
        }
        catch
        {
            // The preview remains usable with an ID fallback when a workbook is
            // being edited or temporarily unavailable.
        }
        return names;
    }

    private static List<string> LoadCharacterSkillIcons(int characterId)
    {
        var icons = new List<string>();
        string folder = Path.Combine(Application.streamingAssetsPath, "Data", "Characters");
        if (!Directory.Exists(folder)) return icons;
        string file = Directory.GetFiles(folder, characterId + "_*.xlsx").FirstOrDefault();
        if (string.IsNullOrEmpty(file)) return icons;
        try
        {
            using var package = new ExcelPackage(new FileInfo(file));
            var sheet = package.Workbook.Worksheets["Skills"];
            if (sheet?.Dimension == null) return icons;
            int headerRow = -1;
            int iconColumn = -1;
            int headerLimit = Mathf.Min(5, sheet.Dimension.End.Row);
            for (int row = 1; row <= headerLimit && iconColumn < 0; row++)
            for (int column = 1; column <= sheet.Dimension.End.Column; column++)
            {
                if (!string.Equals(sheet.Cells[row, column].Text.Trim(), "Icon", StringComparison.OrdinalIgnoreCase))
                    continue;
                headerRow = row;
                iconColumn = column;
                break;
            }
            if (iconColumn < 0) return icons;
            for (int row = headerRow + 1; row <= sheet.Dimension.End.Row; row++)
            {
                string icon = sheet.Cells[row, iconColumn].Text.Trim();
                if (!string.IsNullOrEmpty(icon)) icons.Add(icon);
            }
        }
        catch
        {
            // Frames and the rest of the preview still render if an individual
            // character workbook is temporarily locked by Excel.
        }
        return icons;
    }

    private void SetEditorActionTextVisible(bool visible)
    {
        foreach (PsdUiBinding binding in FindSceneComponents<PsdUiBinding>())
        {
            if (binding.bindingKey == "CooldownTurns" || binding.bindingKey == "BurstCooldown")
                binding.gameObject.SetActive(visible);
        }
    }

    private string FindEnemyPreviewArtPath(int enemyId)
    {
        string manifestFile = Path.Combine(Application.streamingAssetsPath,
            EnemyArtResolver.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestFile)) return string.Empty;
        try
        {
            EnemyArtManifest manifest = JsonUtility.FromJson<EnemyArtManifest>(File.ReadAllText(manifestFile));
            return manifest != null && manifest.TryGetArtPath(enemyId, out string path) ? path : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ApplyEditorPreview(StreamingSpriteReference reference, string relativePath)
    {
        if (reference == null || !IsConcreteStreamingPath(relativePath)) return;
        Image image = reference.GetComponent<Image>();
        if (image == null) return;

        EditorPreviewAsset asset = GetOrCreateEditorPreviewAsset(relativePath);
        if (asset == null || asset.sprite == null)
        {
            image.sprite = null;
            image.enabled = false;
            return;
        }

        image.sprite = asset.sprite;
        image.enabled = reference.revealOnLoad;
        image.preserveAspect = !reference.allowCrop;
        AspectRatioFitter fitter = image.GetComponent<AspectRatioFitter>();
        if (reference.allowCrop && fitter != null && asset.sprite.rect.height > 0f)
        {
            fitter.aspectRatio = asset.sprite.rect.width / asset.sprite.rect.height;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }

        EditorPreviewAssignments.Add(new EditorPreviewAssignment { image = image, sprite = asset.sprite });
    }

    private EditorPreviewAsset GetOrCreateEditorPreviewAsset(string relativePath)
    {
        string key = relativePath.Replace('\\', '/').TrimStart('/');
        if (EditorPreviewAssets.TryGetValue(key, out EditorPreviewAsset cached)) return cached;

        string streamingRoot = Path.GetFullPath(Application.streamingAssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string file = Path.GetFullPath(Path.Combine(streamingRoot,
            key.Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            EditorPreviewAssets[key] = null;
            return null;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = Path.GetFileNameWithoutExtension(file) + " [Editor Preview]",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 1
        };
        if (!texture.LoadImage(File.ReadAllBytes(file), false))
        {
            DestroyImmediate(texture);
            EditorPreviewAssets[key] = null;
            return null;
        }

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        var created = new EditorPreviewAsset { texture = texture, sprite = sprite };
        EditorPreviewAssets[key] = created;
        return created;
    }

    private static bool IsConcreteStreamingPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && path != "-" &&
               !path.Contains("{") && !path.Contains("}") &&
               !Path.IsPathRooted(path) && !path.Contains(":") &&
               !path.Replace('\\', '/').Split('/').Contains("..");
    }

    private IEnumerable<T> FindSceneComponents<T>() where T : Component
    {
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) yield break;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        foreach (T component in root.GetComponentsInChildren<T>(true))
            yield return component;
    }

    private Dictionary<string, EditorPreviewAsset> EditorPreviewAssets =>
        editorPreviewAssets ??= new Dictionary<string, EditorPreviewAsset>(StringComparer.OrdinalIgnoreCase);

    private List<EditorPreviewAssignment> EditorPreviewAssignments =>
        editorPreviewAssignments ??= new List<EditorPreviewAssignment>();

    private void ClearEditorPreview()
    {
        if (editorPreviewAssignments != null)
        {
            foreach (EditorPreviewAssignment assignment in editorPreviewAssignments)
            {
                if (assignment.image != null && assignment.image.sprite == assignment.sprite)
                {
                    assignment.image.sprite = null;
                    assignment.image.enabled = false;
                }
            }
            editorPreviewAssignments.Clear();
        }

        if (editorPreviewAssets == null) return;
        foreach (EditorPreviewAsset asset in editorPreviewAssets.Values.Where(item => item != null))
        {
            if (asset.sprite != null) DestroyImmediate(asset.sprite);
            if (asset.texture != null) DestroyImmediate(asset.texture);
        }
        editorPreviewAssets.Clear();
    }
}
#endif
