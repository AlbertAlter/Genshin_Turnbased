using UnityEngine;

/// <summary>
/// Stable metadata emitted by the PSD UI importer. Runtime view builders can
/// resolve a node by bindingKey without depending on its visual layer name.
/// </summary>
public sealed class PsdUiBinding : MonoBehaviour
{
    public string bindingKey;
    public string templateId;
    public string templateRole;
    [HideInInspector] public string sourcePath;
}
