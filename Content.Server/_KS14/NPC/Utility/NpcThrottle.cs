using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     The "do this at most every so often" timer the NPC systems keep as a <see cref="TimeSpan"/> field, so each
///         does not write it out by hand.
/// </summary>
public static class NpcThrottle
{
    /// <summary>
    ///     When <paramref name="now"/> has reached <paramref name="next"/>, moves <paramref name="next"/> on to
    ///         <paramref name="interval"/> from now and returns true. Otherwise leaves it alone and returns false.
    /// </summary>
    public static bool TryConsume(ref TimeSpan next, TimeSpan now, TimeSpan interval)
    {
        if (now < next)
            return false;

        next = now + interval;
        return true;
    }

    /// <summary>
    ///     As <see cref="TryConsume(ref TimeSpan, TimeSpan, TimeSpan)"/>, but with <paramref name="force"/> it is due
    ///         whatever <paramref name="next"/> says. Either way the next one is <paramref name="interval"/> from now.
    /// </summary>
    public static bool TryConsume(ref TimeSpan next, TimeSpan now, TimeSpan interval, bool force)
    {
        if (!force && now < next)
            return false;

        next = now + interval;
        return true;
    }

    /// <summary>
    ///     A first due time somewhere within <paramref name="interval"/> from now, so things spawned together do
    ///         not all come due on the same tick.
    /// </summary>
    public static TimeSpan Spread(TimeSpan now, TimeSpan interval, IRobustRandom random)
    {
        return now + interval * random.NextDouble();
    }
}
