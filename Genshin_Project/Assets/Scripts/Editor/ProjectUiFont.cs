using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Single source of truth for every generated TextMesh Pro label.</summary>
public static class ProjectUiFont
{
    public const string SourceFontPath = "Assets/Fonts/zh-cn.ttf";
    public const string FontAssetPath = "Assets/Fonts/zh-cn SDF.asset";

    public static TMP_FontAsset LoadOrCreate()
    {
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fontAsset == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
                throw new FileNotFoundException("项目唯一 UI 字体不存在。", SourceFontPath);

            fontAsset = TMP_FontAsset.CreateFontAsset(source);
            fontAsset.name = "zh-cn SDF";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            Material material = fontAsset.material;
            Texture2D[] atlases = fontAsset.atlasTextures;
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            if (material != null && !AssetDatabase.Contains(material))
                AssetDatabase.AddObjectToAsset(material, fontAsset);
            if (atlases != null)
            {
                foreach (Texture2D atlas in atlases)
                    if (atlas != null && !AssetDatabase.Contains(atlas))
                        AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            }
            AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);
        }

        ApplyAsTmpDefault(fontAsset);
        return fontAsset;
    }

    private static void ApplyAsTmpDefault(TMP_FontAsset fontAsset)
    {
        TMP_Settings settings = TMP_Settings.instance;
        if (settings == null) return;
        var serialized = new SerializedObject(settings);
        SerializedProperty property = serialized.FindProperty("m_defaultFontAsset");
        if (property == null || property.objectReferenceValue == fontAsset) return;
        property.objectReferenceValue = fontAsset;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
