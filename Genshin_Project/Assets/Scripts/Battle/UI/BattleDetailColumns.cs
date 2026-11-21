using UnityEngine;
using UnityEngine.UI;

/// <summary>The title list and reading pane share the HTML detail grid. Text wraps inside its assigned column.</summary>
public sealed class BattleDetailColumns : LayoutGroup
{
    private const float Gap = 25;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        SetLayoutInputForAxis(0, 0, 1, 0);
    }

    public override void SetLayoutHorizontal()
    {
        if (rectChildren.Count != 2) return;
        float width = rectTransform.rect.width;
        float listWidth = Mathf.Min(Mathf.Max(237, width * .30f), Mathf.Max(0, width - Gap));
        SetChildAlongAxis(rectChildren[0], 0, 0, listWidth);
        SetChildAlongAxis(rectChildren[1], 0, listWidth + Gap, Mathf.Max(0, width - listWidth - Gap));
    }

    public override void CalculateLayoutInputVertical()
    {
        float height = 0;
        foreach (var column in rectChildren)
            height = Mathf.Max(height, LayoutUtility.GetPreferredHeight(column));
        SetLayoutInputForAxis(height, height, 0, 1);
    }

    public override void SetLayoutVertical()
    {
        foreach (var column in rectChildren)
            SetChildAlongAxis(column, 1, 0, LayoutUtility.GetPreferredHeight(column));
    }
}
