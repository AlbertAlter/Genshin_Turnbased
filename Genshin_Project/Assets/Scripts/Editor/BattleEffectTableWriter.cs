using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Shared editor publishing and model resolution. Runtime playback only receives bound native assets and data.</summary>
[InitializeOnLoad]
public static partial class BattleEffectTableWriter
{
    private const string NativeRoot = "Assets/ArtAssets/UE/";
    static BattleEffectTableWriter()
    {
        SkillEffectsPreview.ResolveAssets = Resolve;
        SkillEffectsPreview.WriteParameters = Save;
        SkillEffectsPreview.WriteTableParameters = SaveTable;
    }
    public static void SyncPublished()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Charts/UE"));
        foreach (var kind in BattleEffectParameters.Kinds) BattleEffectParameters.ReadFile(kind, Path.Combine(source, BattleEffectParameters.FileName(kind)));
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UE"); Directory.CreateDirectory(target);
        string master=Path.Combine(source,BattleTableEffectCatalog.FileName);
        BattleTableEffectCatalog.ReadFile(master);
        File.Copy(master,Path.Combine(target,BattleTableEffectCatalog.FileName),true);
        foreach (var kind in BattleEffectParameters.Kinds)
        {
            string from = Path.Combine(source, BattleEffectParameters.FileName(kind)), to = Path.Combine(target, BattleEffectParameters.FileName(kind));
            File.Copy(from, to, true); File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
        }
    }
    private static string Save(BattleEffectKind kind, IReadOnlyDictionary<string, float> values)
    {
        BattleEffectParameters.Parse(kind, values);
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Charts/UE", BattleEffectParameters.FileName(kind)));
        string target = BattleEffectParameters.RuntimePath(kind);
        BattleEffectParameters.ReadFile(kind, source);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        bool existed = File.Exists(target), targetOpened = false, saved = false;
        try
        {
            using var from = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var to = new FileStream(target, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            targetOpened = true;
            byte[] before = Read(from), targetBefore = Read(to);
            byte[] edited = ParameterWorkbookPatch.Update(before, values);
            try { Write(from, edited); Write(to, edited); saved = true; }
            catch (Exception failure)
            {
                try { Write(from, before); Write(to, targetBefore); }
                catch (Exception recovery) { throw new IOException("写回失败且还原失败：" + recovery.Message, failure); }
                throw;
            }
        }
        finally { if (!saved && targetOpened && !existed && File.Exists(target)) File.Delete(target); }
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UE/" + BattleEffectParameters.FileName(kind), ImportAssetOptions.ForceUpdate);
        return "已写回并发布：Charts/UE/" + BattleEffectParameters.FileName(kind);
    }
    private static byte[] Read(Stream stream)
    { using var memory = new MemoryStream(); stream.Position = 0; stream.CopyTo(memory); return memory.ToArray(); }
    private static void Write(FileStream stream, byte[] bytes)
    { stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.SetLength(bytes.Length); stream.Flush(true); }

    private static BattleEffectModelAsset Model(string relative, float referenceRadius, bool animated = false, float referenceTime = 0, string[] materials = null)
    {
        string path = NativeRoot + relative;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new FileNotFoundException("尚未发布原生模型，请先导入美术资源：" + path);
        if (!importer.isReadable || (animated && (importer.animationType != ModelImporterAnimationType.Legacy || !importer.importAnimation || !importer.importBlendShapes)))
        {
            importer.isReadable = true;
            if (animated) { importer.animationType = ModelImporterAnimationType.Legacy; importer.importAnimation = true; importer.importBlendShapes = true; }
            importer.SaveAndReimport();
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidDataException("模型导入失败：" + path);
        AnimationClip clip = null;
        if (animated)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip candidate && !candidate.name.StartsWith("__preview__", StringComparison.Ordinal) && candidate.length > 0) { clip = candidate; break; }
            if (clip == null) throw new InvalidDataException("模型缺少可采样动画：" + path);
        }
        return new BattleEffectModelAsset { Prefab = prefab, Clip = clip, IncludedMaterials = materials,
            ReferenceRadius = referenceRadius, ReferenceTime = referenceTime };
    }
    public static BattleEffectAssets Resolve(BattleEffectKind kind)
    {
        if (kind == BattleEffectKind.DandelionWindOrbit) return ResolveDandelion();
        if (BattleEffectParameters.IsVenti(kind)) return ResolveVenti(kind);
        var assets = new BattleEffectAssets();
        const string common = "general/Explosion/Exports/Explosion_";
        const string amber = "Characters/1009_Amber/ArrowRain/Exports/Amber_";
        const string lisa = "Characters/1011_Lisa/VioletArc/Exports/Lisa_";
        const string kaeya = "Characters/1010_Kaeya/GlacialWaltz/Exports/Kaeya_";
        void Add(BattleEffectPart part, string path, float radius, bool animated = false, float time = 0, string[] materials = null)
            => assets.Models.Add(part, Model(path, radius, animated, time, materials));
        if (BattleEffectParameters.IsExplosion(kind))
        {
            Add(BattleEffectPart.Flash, common + "FlashCore.fbx", 1.150862f);
            Add(BattleEffectPart.Fire, common + "FlamePetal.fbx", 1.140002f);
            Add(BattleEffectPart.FireCloud, common + "FireCloud.fbx", 1.150862f);
            Add(BattleEffectPart.Ring, common + "ShockwaveBrokenRing.fbx", 1.033999f);
            Add(BattleEffectPart.Spark, common + "SparkStreak.fbx", 1);
            Add(BattleEffectPart.Smoke, common + "SmokeWisp.fbx", 1.150862f);
        }
        else if (kind == BattleEffectKind.AmberArrowRain)
        { Add(BattleEffectPart.Arrow, amber + "Arrow.fbx", 1.25f); Add(BattleEffectPart.Impact, amber + "ImpactFlame.fbx", .421473f); }
        else if (kind == BattleEffectKind.AmberBurningArea)
        {
            Add(BattleEffectPart.BurnFootprint, amber + "BurningArea.fbx", 1.100525f, materials: new[] { "BurnArea_SoftScorch_VertexRGBA" });
            Add(BattleEffectPart.Flame, amber + "FlameTongue.fbx", 1.012719f);
            Add(BattleEffectPart.Spark, common + "SparkStreak.fbx", 1);
            Add(BattleEffectPart.Ring, common + "ShockwaveBrokenRing.fbx", 1.033999f);
        }
        else if (kind == BattleEffectKind.LisaVioletArc)
        {
            Add(BattleEffectPart.LisaCast, lisa + "CastUp.fbx", 5.525429f, true, 11f / 17);
            Add(BattleEffectPart.LisaStrike, lisa + "LightningStrike.fbx", 6.116439f, true, 2f / 31);
        }
        else if (kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.KaeyaIcicle)
        {
            Add(BattleEffectPart.KaeyaIce, kaeya + "Icicle.fbx", .75027015f);
            Add(BattleEffectPart.KaeyaMist, kaeya + "FrostMist.fbx", 1.19295175f);
            Add(BattleEffectPart.KaeyaGrain, kaeya + "FrostGrain.fbx", .40050609f);
            Add(BattleEffectPart.KaeyaImpact, kaeya + "IceImpact.fbx", .49735295f, true, 1f / 26);
            foreach (var model in assets.Models.Values)
            {
                model.IceMaterials = new[] { "Kaeya_Ice_TranslucentIOR131" };
                model.MistMaterials = new[] { "Kaeya_Ice_TransientFrostMist" };
            }
        }
        else throw new ArgumentOutOfRangeException(nameof(kind));
        return assets;
    }
}

