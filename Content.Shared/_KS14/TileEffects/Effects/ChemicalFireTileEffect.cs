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
    ///     Duration that this chemfire lasts for per scale.
    ///         If null, uses default fixed duration.
    /// </summary>
    [DataField] public TimeSpan? DurationScale = TimeSpan.FromSeconds(1d);

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        var nowExists = _chemicalFireSystem.SpawnChemicalFire(Id, tileRef, duration: DurationScale.HasValue ? DurationScale * scale : null).HasValue;

        // fail and don't consume anything if no chemfire could be created
        if (!nowExists)
            return false;

        reagentData.RemovedVolume += Removed * scale;
        return true;
    }
}
