// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class NuclearReactorComponent : Component
{
    public static int ReactorGridWidth = 7;
    public static int ReactorGridHeight = 7;
    public readonly int ReactorOverheatTemp = 1200;
    public readonly int ReactorFireTemp = 1500;
    public readonly int ReactorMeltdownTemp = 2000;

    [DataField]
    public float RadiationLevel = 0;
    [DataField]
    public float ReactorVesselGasVolume = 200;
    [DataField]
    public bool Melted = false;

    // Temperature
    [DataField, AutoNetworkedField]
    public float Temperature = Atmospherics.T20C;
    [DataField(serverOnly: true)]
    public float LastDirtiedTemperature = Atmospherics.T20C;


    [DataField]
    public float ThermalMass = 420 * 2000; // specific heat capacity of steel (420 J/KgK) * mass of reactor (Kg)
    [DataField]
    public float ControlRodInsertion = 2;

    [DataField]
    public bool isSmoking = false;
    [DataField]
    public bool isBurning = false;
    [DataField]
    public string AlertChannel = "Engineering";

    [ViewVariables(VVAccess.ReadOnly)]
    [DataField]
    public float ThermalPower = 0;
    public float[] ThermalPowerL1 = new float[32];
    public float[] ThermalPowerL2 = new float[32];


    [DataField]
    public ItemSlot PartSlot = new();

    // Making this a DataField causes the game to explode, neat
    public ReactorPartComponent?[,] ComponentGrid = new ReactorPartComponent[ReactorGridWidth, ReactorGridHeight];

    // Woe, 3 dimensions be upon ye
    public List<ReactorNeutron>[,] FluxGrid = new List<ReactorNeutron>[ReactorGridWidth, ReactorGridHeight];

    public double[,] TemperatureGrid = new double[ReactorGridWidth, ReactorGridHeight];
    public int[,] NeutronGrid = new int[ReactorGridWidth, ReactorGridHeight];

    public NetEntity[,] VisualGrid = new NetEntity[ReactorGridWidth, ReactorGridHeight];

    public GasMixture? AirContents;

    /// <summary>
    ///     Prefab to apply upon mapinit. Null to apply no prefab.
    /// </summary>
    [DataField]
    public string? Prefab = "normal";

    [DataField("inlet")]
    public string InletName { get; set; } = "inlet";

    [DataField("outlet")]
    public string OutletName { get; set; } = "outlet";

    #region Debug
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("neutrons")]
    public int NeutronCount = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("meltedParts")]
    public int MeltedParts = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("controlRods")]
    public int DetectedControlRods = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("controlRodsInsertion")]
    public float AvgInsertion = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("totalN-Rads")]
    public float TotalNRads = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("totalRads")]
    public float TotalRads = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField("spentFuel")]
    public float TotalSpent = 0;
    #endregion

    // KS14: Sounds
    /// <summary>
    ///     Sound to play when the reactor starts emitting smoke. Can loop.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? WarningAlertSound = null;

    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public NetEntity? WarningAlertSoundUid = null;

    /// <summary>
    ///     Sound to play when the reactor starts being on fire. Can loop.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? DangerAlertSound = null;

    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public NetEntity? DangerAlertSoundUid = null;

    /// <summary>
    ///     Trigger key to signal on this entity when doing meltdown.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string? MeltdownKeyOut = null;

    /// <summary>
    ///     Sound to play when manually silencing reactor alarms.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? ManualSilenceSound = null;

    /// <summary>
    ///     By when can this reactor's temp indicators, radio updates, etc
    ///         update again?
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextIndicatorUpdateBy = TimeSpan.Zero; // Rightfully this should be TimeSpan.MinValue but tests don't like that.

    /// <summary>
    ///     Offset on the current simulation-time to set <see cref="NextIndicatorUpdateBy"/>, when emagged.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan EmagSabotageDelay = TimeSpan.FromSeconds(15);
}
