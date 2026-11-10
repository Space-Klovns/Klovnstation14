using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects.Effects;

public sealed partial class CheckTemperatureTileEffect : KsTileEffect
{
    [DataField] public float? Minimum = null;
    [DataField] public float? Maximum = null;

    /// <summary>
    ///     Whether an open flame in the solution (<see cref="KsTileEffectContext.FlameTemperature"/>) counts toward
    ///         <see cref="Minimum"/>, so that a lit welder dropped in fuel lights it, however cold the fuel. On unless a
    ///         reagent only reacts to being heated through.
    /// </summary>
    [DataField] public bool LitByFlame = true;

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectContext context)
    {
        if (context.Solution is not { } solution)
            return false;

        var temperature = solution.Temperature;

        if (Minimum is { } &&
            temperature < Minimum &&
            !(LitByFlame && context.FlameTemperature >= Minimum))
            return false;

        if (Maximum is { } &&
            temperature > Maximum)
            return false;

        return true;
    }
}
