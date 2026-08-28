using System;
using System.Collections.Generic;

[Serializable]
public sealed class WeaponAttributesData
{
    public int WeaponID;
    public string WeaponName;
    public int WeaponType;
    public int Star;
    public string SecondaryAttribute;
    public int AscensionCurve;
    public float BaseATK;
    public float BaseSecondaryAttribute;
    public string Description;
}

[Serializable]
public sealed class WeaponLevelBonusData
{
    public int WeaponID;
    public float[] ATKPerLevel = new float[7];
    public float[] SecondaryPerLevel = new float[7];
}

[Serializable]
public sealed class WeaponParamData
{
    public int WeaponID;
    public int Index;
    public float Param;
    public float RefineBonus;
}

[Serializable]
public sealed class WeaponLoadout
{
    public int WeaponID;
    public int Level = 1;
    public int Ascension;
    public int Refinement = 1;
}

[Serializable]
public sealed class WeaponPanel
{
    public float BaseATK;
    public string SecondaryAttribute;
    public float SecondaryValue;
}

[Serializable]
public sealed class WeaponRuntimeContext
{
    public int WeaponID;
    public int Refinement;
    public BattleEntity Equipper;
    public string SourceInstanceID;
    public Dictionary<int, float> ResolvedParams = new Dictionary<int, float>();

    public float ResolveParam(int index)
    {
        return ResolvedParams != null && ResolvedParams.TryGetValue(index, out float value)
            ? value
            : 0f;
    }
}
