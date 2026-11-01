using System;

[Serializable]
public sealed class CharacterCardArtManifest
{
    public static string PortraitFile(int characterId) => $"Character_Icon/{characterId}_04.png";
    public string framePath = string.Empty;
    public CharacterCardArtEntry[] characters = Array.Empty<CharacterCardArtEntry>();

    public bool TryGetCardPath(int characterId, out string cardPath)
    {
        if (characters != null)
        {
            foreach (CharacterCardArtEntry entry in characters)
            {
                if (entry != null && entry.characterId == characterId &&
                    !string.IsNullOrWhiteSpace(entry.cardPath))
                {
                    cardPath = entry.cardPath;
                    return true;
                }
            }
        }

        cardPath = string.Empty;
        return false;
    }
}

[Serializable]
public sealed class CharacterCardArtEntry
{
    public int characterId;
    public string cardPath = string.Empty;
}
