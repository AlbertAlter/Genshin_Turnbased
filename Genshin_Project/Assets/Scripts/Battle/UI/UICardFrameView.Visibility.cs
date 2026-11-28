using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class UICardFrameView
{
    private readonly Dictionary<object,float> artHiddenOwners=new Dictionary<object,float>();
    public float ArtHiddenAmount {get;private set;}
    /// <summary>Hides only card artwork/frame/back. Health, status overlays and borrowed effect mounts stay independent.</summary>
    public void SetArtHidden(object owner,float amount)
    {
        if(owner==null)return;
        if(amount<=.0001f)artHiddenOwners.Remove(owner);else artHiddenOwners[owner]=Mathf.Clamp01(amount);
        float value=0;foreach(float current in artHiddenOwners.Values)value=Math.Max(value,current);
        if(Math.Abs(value-ArtHiddenAmount)<.00001f)return;
        ArtHiddenAmount=value;
        foreach(var layer in new Graphic[]{Surface,frameImage,innerImage,OuterShadow,glowImage})if(layer!=null)layer.canvasRenderer.SetAlpha(1-value);
        GetComponent<UICardView>()?.SetArtHiddenBack(value);
    }
    private void ReleaseArtVisibility()
    {artHiddenOwners.Clear();ArtHiddenAmount=0;GetComponent<UICardView>()?.SetArtHiddenBack(0);}
}
