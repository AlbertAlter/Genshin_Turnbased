using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Initial runtime bridge for the generated BattleField1 scene. Stage JSON owns
/// IDs/levels/slots; DataManager owns localized names; StreamingAssets owns art.
/// Boss and status UI stay hidden until their runtime contracts are configured.
/// </summary>
[ExecuteAlways]
public sealed partial class BattlefieldStageViewLoader : MonoBehaviour
{
    public TextAsset stageConfigJson;

    private IEnumerator Start()
    {
        // ExecuteAlways also invokes lifecycle methods while the scene is being
        // edited. The editor-preview half of this class owns that path; the
        // coroutine below remains the authoritative runtime binder.
        if (!Application.isPlaying) yield break;

        SetDeferredSectionsVisible(false);
        SetRosterSlotsVisible(false);
        if (stageConfigJson == null)
        {
            Debug.LogError("BattleField1 没有关卡编辑器 Stage 引用。", this);
            yield break;
        }

        StageSetupData stage = JsonUtility.FromJson<StageSetupData>(stageConfigJson.text);
        if (stage == null)
        {
            Debug.LogError("BattleField1 无法解析 Stage：" + stageConfigJson.name, this);
            yield break;
        }

        while (DataManager.Instance == null || !DataManager.Instance.IsLoaded) yield return null;
        BindBackground(stage.BattleID);
        BindCharacters(stage.Allies ?? new List<StageAllySetup>());
        BindEnemies(stage.Enemies ?? new List<StageEnemySetup>());
    }

    private static void SetRosterSlotsVisible(bool visible)
    {
        SetMatchingChildrenVisible("character", "CharacterCard", visible);
        SetMatchingChildrenVisible("角色栏", "角色", visible);
        SetMatchingChildrenVisible("Enemy", "EnemyCard", visible);
        SetMatchingChildrenVisible("敌人栏", "敌人", visible);
    }

    private static void SetMatchingChildrenVisible(string containerName, string childPrefix, bool visible)
    {
        Transform container = FindTransform(containerName);
        if (container == null) return;
        for (int index = 0; index < container.childCount; index++)
        {
            Transform child = container.GetChild(index);
            if (child.name.StartsWith(childPrefix)) child.gameObject.SetActive(visible);
        }
    }

    private static void BindBackground(int battleId)
    {
        PsdUiBinding binding = FindObjectsOfType<PsdUiBinding>(true)
            .FirstOrDefault(item => item.bindingKey == "BattleBackground");
        StreamingSpriteReference reference = binding != null
            ? binding.GetComponent<StreamingSpriteReference>() ?? binding.GetComponentInChildren<StreamingSpriteReference>(true)
            : null;
        if (reference != null)
            reference.SetResolvedPath($"art_assets/BattleBG/{Mathf.Max(1, battleId)}_BG.png");
    }

    private void BindCharacters(IReadOnlyList<StageAllySetup> allies)
    {
        BindOrderedGroups("character", "CharacterCard", allies.Count,
            index => allies[index] != null && allies[index].CharacterID > 0, (group, index) =>
        {
            StageAllySetup ally = allies[index];
            BindImage(group, "CharacterCardArt", $"art_assets/Character_Whole/{ally.CharacterID}_03.png");
        });
        BindOrderedGroups("角色栏", "角色", allies.Count,
            index => allies[index] != null && allies[index].CharacterID > 0, (group, index) =>
        {
            StageAllySetup ally = allies[index];
            BindImage(group, "CharacterAvatar", $"art_assets/Character_Icon/{ally.CharacterID}_02.png");
        });
    }

