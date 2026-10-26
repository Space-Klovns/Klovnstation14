using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Clockwise authoring turns are distinct from the engine's counter-clockwise angles.</summary>
public static class KsProcgenRotation
{
    public static Angle ToEngineAngle(int quarterTurns)
    {
        if (quarterTurns is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(quarterTurns));
        return Angle.FromDegrees(-90.0 * (double) quarterTurns);
    }
}
