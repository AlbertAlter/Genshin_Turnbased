using System;
using UnityEditor;

/// <summary>Native UE meshes are cached once by the shared UI projection renderer.</summary>
public sealed class BattleEffectArtPostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetPath.StartsWith("Assets/ArtAssets/UE/", StringComparison.Ordinal))
        {
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            // Older effect FBXs omit smoothing groups and morph normals. Let Unity
            // calculate the morph normals instead of asking the FBX SDK to generate them.
            // Keep the authored base normals; the projection shader still uses them.
            importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingSource = ModelImporterNormalSmoothingSource.PreferSmoothingGroups;
        }
    }
    public override uint GetVersion() => 2;
}
