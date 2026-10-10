using Content.Shared._KS14.IoC;
using Content.Shared._KS14.LockOn;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LockOn;

/// <summary>
///     Plays the lock sounds to the local player, and registers <see cref="KsLockOnIndicatorOverlay"/>.
/// </summary>
public sealed partial class KsLockOnClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private EntityQuery<KsLockOnClientStateComponent> _stateQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsLockOnIndicatorOverlay();
        dependencyCollection.InjectDependencies(overlay, oneOff: true);

        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsLockOnIndicatorOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var localEntity = _playerManager.LocalEntity;
        var curTime = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<KsTargetLockOnComponent>();
        while (enumerator.MoveNext(out var shooterUid, out var lockOnComponent))
        {
            if (localEntity == null || lockOnComponent.Target != localEntity)
            {
                // Forget the lock, so the next one on us announces itself again.
                if (_stateQuery.TryComp(shooterUid, out var staleState))
                    staleState.AnnouncedLockStart = null;

                continue;
            }

            var stateComponent = EnsureComp<KsLockOnClientStateComponent>(shooterUid);

            if (stateComponent.AnnouncedLockStart != lockOnComponent.LockStartTime)
            {
                stateComponent.AnnouncedLockStart = lockOnComponent.LockStartTime;

                // Already finished when we first hear of it: nothing left to warn about.
                stateComponent.LockedSoundPlayed = curTime >= lockOnComponent.LockEndTime;

                if (!stateComponent.LockedSoundPlayed)
                    _audioSystem.PlayGlobal(lockOnComponent.LockingSound, Filter.Local(), recordReplay: false);
            }

            if (!stateComponent.LockedSoundPlayed && curTime >= lockOnComponent.LockEndTime)
            {
                stateComponent.LockedSoundPlayed = true;
                _audioSystem.PlayGlobal(lockOnComponent.LockedSound, Filter.Local(), recordReplay: false);
            }
        }
    }
}
