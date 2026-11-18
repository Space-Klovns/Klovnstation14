using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Hands;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Robust.Shared.Timing;

namespace Content.Shared._Battlefield14.Wieldable;

/// <summary>
///     Runs <see cref="KsWieldDelayComponent"/>: one use delay, started on pickup and again on wielding, that blocks
///         wielding and, when the item says so, firing.
/// </summary>
public sealed partial class KsWieldDelaySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    /// <summary>
    ///     The use delay id, kept apart from the item's own (<see cref="UseDelaySystem.DefaultId"/>), which wielding resets.
    /// </summary>
    public const string WieldDelayId = "KsWieldDelay";

    // On startup, not map init: ModifiedDelay is not saved, so an entity loaded from a map works it out again here.
    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsWieldDelayComponent> entity, ref ComponentStartup args)
    {
        RefreshWieldDelay(entity.AsNullable());
    }

    // By-value class event: the engine has Entity<T> handlers only for ref events.
    [SubscribeLocalEvent]
    private void OnGotEquippedHand(EntityUid uid, KsWieldDelayComponent component, GotEquippedHandEvent args)
    {
        StartDelay((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnItemWielded(Entity<KsWieldDelayComponent> entity, ref ItemWieldedEvent args)
    {
        StartDelay(entity);
    }

    [SubscribeLocalEvent]
    private void OnWieldAttempt(Entity<KsWieldDelayComponent> entity, ref HeldRelayedEvent<WieldAttemptEvent> args)
    {
        // Every held item hears of every wield attempt; only this item's own counts.
        if (args.Args.Cancelled || args.Args.Wielded != entity.Owner)
            return;

        if (GetRemainingDelay(entity) is not { } remaining)
            return;

        args.Args.Cancel();
        _popupSystem.PopupEntity(
            KsPopupMessage.Create("bf14-wield-delay-wield-blocked", ("item", entity.Owner), ("seconds", Math.Round(remaining.TotalSeconds, 1))),
            args.Args.User,
            args.Args.User);
    }

    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsWieldDelayComponent> entity, ref ShotAttemptedEvent args)
    {
        if (!entity.Comp.PreventFiring || args.Cancelled)
            return;

        if (GetRemainingDelay(entity) != null)
            args.Cancel();
    }

    /// <summary>
    ///     Works the delay out again from <see cref="KsWieldDelayComponent.BaseDelay"/>. Call it whenever something that
    ///         handles <see cref="KsGetWieldDelayEvent"/> changes, such as an attachment going on or off.
    /// </summary>
    public void RefreshWieldDelay(Entity<KsWieldDelayComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        var ev = new KsGetWieldDelayEvent(entity.Comp.BaseDelay);
        RaiseLocalEvent(entity, ref ev);

        entity.Comp.ModifiedDelay = ev.Delay > TimeSpan.Zero ? ev.Delay : TimeSpan.Zero;
        Dirty(entity);
    }

    /// <summary>
    ///     How long is left of the delay, or null if it is not running.
    /// </summary>
    public TimeSpan? GetRemainingDelay(Entity<KsWieldDelayComponent> entity)
    {
        if (!_useDelaySystem.TryGetDelayInfo(entity.Owner, out var info, WieldDelayId))
            return null;

        var remaining = info.EndTime - _gameTiming.CurTime;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private void StartDelay(Entity<KsWieldDelayComponent> entity)
    {
        if (entity.Comp.ModifiedDelay <= TimeSpan.Zero)
            return;

        _useDelaySystem.SetLength(entity.Owner, entity.Comp.ModifiedDelay, WieldDelayId);
        _useDelaySystem.TryResetDelay(entity.Owner, id: WieldDelayId);
    }
}
