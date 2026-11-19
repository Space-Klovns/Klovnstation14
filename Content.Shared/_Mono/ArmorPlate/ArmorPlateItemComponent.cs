// BF14-BLU: ported from Monolith via Battlefield14 and reworked, see ArmorPlateSystem
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
///     An armor plate, active while it is the first plate in a worn <see cref="ArmorPlateHolderComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ArmorPlateSystem))]
public sealed partial class ArmorPlateItemComponent : Component
{
    /// <summary>
    ///     How much damage the plate takes before it breaks, for examine. Match it to the plate's destruction threshold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxDurability = 100;

    /// <summary>
    ///     The wearer's walk speed modifier while the plate is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1.0f;

    /// <summary>
    ///     The wearer's sprint speed modifier while the plate is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1.0f;

    /// <summary>
    ///     The share of absorbed damage the wearer takes as stamina damage instead.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaDamageMultiplier = 1.0f;

    /// <summary>
    ///     How much of the wearer's damage the plate takes as durability damage, by damage type. Defaults to 1.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> DamageMultipliers = new();

    /// <summary>
    ///     The share of each damage type the plate absorbs. A negative ratio adds that share to the damage instead, and
    ///         the plate still takes durability damage for it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> AbsorptionRatios = new();
}
