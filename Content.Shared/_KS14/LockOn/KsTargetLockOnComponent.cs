using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.LockOn;

/// <summary>
///     Put on a shooter. It must hold a lock on its target for <see cref="LockOnTime"/> before it may fire.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedKsTargetLockOnSystem))]
public sealed partial class KsTargetLockOnComponent : Component
{
    /// <summary>
    ///     Time from first acquiring a target in line of sight to being allowed to fire.
    /// </summary>
    [DataField]
    public TimeSpan LockOnTime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     If sight of the locked target is lost for longer than this, the lock resets.
    /// </summary>
    [DataField]
    public TimeSpan LoseLockGrace = TimeSpan.FromSeconds(0.5);

    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan LockStartTime;

    /// <summary>
    ///     Firing is allowed at or after this time.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan LockEndTime;

    /// <summary>
    ///     Server-only bookkeeping, not networked.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? LostSightTime;

    /// <summary>
    ///     Colour of the on-screen reticle shown to the target.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color IndicatorColor = Color.Red;

    /// <summary>
    ///     Played client-side to the target only, when a lock on them starts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? LockingSound;

    /// <summary>
    ///     Played client-side to the target only, when the lock completes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? LockedSound;
}
