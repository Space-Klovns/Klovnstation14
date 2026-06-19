using System.Numerics;
using Content.Shared._KS14.Atmos.EntitySystems;
using Content.Shared._KS14.GenericSpriteFlick;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class GasPistonComponent : Component, ISerializationHooks
{
    [DataField, Access(typeof(SharedGasPistonSystem))]
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Extended;

    // dont make this serveronly because GEG?
    [DataField, Access(typeof(SharedGasPistonSystem))]
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<EntityUid> CollidingUids = [];

    [DataField(serverOnly: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? Sound = null;

    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public string InletName;

    /// <summary>
    ///     Id of the fixture that will be used
    ///         for checking whether something collides
    ///         with the extended piston or not.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public string FixtureId;

    /// <summary>
    ///     Ratio (0-1) of gas in the piston, to be leaked to the
    ///         atmosphere when extending.
    /// </summary>
    [DataField(serverOnly: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public float RemovedGasRatio = 0f;

    /// <summary>
    ///     Minimum pressure to stay extended (X),
    ///         and pressure at which maximum damage is done (Y).
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 PressureRange;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier MinimumDamage;

    /// <summary>
    ///     Damage done at maximum pressure.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier MaximumDamage;

    /// <summary>
    ///     Throw force relative to zero.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MaxThrowForce;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public KsSpriteFlickData? FlickData = null;

    /// <summary>
    ///     Should the power actually be capped?
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Capped = true;

    void ISerializationHooks.AfterDeserialization()
    {
        if (PressureRange.X > PressureRange.Y)
            throw new ArgumentException("PressureRange has higher min than max!");

        if (MinimumDamage.GetTotal() > MaximumDamage.GetTotal())
            throw new ArgumentException("MinimumDamage is higher than MaximumDamage!");
    }
}

[Serializable, NetSerializable]
public enum GasPistonVisuals : byte
{
    Extended
}
