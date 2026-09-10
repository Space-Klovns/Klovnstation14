using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects.Effects;

public sealed partial class CheckTemperatureTileEffect : KsTileEffect
{
    [DataField] public float? Minimum = null;
    [DataField] public float? Maximum = null;

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        if (reagentData.Solution is not { } solution)
            return false;

        var temperature = solution.Temperature;

        if (Minimum is { } &&
            temperature < Minimum)
            return false;

        if (Maximum is { } &&
            temperature > Maximum)
            return false;

        return true;
    }
}
