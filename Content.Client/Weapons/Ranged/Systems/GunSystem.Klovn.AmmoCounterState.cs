using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Client.Weapons.Ranged.Systems;

// Partial: the ammo counter follows changes the client didn't predict, kept apart from the upstream counter code.
public sealed partial class GunSystem
{
    /// <summary>
    ///     Ammo providers whose server state was applied this frame, refreshed in <see cref="FrameUpdate"/>.
    /// </summary>
    private readonly HashSet<EntityUid> _ammoCountersToRefresh = new();

    // A magazine's rounds, whether loose or in a gun, as the battery's shots are in SharedGunSystem.Battery.cs.
    [SubscribeLocalEvent]
    private void OnBallisticAfterAutoHandleState(Entity<BallisticAmmoProviderComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        UpdateAmmoCount(entity.Owner, prediction: true);
    }

    /// <summary>
    ///     The parts of <see cref="UpdateAmmoCount(EntityUid, bool)"/> upstream lacks. During state application the
    ///         prediction gate drops every update (it isn't a first-time tick), so a change only the server made never
    ///         reached the counter: <paramref name="uid"/> is queued instead, and true returned. The refresh waits for the
    ///         frame's ticks, as re-prediction may yet play the client's own unacknowledged changes over the state.
    ///         Otherwise the gun holding <paramref name="uid"/> (its magazine) is updated too, which has the counter.
    /// </summary>
    private bool KsUpdateAmmoCountOutsideItself(EntityUid uid, bool prediction)
    {
        if (Timing.ApplyingState)
        {
            _ammoCountersToRefresh.Add(uid);
            return true;
        }

        if (prediction && !Timing.IsFirstTimePredicted)
            return false;

        UpdateContainingGunAmmoCount(uid);
        return false;
    }

    private void UpdateContainingGunAmmoCount(EntityUid uid)
    {
        if (Containers.TryGetContainingContainer(uid, out var container) &&
            container.Owner != uid &&
            TryComp<AmmoCounterComponent>(container.Owner, out var gunCounterComponent))
            UpdateAmmoCount((container.Owner, gunCounterComponent));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_ammoCountersToRefresh.Count == 0)
            return;

        foreach (var uid in _ammoCountersToRefresh)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (TryComp<AmmoCounterComponent>(uid, out var counterComponent))
                UpdateAmmoCount((uid, counterComponent));

            UpdateContainingGunAmmoCount(uid);
        }

        _ammoCountersToRefresh.Clear();
    }
}
