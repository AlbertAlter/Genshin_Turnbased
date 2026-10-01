using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Composes a character card from independent art and frame Image layers.</summary>
public sealed class CharacterCardView : MonoBehaviour
{
    public Image cardImage;
    public Image frameImage;

    private int _characterId = -1;
    private Coroutine _loadRoutine;

    public void SetCharacter(int characterId)
    {
        if (_characterId == characterId) return;
        _characterId = characterId;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        ClearImages();
        if (characterId > 0) _loadRoutine = StartCoroutine(Load(characterId));
    }

    public void ClearCharacter()
    {
        _characterId = -1;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = null;
        ClearImages();
    }

    private IEnumerator Load(int characterId)
    {
        yield return CharacterCardArtResolver.Load(
            characterId,
            (card, frame) =>
            {
                if (_characterId != characterId) return;
                if (cardImage != null) cardImage.sprite = card;
                if (frameImage != null)
                {
                    frameImage.sprite = frame;
                    frameImage.transform.SetAsLastSibling();
                }
            },
            message => Debug.LogWarning(message, this));
        _loadRoutine = null;
    }

    private void ClearImages()
    {
        if (cardImage != null) cardImage.sprite = null;
        if (frameImage != null) frameImage.sprite = null;
    }
}
