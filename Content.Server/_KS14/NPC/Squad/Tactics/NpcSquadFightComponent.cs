using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     On whoever issues a squad's orders: what the squad is doing together in a fight right now - a push or a flank - and
///         when it may next. See <c>NpcSquadTacticsSystem.Fight.cs</c>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcSquadFightComponent : Component
{
    /// <summary>
    ///     The hostile being pushed on, while a push is on.
    /// </summary>
    [ViewVariables]
    public EntityUid? PushTargetUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan PushStartedAt;

    [ViewVariables]
    public readonly List<EntityUid> Pushers = new();

    [ViewVariables, AutoPausedField]
    public TimeSpan NextPushAt;

    /// <summary>
    ///     The hostile being flanked, while a flank is on.
    /// </summary>
    [ViewVariables]
    public EntityUid? FlankTargetUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan FlankStartedAt;

    [ViewVariables]
    public readonly List<EntityUid> Flankers = new();

    /// <summary>
    ///     How far each flanker had to go when it set off, by its index in <see cref="Flankers"/>: for telling one that cannot
    ///         get round from one on its way.
    /// </summary>
    [ViewVariables]
    public readonly List<float> FlankStartDistances = new();

    [ViewVariables, AutoPausedField]
    public TimeSpan NextFlankAt;

    /// <summary>
    ///     When and where one of the squad's grenades last went off.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan LastBlastAt;

    [ViewVariables]
    public EntityCoordinates LastBlastCoordinates;
}
