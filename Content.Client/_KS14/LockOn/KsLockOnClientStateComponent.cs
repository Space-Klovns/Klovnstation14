using Content.Shared._KS14.LockOn;

namespace Content.Client._KS14.LockOn;

/// <summary>
///     Client-only bookkeeping on a shooter, so the lock sounds play once per lock on the local player.
/// </summary>
[RegisterComponent, Access(typeof(KsLockOnClientSystem))]
public sealed partial class KsLockOnClientStateComponent : Component
{
    /// <summary>
    ///     <see cref="KsTargetLockOnComponent.LockStartTime"/> of the lock already announced; a different value is a new lock.
    /// </summary>
    [ViewVariables]
    public TimeSpan? AnnouncedLockStart;

    [ViewVariables]
    public bool LockedSoundPlayed;
}
