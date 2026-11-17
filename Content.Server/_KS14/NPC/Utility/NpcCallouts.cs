namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     The "happened lately and not said yet" checks behind the NPC callouts (a door that refused it, a death it
///         confirmed).
/// </summary>
public static class NpcCallouts
{
    /// <summary>
    ///     Whether something happened at <paramref name="at"/>, if it did, no longer than <paramref name="within"/> before
    ///         <paramref name="now"/>, itself included.
    /// </summary>
    public static bool IsPending(TimeSpan? at, TimeSpan within, TimeSpan now)
    {
        return at is { } happenedAt && now - happenedAt <= within;
    }
}
