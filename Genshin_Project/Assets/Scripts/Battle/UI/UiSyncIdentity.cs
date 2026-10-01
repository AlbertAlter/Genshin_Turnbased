using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Saved with the scene; not used by gameplay. IDs survive object renaming.</summary>
[DisallowMultipleComponent]
public sealed class UiSyncIdentity : MonoBehaviour
{
    [HideInInspector] public string id;
    [HideInInspector] public List<UiSyncField> baseline = new List<UiSyncField>();
}

[Serializable]
public sealed class UiSyncField
{
    public string key;
    public string value;
}
