// SPDX-FileCopyrightText: 2025 Perry Fraser
// SPDX-FileCopyrightText: 2025 metalgearsloth
// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using Content.Shared.Containers.ItemSlots;
using Content.Shared.Guidebook;
using Robust.Shared.GameStates;

namespace Content.Shared.Atmos.Piping.Unary.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true, fieldDeltas: true)] // KS14: Added fielddeltas
public sealed partial class GasCanisterComponent : Component, IGasMixtureHolder
{
    [DataField("port")]
    public string PortName { get; set; } = "port";

    /// <summary>
    ///     Container name for the gas tank holder.
    /// </summary>
    [DataField("container")]
    public string ContainerName { get; set; } = "tank_slot";

    [DataField]
    public ItemSlot GasTankSlot = new();

    [DataField("gasMixture")]
    public GasMixture Air { get; set; } = new();

    /// <summary>
    ///     Last recorded pressure, for appearance-updating purposes.
    /// </summary>
    public float LastPressure = 0f;

    /// <summary>
    ///     Minimum release pressure possible for the release valve.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinReleasePressure = Atmospherics.OneAtmosphere / 10;

    /// <summary>
    ///     Maximum release pressure possible for the release valve.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxReleasePressure = Atmospherics.OneAtmosphere * 10;

    /// <summary>
    ///     Valve release pressure.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ReleasePressure = Atmospherics.OneAtmosphere;

    /// <summary>
    ///     Whether the release valve is open on the canister.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ReleaseValve = false;

    [GuidebookData]
    public float Volume => Air.Volume;

    // KS14
    /// <summary>
    ///     Length of this should be the same as <see cref="EntitySystems.SharedGasTileOverlaySystem.VisibleGasId">
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public byte[] AppearanceGasPercentages = []; // empty array is placeholder; it is properly initialised in ComponentInit

    // KS14
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float NetworkedMoles = 0f;
}
