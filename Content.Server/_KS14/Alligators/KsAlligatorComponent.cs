using Robust.Shared.Timing;

namespace Content.Server._KS14.Alligators;

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsAlligatorSystem))]
public sealed partial class KsAlligatorComponent : Component
{
    [DataField]
    public TimeSpan RollDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan RollRecovery = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan BiteKnockdown = TimeSpan.FromSeconds(3);

    public EntityUid? Victim;
    public EntityUid? Leg;

    [AutoPausedField]
    public TimeSpan RollEndsAt;

    [AutoPausedField]
    public TimeSpan NextRollAt;
}
