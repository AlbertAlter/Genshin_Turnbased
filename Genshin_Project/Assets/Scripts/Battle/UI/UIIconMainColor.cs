using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Shared opt-in icon recolouring. One source texture retains its shading; each host owns its material cache.</summary>
public sealed class UIIconMainColor : IDisposable
{
    public const string IntrinsicElement = "IntrinsicElement";
    private readonly ElementColorPalette palette;
    private readonly Dictionary<string, Color> fixedColors = new Dictionary<string, Color>(StringComparer.Ordinal);
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private Shader shader;
    private bool disposed;

    public UIIconMainColor(ElementColorPalette palette)
        => this.palette = palette ?? throw new ArgumentNullException(nameof(palette));

    /// <summary>Blank preserves original art; otherwise use an element, #RRGGBB, or the actual host's unique intrinsic element.</summary>
    public static void Validate(string specification)
    {
        if (string.IsNullOrEmpty(specification) || specification == IntrinsicElement) return;
        foreach (string element in ElementQuery.Elements) if (specification == element) return;
        if (specification.Length == 7 && specification[0] == '#' && ColorUtility.TryParseHtmlString(specification, out _)) return;
        throw new InvalidDataException("图标 MainColor 须为空、七元素英文名、#RRGGBB或IntrinsicElement：" + specification);
    }

    public Color ResolveColor(string specification, BattleEntity host)
    {
        if (disposed) throw new ObjectDisposedException(nameof(UIIconMainColor));
        Validate(specification);
        if (string.IsNullOrEmpty(specification)) throw new ArgumentException("空主色使用原图，不需要求色。", nameof(specification));
        string key = specification;
        if (key == IntrinsicElement)
        {
            if (host == null) throw new InvalidDataException("IntrinsicElement需要状态的实际单位宿主。");
            key = null;
            foreach (string element in ElementQuery.Elements)
                if (ElementQuery.ReadIntrinsic(host, element) != null)
                {
                    if (key != null) throw new InvalidDataException("图标宿主有多个固有元素，请显式指定MainColor。");
                    key = element;
                }
            if (key == null) throw new InvalidDataException("图标宿主没有固有元素，请显式指定MainColor。");
        }
        if (!fixedColors.TryGetValue(key, out Color color))
        {
            color = key[0] == '#' && ColorUtility.TryParseHtmlString(key, out var literal) ? literal : palette.GetColor(key);
            fixedColors.Add(key, color);
        }
        return color;
    }

    public Material GetMaterial(string specification, BattleEntity host)
    {
        if (disposed) throw new ObjectDisposedException(nameof(UIIconMainColor));
        if (string.IsNullOrEmpty(specification)) return null;
        Color color = ResolveColor(specification, host);
        if (materials.TryGetValue(color, out var material)) return material;
        if (shader == null) shader = Resources.Load<Shader>("UI/IconMainColor");
        if (shader == null) throw new InvalidOperationException("缺少公共图标主色着色器UI/IconMainColor。");
        material = new Material(shader) { name = "IconMainColor_" + ColorUtility.ToHtmlStringRGB(color), hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_MainColor", color);
        materials.Add(color, material);
        return material;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var material in materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
        materials.Clear(); fixedColors.Clear(); shader = null;
    }
}
