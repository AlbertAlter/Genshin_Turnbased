using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays enemy art; missing art deliberately leaves the Image empty.</summary>
public sealed class EnemyArtView : MonoBehaviour
{
    public Image image;
    private int _enemyId = -1;
    private Coroutine _loadRoutine;

    public void SetEnemy(int enemyId)
    {
        if (_enemyId == enemyId) return;
        _enemyId = enemyId;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        if (image != null) image.sprite = null;
        if (enemyId > 0) _loadRoutine = StartCoroutine(Load(enemyId));
    }

    public void ClearEnemy()
    {
        _enemyId = -1;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = null;
        if (image != null) image.sprite = null;
    }

    private IEnumerator Load(int enemyId)
    {
        yield return EnemyArtResolver.Load(
            enemyId,
            sprite =>
            {
                if (_enemyId == enemyId && image != null) image.sprite = sprite;
            },
            message => Debug.LogWarning(message, this));
        _loadRoutine = null;
    }
}
