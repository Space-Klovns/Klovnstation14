using Content.Shared._KS14.Atmos.ChemicalFire;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects.Effects;

public sealed partial class ChemicalFireTileEffect : KsTileEffect
{
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;

    [DataField] public EntProtoId Id = "ChemicalFire";

    /// <summary>
    ///     Amount of removed [something] * scale.
    /// </summary>
    [DataField] public float Removed = 0f;

    /// <summary>
    ///     Duration that this chemfire lasts for per scale, on top of the time until the next update refreshes it.
    ///         If null, uses default fixed duration.
    /// </summary>
    [DataField] public TimeSpan? DurationScale = TimeSpan.FromSeconds(1d);

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        // The fire also lasts until the caller next refreshes it. Without that, it runs out on the tick the next update
        //     would refresh it, and goes out and relights. The scale (the time since the last update) is no stand-in for
        //     the time to the next: a puddle's first update comes sooner than its interval, so it would come up short.
        var duration = DurationScale is { } durationScale
            ? durationScale * scale + (reagentData.RefreshedWithin ?? TimeSpan.FromSeconds(scale))
            : (TimeSpan?)null;

        var nowExists = _chemicalFireSystem.SpawnChemicalFire(Id, tileRef, duration: duration).HasValue;

        // fail and don't consume anything if no chemfire could be created
        if (!nowExists)
            return false;

        reagentData.RemovedVolume += Removed * scale;
        return true;
    }
}
