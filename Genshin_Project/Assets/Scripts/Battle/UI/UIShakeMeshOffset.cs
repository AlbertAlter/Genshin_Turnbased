using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Final render-only deformation. Layout, anchors, hitboxes and scene hierarchy remain untouched.</summary>
[DisallowMultipleComponent]
public sealed class UIShakeMeshOffset : BaseMeshEffect
{
    private Matrix4x4 worldDelta = Matrix4x4.identity;
    private TextMeshProUGUI text;
    protected override void OnEnable()
    {
        base.OnEnable(); text = GetComponent<TextMeshProUGUI>();
        if (text != null) text.OnPreRenderText += DeformText;
    }
    protected override void OnDisable()
    {
        worldDelta = Matrix4x4.identity;
        if (text != null) { text.OnPreRenderText -= DeformText; text.ForceMeshUpdate(); }
        base.OnDisable();
    }
    public void SetWorldDelta(Matrix4x4 delta)
    {
        if (worldDelta == delta) return;
        worldDelta = delta;
        if (graphic != null) graphic.SetVerticesDirty();
        // TMP builds its own meshes rather than Graphic's IMeshModifier pipeline.
        if (text != null && text.isActiveAndEnabled) text.ForceMeshUpdate();
    }
    private Matrix4x4 LocalDelta => transform.worldToLocalMatrix * worldDelta * transform.localToWorldMatrix;
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || text != null || worldDelta == Matrix4x4.identity) return;
        Matrix4x4 delta = LocalDelta; UIVertex vertex = default;
        for (int i = 0; i < mesh.currentVertCount; i++)
        { mesh.PopulateUIVertex(ref vertex, i); vertex.position = delta.MultiplyPoint3x4(vertex.position); mesh.SetUIVertex(vertex, i); }
    }
    private void DeformText(TMP_TextInfo info)
    {
        if (worldDelta == Matrix4x4.identity || !IsActive()) return;
        Matrix4x4 delta = LocalDelta;
        for (int material = 0; material < info.meshInfo.Length; material++)
        {
            var mesh = info.meshInfo[material];
            for (int i = 0; i < mesh.vertexCount; i++) mesh.vertices[i] = delta.MultiplyPoint3x4(mesh.vertices[i]);
        }
    }
}
