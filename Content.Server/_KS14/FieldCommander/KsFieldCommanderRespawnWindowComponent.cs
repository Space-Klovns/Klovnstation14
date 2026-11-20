namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     On a field commander rule: dead defenders may respawn only in the last <see cref="Window"/> before the next
///         wave, so that they come back together as reinforcements rather than one at a time. Their respawn timer
///         still counts (and its cooldown and penalty still apply) the rest of the time; one that runs out outside
///         the window waits for the next.
/// </summary>
/// <remarks>
///     A gate on <c>KsCanGhostRespawnEvent</c>. While no wave is scheduled (a phase that sends none) there is no
///         window to wait for: <see cref="OpenWhileNoWaveScheduled"/> says whether respawning is then allowed or
///         refused.
/// </remarks>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderRespawnWindowComponent : Component
{
    /// <summary>
    ///     How long before each wave respawning opens.
    /// </summary>
    /// <seealso cref="KsFieldCommanderRuleSystem.SetRespawnWindow"/>
    // Read only in VV, whose path goes through the setter: a write past it would leave waiting ghosts told the old window.
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Whether ghosts may respawn while no wave is scheduled, as soon as their own timer is up (its cooldown and
    ///         penalty still apply). False refuses every respawn until a wave is scheduled again.
    /// </summary>
    /// <seealso cref="KsFieldCommanderRuleSystem.SetOpenWhileNoWaveScheduled"/>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool OpenWhileNoWaveScheduled = true;

    /// <summary>
    ///     Shown to a ghost that has to wait.
    /// </summary>
    [DataField]
    public LocId Reason = "ks-fieldcommander-respawn-window-closed";
}
