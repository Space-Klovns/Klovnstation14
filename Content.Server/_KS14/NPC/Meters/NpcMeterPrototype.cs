using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     One kind of NPC meter: a value from 0 to <see cref="Max"/> that something pushes up and time brings back down,
///         such as caution. NPCs hold their readings in <see cref="NpcMetersComponent"/>; anything that wants one asks
///         <see cref="NpcMeterSystem"/> for it by this prototype's id.
/// </summary>
/// <remarks>
///     To add a meter:
///     <list type="number">
///         <item>add one of these;</item>
///         <item>add whatever raises it: a component and system calling <see cref="NpcMeterSystem.Add"/>, like
///             <see cref="NpcMeterOnSquadLossComponent"/>, or <c>AddMeterOperator</c> in HTN;</item>
///         <item>read it wherever it should matter: <c>MeterPrecondition</c>, <c>MeterCon</c>, or a datafield naming
///             the meter on a system's settings, like <c>NpcSquadTacticsSettings.CautionMeter</c>.</item>
///     </list>
/// </remarks>
[Prototype]
public sealed partial class NpcMeterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The highest it goes. Anything added past this is lost.
    /// </summary>
    [DataField]
    public float Max = 100f;

    /// <summary>
    ///     How much it falls each second, steadily, to no lower than 0.
    /// </summary>
    [DataField]
    public float DecayPerSecond = 1f;
}
