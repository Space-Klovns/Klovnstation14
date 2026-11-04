namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     An inline pipe vessel holding a catalyst bed. The reactions the bed catalyses run in the pipe net the reactor is
///         part of, as fast as the bed's share of that net and its activity allow, so a reactor between two pumps is a
///         reactor whose residence time is its net's volume over the flow.
/// </summary>
[RegisterComponent, Access(typeof(KsCatalyticReactorSystem))]
public sealed partial class KsCatalyticReactorComponent : Component
{
    [DataField]
    public string NodeName = "pipe";

    [DataField]
    public string CatalystSlotId = "catalyst";

    /// <summary>
    ///     Litres of the net the bed fills. The reactions run at full speed when the net is no bigger than this, and
    ///         slower in proportion as more pipe dilutes it.
    /// </summary>
    [DataField]
    public float BedVolume = 400f;

    /// <summary>
    ///     Whether the bed got anything done on the last update, which is what the sprite shows.
    /// </summary>
    [ViewVariables]
    public bool Reacting;
}
