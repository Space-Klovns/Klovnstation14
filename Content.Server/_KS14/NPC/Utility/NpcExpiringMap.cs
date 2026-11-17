namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     Helpers for the dictionaries the NPC systems keep of things they remember until a given time.
/// </summary>
public static class NpcExpiringMap
{
    /// <summary>
    ///     Removes every entry whose end time is <paramref name="now"/> or earlier.
    /// </summary>
    public static void PruneExpired<TKey>(Dictionary<TKey, TimeSpan> map, TimeSpan now)
        where TKey : notnull
    {
        // Removing the current entry while enumerating a Dictionary is allowed.
        foreach (var (key, end) in map)
        {
            if (end <= now)
                map.Remove(key);
        }
    }

    /// <summary>
    ///     Whether <paramref name="key"/> is in <paramref name="map"/> with an end time after <paramref name="now"/>.
    /// </summary>
    public static bool IsLive<TKey>(Dictionary<TKey, TimeSpan> map, TKey key, TimeSpan now)
        where TKey : notnull
    {
        return map.TryGetValue(key, out var end) && now < end;
    }
}
