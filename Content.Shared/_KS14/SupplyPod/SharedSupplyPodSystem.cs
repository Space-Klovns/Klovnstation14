using System.Runtime.CompilerServices;
using Robust.Shared.Audio;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.SupplyPod;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
public abstract class SharedSupplyPodSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming GameTiming = default!;

    private static readonly SupplyPodLandedEvent SupplyPodLandedEvent = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!GameTiming.IsFirstTimePredicted)
            return;

        var curTime = GameTiming.CurTime;
        var eqe = EntityQueryEnumerator<ActiveSupplyPodComponent, SupplyPodComponent>();

        while (eqe.MoveNext(out var uid, out var activeSupplyPodComponent, out var supplyPodComponent))
        {
            // Pre-impact
            if (curTime >= activeSupplyPodComponent.FallSoundTime &&
                supplyPodComponent.FallSound is { } fallSound)
            {
                PlayActiveSound(uid, activeSupplyPodComponent, fallSound);

                activeSupplyPodComponent.FallSoundTime = TimeSpan.MaxValue;
                Dirty(uid, activeSupplyPodComponent);
            }

            if (curTime < activeSupplyPodComponent.LaunchFinishTime)
                continue;

            // Impact
            // this infinitely removes and readds the comp on client because comp removal isnt predicted, or something.
            activeSupplyPodComponent.LaunchFinishTime = TimeSpan.MaxValue;

            PlayActiveSound(uid, activeSupplyPodComponent, supplyPodComponent.ImpactSound);

            RaiseLocalEvent(uid, SupplyPodLandedEvent);

            // DEFER BECAUSE PREDICTION OR ANGLO.
            RemCompDeferred(uid, activeSupplyPodComponent);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract void PlayActiveSound(EntityUid uid, ActiveSupplyPodComponent activeSupplyPodComponent, SoundSpecifier? soundSpecifier);
}

/// <summary>
///     Raised by-value on a supply pod when it lands.
/// </summary>
public record struct SupplyPodLandedEvent;
