namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     On anything that has fired a gun lately, NPC or not: when it last did, which anyone near could hear. What NPCs go by
///         to tell a hostile that has stopped shooting - reloading, or out - from one that never started. See
///         <see cref="NpcGunfireSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcGunfireSystem))]
public sealed partial class NpcRecentShooterComponent : Component
{
    [ViewVariables, AutoPausedField]
    public TimeSpan LastShotAt;
}
