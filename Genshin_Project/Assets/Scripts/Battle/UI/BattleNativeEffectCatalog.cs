using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Native asset references published by the art importer, valid in Editor and player builds.</summary>
public sealed class BattleNativeEffectCatalog : ScriptableObject
{
    public const string ResourcePath = "UI/BattleNativeEffects";
    [Serializable] public sealed class Part
    {
        public BattleEffectPart part;
        public BattleEffectModelAsset model;
    }
    [Serializable] public sealed class Entry
    {
        public BattleEffectKind kind;
        public List<Part> parts = new List<Part>();
    }
    public Shader shader;
    public List<Entry> entries = new List<Entry>();
    public BattleEffectAssets Assets(BattleEffectKind kind)
    {
        foreach (var entry in entries)
            if (entry.kind == kind)
            {
                var assets = new BattleEffectAssets();
                foreach (var part in entry.parts) assets.Models.Add(part.part, part.model);
                return assets;
            }
        throw new InvalidOperationException("原生特效资源目录缺少：" + kind);
    }
}
