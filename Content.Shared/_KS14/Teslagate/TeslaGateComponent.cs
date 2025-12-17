// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.TeslaGate;

/// <summary>
///     This is used for tesla gate and storing the time it has to / from the next pulse
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TeslaGateComponent : Component
{
    /// <summary>
    ///     When can the gate pulse again?
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextPulse = TimeSpan.Zero;

    /// <summary>
    ///     Last time the gate turned on.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan LastShockTime = TimeSpan.Zero;

    [DataField]
    public DamageSpecifier ShockDamage = new();

    public bool CurrentlyShocking = false;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<NetEntity> ThingsBeingShocked = new();

    /// <summary>
    ///     If true, then this teslagate was disabled
    ///         because of a power change.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool WasDisabledByPower = false;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = false;

    /// <summary>
    ///     Whether this tesla gate's timer is cut.
    ///         Determines whether the gate will automatically turn on or off.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool IsTimerWireCut = false;

    /// <summary>
    ///     Whether this tesla gate's aux wire is cut.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool IsAuxWireCut = false;

    /// <summary>
    ///     Whether this tesla gate's pulse interval is hacked via wires.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool IsIntervalHacked = false;

    /// <summary>
    ///     Whether this tesla gate is hacked to force it to be on.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool IsForceHacked = false;

    /// <summary>
    ///     Which alert levels does the tesla gate automatically turn on at?
    ///         Must be lowercase.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public List<string> EnabledAlertLevels = new() { "red", "violet", "epsilon", "delta" };


    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan PulseInterval = TimeSpan.FromSeconds(3);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ShockLength = TimeSpan.FromSeconds(1);


    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan DefaultPulseInterval = TimeSpan.FromSeconds(3);
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan HackedPulseInterval = TimeSpan.FromSeconds(1.5);


    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? ShockSound = new SoundPathSpecifier(new ResPath("/Audio/Effects/Lightning/lightningshock.ogg"));

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? StartingSound = new SoundPathSpecifier(new ResPath("/Audio/Effects/poster_being_set.ogg"));
}

[Serializable, NetSerializable]
public enum TeslaGateVisuals : byte { ShockingState }

[Serializable, NetSerializable]
public enum TeslaGateVisualState : byte
{
    Inactive,
    Active,
}

[Serializable, NetSerializable]
public enum TeslaGateSafetyWireKey : byte { StatusKey }

[Serializable, NetSerializable]
public enum TeslaGateForceWireKey : byte { StatusKey }

[Serializable, NetSerializable]
public enum TeslaGateAuxWireKey : byte { StatusKey }
