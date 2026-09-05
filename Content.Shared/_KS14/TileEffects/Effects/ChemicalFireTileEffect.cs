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

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        _chemicalFireSystem.SpawnChemicalFire(Id, tileRef);
        reagentData.RemovedVolume += Removed * scale;

        return true;
    }
}