    private void BindEnemies(IReadOnlyList<StageEnemySetup> enemies)
    {
        BindSlottedEnemyGroups("Enemy", "EnemyCard", enemies, (group, enemy) =>
        {
            BindEnemyImage(group, "EnemyCardArt", enemy.EnemyID);
        });
        BindSlottedEnemyGroups("敌人栏", "敌人", enemies, (group, enemy) =>
        {
            BindImage(group, "EnemyAvatar", $"art_assets/Enemy_Icon/{enemy.EnemyID}_01.png");
            string displayName = DataManager.Instance.EnemyMainDict.TryGetValue(enemy.EnemyID, out EnemyMainData data)
                ? data.EnemyName
                : $"敌人 {enemy.EnemyID}";
            PsdUiBinding labelBinding = group.GetComponentsInChildren<PsdUiBinding>(true)
                .FirstOrDefault(binding => binding.bindingKey == "EnemyLevelName");
            TMP_Text levelAndName = labelBinding != null
                ? labelBinding.GetComponent<TMP_Text>()
                : group.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.text.StartsWith("Lv."));
            if (levelAndName != null) levelAndName.text = $"Lv.{enemy.Level} {displayName}";
        });
    }

    private void BindOrderedGroups(
        string containerName,
        string childPrefix,
        int configuredCount,
        System.Func<int, bool> isConfigured,
        System.Action<Transform, int> bind)
    {
        Transform container = FindTransform(containerName);
        if (container == null) return;
        List<Transform> groups = Enumerable.Range(0, container.childCount)
            .Select(container.GetChild)
            .Where(child => child.name.StartsWith(childPrefix))
            .OrderBy(child => FirstNumber(child.name))
            .ToList();
        for (int index = 0; index < groups.Count; index++)
        {
            bool used = index < configuredCount && isConfigured(index);
            groups[index].gameObject.SetActive(used);
            if (used) bind(groups[index], index);
        }
    }

    private void BindSlottedEnemyGroups(
        string containerName,
        string childPrefix,
        IReadOnlyList<StageEnemySetup> enemies,
        System.Action<Transform, StageEnemySetup> bind)
    {
        Transform container = FindTransform(containerName);
        if (container == null) return;
        List<Transform> groups = Enumerable.Range(0, container.childCount)
            .Select(container.GetChild)
            .Where(child => child.name.StartsWith(childPrefix))
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

    private static void BindImage(Transform group, string bindingKey, string path)
    {
        foreach (PsdUiBinding binding in group.GetComponentsInChildren<PsdUiBinding>(true))
        {
            if (binding.bindingKey != bindingKey) continue;
            StreamingSpriteReference reference = binding.GetComponent<StreamingSpriteReference>() ??
                                                 binding.GetComponentInChildren<StreamingSpriteReference>(true);
            if (reference != null) reference.SetResolvedPath(path);
        }
    }

    private void BindEnemyImage(Transform group, string bindingKey, int enemyId)
    {
        foreach (PsdUiBinding binding in group.GetComponentsInChildren<PsdUiBinding>(true))
        {
            if (binding.bindingKey != bindingKey) continue;
            StreamingSpriteReference reference = binding.GetComponent<StreamingSpriteReference>() ??
                                                 binding.GetComponentInChildren<StreamingSpriteReference>(true);
            if (reference == null) continue;
            UnityEngine.UI.Image image = reference.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
            {
                image.sprite = null;
                image.enabled = false;
            }
            StartCoroutine(EnemyArtResolver.Load(enemyId, sprite =>
            {
                if (image == null) return;
                image.sprite = sprite;
                image.enabled = true;
                image.preserveAspect = !reference.allowCrop;
                UnityEngine.UI.AspectRatioFitter fitter = image.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                if (reference.allowCrop && fitter != null && sprite.rect.height > 0f)
                {
                    fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                    fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
                }
            }, _ => { }));
        }
    }

    private void SetDeferredSectionsVisible(bool visible)
    {
        foreach (Transform item in FindObjectsOfType<Transform>(true))
        {
            if (item.name == "Boss" || item.name == "状态栏" || item.name == "敌人状态区")
                item.gameObject.SetActive(visible);
        }
        foreach (PsdUiBinding binding in FindObjectsOfType<PsdUiBinding>(true))
        {
            if (binding.bindingKey == "ElementAura" || binding.bindingKey == "StatusIcon")
                binding.gameObject.SetActive(visible);
        }
    }

    private static Transform FindTransform(string name)
    {
        return FindObjectsOfType<Transform>(true).FirstOrDefault(item => item.name == name);
    }

    private static int FirstNumber(string value)
    {
        int result = 0;
        bool found = false;
        foreach (char character in value)
        {
            if (character >= '0' && character <= '9')
            {
                found = true;
                result = result * 10 + character - '0';
            }
            else if (found) break;
        }
        return found ? result : int.MaxValue;
    }
}
