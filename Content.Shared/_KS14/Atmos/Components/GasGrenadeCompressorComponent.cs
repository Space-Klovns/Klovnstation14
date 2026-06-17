using Content.Shared.Atmos;
using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class GasGrenadeCompressorComponent : Component
{
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string InletName = "pipe";

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float TargetPressure = 7600f;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MaxTargetPressure = 7600f;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = false;

    /// <summary>
    ///     Whether it's both enabled and powered.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Active = false;

    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<MaterialPrototype> Material;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string SlotName = "grenade_slot";

    /// <summary>
    ///     Uid of the thing inside, if any.
    ///         Null if none.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? InsertedUid = null;

    /// <summary>
    /// Whitelist of gasses that can be pumped into the grenade.
    /// </summary>
    [DataField]
    public HashSet<Gas> GasWhitelist = new()
    {
        Gas.Oxygen,
        Gas.Nitrogen,
        Gas.NitrousOxide,
        Gas.WaterVapor,
        Gas.Ammonia,
        Gas.Zipion,
        Gas.Argon
    };
}

[Serializable, NetSerializable]
public enum GasGrenadeCompressorVisuals : byte
{
    Active
}

[Serializable, NetSerializable]
public enum GasGrenadeCompressorUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorBoundUserInterfaceState(float targetPressure, bool enabled, bool hasGrenade, float grenadePressure, bool isSpent, int materialAmount) : BoundUserInterfaceState, IEquatable<GasGrenadeCompressorBoundUserInterfaceState>
{
    public float TargetPressure { get; } = targetPressure;
    public bool Enabled { get; } = enabled;
    public bool HasGrenade { get; } = hasGrenade;
    public float GrenadePressure { get; } = grenadePressure;
    public bool IsSpent { get; } = isSpent;
    public int MaterialAmount { get; } = materialAmount;

    /*
        KS14 EXMP: UI state

        Internally, UserInterfaceSystem will not apply a state to the client
            if the new one Equals an already existing one.

        However, the base Equals only checks what backend object is being referenced;
            so it is always inequal.

        Here we want to prevent new states from being added if they are *effectively* the same (even if different objects)
            so this is done.
    */
    public bool Equals(GasGrenadeCompressorBoundUserInterfaceState? other)
    {
        if (other == null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return TargetPressure == other.TargetPressure &&
            Enabled == other.Enabled &&
            HasGrenade == other.HasGrenade &&
            GrenadePressure == other.GrenadePressure &&
            IsSpent == other.IsSpent &&
            MaterialAmount == other.MaterialAmount;
    }

    public override bool Equals(object? otherObj)
        => ReferenceEquals(this, otherObj) || otherObj is GasGrenadeCompressorBoundUserInterfaceState otherState && Equals(otherState);

    public override int GetHashCode()
        => HashCode.Combine(TargetPressure, Enabled, HasGrenade, GrenadePressure, IsSpent, MaterialAmount);
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorRearmMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorChangeTargetPressureMessage(float targetPressure) : BoundUserInterfaceMessage
{
    public float TargetPressure { get; } = targetPressure;
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}
