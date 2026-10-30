using System.Numerics;

namespace Content.Shared._KS14.ZLevel.Audio;

/// <summary>
///     Works out where a sound on one z-level appears to come from, heard from another.
/// </summary>
/// <remarks>
///     Sound does not go through a floor, it goes through the gaps in one - so a leaked sound is not heard from
///         where it was made, it is heard from the hole it came through, the way it would be from the far side
///         of a doorway. What travels is the length of the whole bent path, not the straight line.
///     The engine positions audio in two dimensions, so both of those cannot be handed to it directly: a source
///         placed at the hole is heard from the right direction but far too close. What is handed over instead
///         is the hole's direction at the whole path's distance, which is the pair a listener actually reads -
///         bearing from where it enters the room, loudness from how far it has travelled to get there.
/// </remarks>
public static class KsZLevelAudioLeak
{
    /// <summary>
    ///     Below this, two positions are the same place and the direction between them means nothing.
    /// </summary>
    public const float MinimumDirection = 0.01f;

    /// <summary>
    ///     How long the path from a sound to a listener is, going through a gap in the floor between them.
    /// </summary>
    /// <param name="sourcePosition">Where the sound is, on its own z-level.</param>
    /// <param name="holePosition">The gap it passes through, on the upper z-level's floor plane.</param>
    /// <param name="listenerPosition">Where it is being heard, on the other z-level.</param>
    /// <param name="verticalDistance">How far apart the two floor planes are, in tiles.</param>
    public static float GetPathLength(
        Vector2 sourcePosition,
        Vector2 holePosition,
        Vector2 listenerPosition,
        float verticalDistance)
    {
        // Along its own floor to the gap...
        var toHole = (sourcePosition - holePosition).Length();

        // ...then down through it, which is the only leg with any height to it.
        var holeToListener = (holePosition - listenerPosition).Length();
        var throughHole = MathF.Sqrt(holeToListener * holeToListener + verticalDistance * verticalDistance);

        return toHole + throughHole;
    }

    /// <summary>
    ///     Where to place a leaked sound so that it is heard from the direction of the gap it came through, at
    ///         the distance it actually travelled.
    /// </summary>
    /// <remarks>
    ///     <paramref name="sourcePosition"/> is the fallback bearing for a gap directly overhead, where there
    ///         is no horizontal direction to the gap at all and the sound genuinely comes from straight up.
    ///         Nothing in a two-dimensional mix can express that, so it points at the sound instead.
    /// </remarks>
    public static Vector2 GetApparentPosition(
        Vector2 sourcePosition,
        Vector2 holePosition,
        Vector2 listenerPosition,
        float verticalDistance)
    {
        var pathLength = GetPathLength(sourcePosition, holePosition, listenerPosition, verticalDistance);

        var toHole = holePosition - listenerPosition;
        if (toHole.Length() >= MinimumDirection)
            return listenerPosition + toHole.Normalized() * pathLength;

        var toSource = sourcePosition - listenerPosition;
        if (toSource.Length() >= MinimumDirection)
            return listenerPosition + toSource.Normalized() * pathLength;

        // Straight overhead and directly above the listener at once: any bearing is as wrong as any other, so
        //      what is left to get right is the distance.
        return listenerPosition + new Vector2(pathLength, 0f);
    }
}
