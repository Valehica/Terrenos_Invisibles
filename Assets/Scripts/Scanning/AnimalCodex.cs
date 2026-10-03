using System.Collections.Generic;

public static class AnimalCodex
{
    private static readonly HashSet<string> unlockedSpecies = new HashSet<string>();

    public static bool IsUnlocked(string speciesId) => unlockedSpecies.Contains(speciesId);

    public static void Unlock(string speciesId) => unlockedSpecies.Add(speciesId);
}
