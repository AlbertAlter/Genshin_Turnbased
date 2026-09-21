using System;

[Serializable]
public sealed class EnemyArtManifest
{
    public EnemyArtEntry[] enemies = Array.Empty<EnemyArtEntry>();

    public bool TryGetArtPath(int enemyId, out string artPath)
    {
        if (enemies != null)
        {
            foreach (EnemyArtEntry entry in enemies)
            {
                if (entry != null && entry.enemyId == enemyId && !string.IsNullOrWhiteSpace(entry.artPath))
                {
                    artPath = entry.artPath;
                    return true;
                }
            }
        }

        artPath = string.Empty;
        return false;
    }
}

[Serializable]
public sealed class EnemyArtEntry
{
    public int enemyId;
    public string artPath = string.Empty;
}
