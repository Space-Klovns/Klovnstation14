namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     On a field commander rule: dead defenders may respawn only in the last <see cref="Window"/> before the next
///         wave, so that they come back together as reinforcements rather than one at a time. Their respawn timer
///         still counts (and its cooldown and penalty still apply) the rest of the time; one that runs out outside
///         the window waits for the next.
/// </summary>
/// <remarks>
///     A gate on <c>KsCanGhostRespawnEvent</c>; while no wave is scheduled, respawning is refused.
/// </remarks>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderRespawnWindowComponent : Component
{
    /// <summary>
    ///     How long before each wave respawning opens.
    /// </summary>
    [DataField]
    public TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Shown to a ghost that has to wait.
    /// </summary>
    [DataField]
    public LocId Reason = "ks-fieldcommander-respawn-window-closed";
}
