using System.Runtime.CompilerServices;
using Content.Shared._KS14.SupplyPod;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
public sealed class SupplyPodSystem : SharedSupplyPodSystem
{
    [Dependency] private readonly AudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupplyPodComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SupplyPodComponent> entity, ref MapInitEvent args)
    {
        var curTime = GameTiming.CurTime;

        var activeComponent = EnsureComp<ActiveSupplyPodComponent>(entity.Owner);
        activeComponent.LaunchFinishTime = curTime + entity.Comp.FallDuration;
        activeComponent.FallSoundTime = curTime + entity.Comp.FallSoundDelay;
        activeComponent.DestinationCoordinates = Transform(entity).Coordinates;
        Dirty(entity.Owner, activeComponent);
    }

    // Purely for sound to be recorded in replays lol
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void PlayActiveSound(EntityUid uid, ActiveSupplyPodComponent activeSupplyPodComponent, SoundSpecifier? soundSpecifier)
    {
        _audioSystem.PlayStatic(
            soundSpecifier,
            Filter.Empty(),
            activeSupplyPodComponent.DestinationCoordinates,
            true
        );
    }
}
