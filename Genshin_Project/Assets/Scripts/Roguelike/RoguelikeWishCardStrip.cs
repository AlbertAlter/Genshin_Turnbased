using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>当期卡牌横向列表。卡牌尺寸取模板，滚动范围取实际内容宽度。</summary>
public sealed class RoguelikeWishCardStrip : MonoBehaviour
{
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RoguelikeWishTile characterTemplate, weaponTemplate;
    [SerializeField] private Button previous, next;
    [SerializeField, Min(0)] private float spacing = 14;
    [SerializeField, Min(0)] private float cardWidth;

    private readonly List<RoguelikeWishTile> tiles = new List<RoguelikeWishTile>();
    private float stride;

    private void OnEnable()
    {
        previous.onClick.AddListener(Previous);
        next.onClick.AddListener(Next);
        scroll.onValueChanged.AddListener(UpdateArrows);
    }

    private void OnDisable()
    {
        previous.onClick.RemoveListener(Previous);
        next.onClick.RemoveListener(Next);
        scroll.onValueChanged.RemoveListener(UpdateArrows);
        Clear();
    }

    internal void Bind(UICardFactory factory, RoguelikeWishPool pool, RoguelikeWishLanguage language,
        Action<RoguelikeWishPrize> openDetail)
    {
        Clear();
        if (factory != null && factory.isActiveAndEnabled && pool != null)
        {
            var template = pool.Featured.Kind == RoguelikeWishKind.Character ? characterTemplate : weaponTemplate;
            float width = ((RectTransform)template.transform).sizeDelta.x;
            float scale = cardWidth > 0f ? cardWidth / width : 1f;
            stride = width * scale + spacing;
            foreach (var prize in pool.FourStars) AddCard(prize);
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(0f, tiles.Count * stride - spacing));

            void AddCard(RoguelikeWishPrize prize)
            {
                var tile = RoguelikeWishView.Spawn(template, scroll.content);
                var rect = (RectTransform)tile.transform;
                rect.anchoredPosition = rect.anchoredPosition * scale + new Vector2(tiles.Count * stride, 0);
                rect.localScale = Vector3.one * scale;
                tile.BindCard(factory, prize, language, () => openDetail(prize));
                tiles.Add(tile);
            }
        }
        UpdateArrows(Vector2.zero);
    }

    internal void RefreshLanguage(RoguelikeWishLanguage language)
    {
        foreach (var tile in tiles) tile.RefreshLanguage(language);
    }

    internal void Clear()
    {
        RoguelikeWishView.Clear(tiles);
        scroll.StopMovement();
        scroll.content.anchoredPosition = Vector2.zero;
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0);
        UpdateArrows(Vector2.zero);
    }

    private float Overflow => Mathf.Max(0f, scroll.content.rect.width - scroll.viewport.rect.width);
    private void Previous() => Step(-stride);
    private void Next() => Step(stride);

    private void Step(float amount)
    {
        float overflow = Overflow;
        if (overflow <= 0f) return;
        scroll.StopMovement();
        scroll.horizontalNormalizedPosition = Mathf.Clamp01((amount - scroll.content.anchoredPosition.x) / overflow);
        UpdateArrows(Vector2.zero);
    }

    private void UpdateArrows(Vector2 _)
    {
        float position = -scroll.content.anchoredPosition.x;
        bool overflow = Overflow > 1f;
        scroll.horizontal = overflow;
        previous.gameObject.SetActive(overflow && position > 1f);
        next.gameObject.SetActive(overflow && position < Overflow - 1f);
    }
}
