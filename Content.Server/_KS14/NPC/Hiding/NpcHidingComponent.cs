using Content.Shared.FixedPoint;

namespace Content.Server._KS14.NPC.Hiding;

/// <summary>
///     On an NPC that got into a closet by itself, to heal (<see cref="NpcClosetHealerComponent"/>), for as long as it means
///         to stay. Taken off, it gets out: see <see cref="NpcHidingSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NpcHidingComponent : Component
{
    /// <summary>
    ///     What it got into.
    /// </summary>
    [ViewVariables]
    public EntityUid StorageUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan EnteredAt;

    /// <summary>
    ///     The damage it had when it got in: any more, and the closet is being shot, or worse.
    /// </summary>
    [ViewVariables]
    public FixedPoint2 Damage;

    /// <summary>
    ///     Whether its meds have started to work since it got in: once they stop, it is done.
    /// </summary>
    [ViewVariables]
    public bool WasRecovering;

    /// <summary>
    ///     When it next checks whether to get out.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextCheck;
}
