using System;

[Serializable]
internal sealed class PsdLayoutManifest
{
    public int schema_version;
    public string source;
    public PsdDocumentInfo document;
    public int top_level_layers;
    public int total_layers;
    public PsdLayerInfo[] layers;
}

[Serializable]
internal sealed class PsdDocumentInfo
{
    public int width;
    public int height;
    public int color_mode;
    public int depth;
}

[Serializable]
internal sealed class PsdLayerInfo
{
    public string name;
    public int depth;
    public string kind;
    public bool visible;
    public int opacity = 255;
    public string blend_mode;
    public int[] bbox;
    public bool clipping;
    public bool is_group;
    public string text;
    public string asset;
    public string raster_quality;
    public string export_error;
    public PsdLayerInfo[] children;
}
