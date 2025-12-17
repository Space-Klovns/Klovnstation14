// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Dataset;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class TurbineComponent : Component
{
    /// <summary>
    /// Power generated last tick
    /// </summary>
    [DataField]
    public float LastGen = 0;

    /// <summary>
    /// Watts per revolution
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StatorLoad = 35000;

    /// <summary>
    /// Current RPM of turbine
    /// </summary>
    [DataField("RPM"), AutoNetworkedField]
    public float RPM = 0;

    /// <summary>
    /// Turbine's resistance to change in RPM
    /// </summary>
    [DataField]
    public float TurbineMass = 1000;

    /// <summary>
    /// Most efficient power generation at this value, overspeed at 1.2*this
    /// </summary>
    [DataField]
    public float BestRPM = 600;

    /// <summary>
    /// Volume of gas to process per tick for power generation
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FlowRate = Atmospherics.MaxTransferRate;

    /// <summary>
    /// Maximum volume of gas to process per tick
    /// </summary>
    [DataField]
    public float FlowRateMax = Atmospherics.MaxTransferRate * 5;

    /// <summary>
    /// Max/min temperatures
    /// </summary>
    [DataField]
    public float MaxTemp = 3000;
    [DataField]
    public float MinTemp = Atmospherics.T20C;

    /// <summary>
    /// The damage this turbine takes every time its overspeed.
    /// </summary>
    [DataField("overspeedDamage", required: true), AutoNetworkedField]
    public DamageSpecifier BladeOverspeedDamage;

    /// <summary>
    /// Amount of damage this turbine can be at before its blade would be
    /// ruined. Dynamically evaluated from the entity's DestructibleComponent.
    /// </summary>
    [AutoNetworkedField]
    public FixedPoint2 BladeBreakingPoint = new();

    /// <summary>
    /// Damage messages shown on examine.
    /// </summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype>? DamageMessages = "TurbineDamageMessages";

    /// <summary>
    /// If the turbine is functional or not
    /// </summary>
    [DataField]
    public bool Ruined = false;

    /// <summary>
    /// Flag for indicating that energy available is less than needed to turn the turbine
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Stalling = false;

    /// <summary>
    /// Flag for RPM being > BestRPM*1.2
    /// </summary>
    public bool Overspeed => RPM > BestRPM * 1.2;

    /// <summary>
    /// Flag for gas tempurature being > MaxTemp - 500
    /// </summary>
    [DataField]
    public bool Overtemp = false;

    /// <summary>
    /// Flag for gas tempurature being < MinTemp
    /// </summary>
    [DataField]
    public bool Undertemp = false;

    /// <summary>
    /// Adjustment for power generation
    /// </summary>
    [DataField]
    public float PowerMultiplier = 1;

    [DataField]
    public EntityUid? AlarmAudioOvertemp;

    [DataField]
    public EntityUid? AlarmAudioUnderspeed;

    [DataField("inlet")]
    public string InletName { get; set; } = "inlet";

    [DataField("outlet")]
    public string OutletName { get; set; } = "outlet";

    public bool IsSparking = false;
    public bool IsSmoking = false;

    //Debugging
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("HasPipes")]
    public bool HasPipes = false;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("SupplierMaxSupply")]
    public float SupplierMaxSupply = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("LastVolumeTransfer")]
    public float LastVolumeTransfer = 0;

}
