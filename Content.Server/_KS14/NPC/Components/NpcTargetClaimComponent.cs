using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     What an NPC has claimed for itself, so that its squadmates leave it be. See <see cref="NpcTargetClaimSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcTargetClaimSystem))]
public sealed partial class NpcTargetClaimComponent : Component
{
    [ViewVariables]
    public EntityUid? TargetUid;

    /// <summary>
    ///     When the claim lapses, in case the NPC never gets round to it.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan EndTime;
}
