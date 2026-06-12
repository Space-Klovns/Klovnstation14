using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GasGrenadeCompressorComponent : Component
{
    [DataField, AutoNetworkedField]
    public string InletName = "pipe";

    [DataField, AutoNetworkedField]
    public float TargetPressure = 7600f;

    [DataField, AutoNetworkedField]
    public float MaxTargetPressure = 7600f;

    [DataField, AutoNetworkedField]
    public bool Enabled = false;

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
public enum GasGrenadeCompressorUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorBoundUserInterfaceState : BoundUserInterfaceState
{
    public float TargetPressure { get; }
    public bool Enabled { get; }
    public bool HasGrenade { get; }
    public float GrenadePressure { get; }
    public bool IsSpent { get; }
    public int SteelAmount { get; }

    public GasGrenadeCompressorBoundUserInterfaceState(float targetPressure, bool enabled, bool hasGrenade, float grenadePressure, bool isSpent, int steelAmount)
    {
        TargetPressure = targetPressure;
        Enabled = enabled;
        HasGrenade = hasGrenade;
        GrenadePressure = grenadePressure;
        IsSpent = isSpent;
        SteelAmount = steelAmount;
    }
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorRearmMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorChangeTargetPressureMessage : BoundUserInterfaceMessage
{
    public float TargetPressure { get; }

    public GasGrenadeCompressorChangeTargetPressureMessage(float targetPressure)
    {
        TargetPressure = targetPressure;
    }
}

[Serializable, NetSerializable]
public sealed class GasGrenadeCompressorToggleMessage : BoundUserInterfaceMessage
{
    public bool Enabled { get; }

    public GasGrenadeCompressorToggleMessage(bool enabled)
    {
        Enabled = enabled;
    }
}
