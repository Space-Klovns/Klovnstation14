// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Sparks;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Kept you waiting, huh?
///
///     Manages speczones and loading them. At any moment,
///         a speczone may not exist for any reason and you
///         should not assume that a speczone always exists.
/// </summary>
public abstract class SharedSpeczoneSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedSparksSystem _sparksSystem = default!;

    private EntityQuery<AlwaysAllowedInSpeczoneComponent> _alwaysAllowedQuery;

    public override void Initialize()
    {
        base.Initialize();

        _alwaysAllowedQuery = GetEntityQuery<AlwaysAllowedInSpeczoneComponent>();

        SubscribeLocalEvent<AttemptGeneralSpeczoneInterferableEvent>(OnAttemptInterfere);
        SubscribeLocalEvent<BlockShootingInSpeczoneComponent, ShotAttemptedEvent>(OnAttemptShoot);
    }

    /// <summary>
    ///     Helper for raising AttemptGeneralSpeczoneInterferableEvent
    /// </summary>
    /// <returns>Whether the event was cancelled (interfered).</returns>
    public bool AttemptInterfere(EntityUid uid, EntityUid? user = null, bool predicted = false)
    {
        var ev = new AttemptGeneralSpeczoneInterferableEvent(uid, User: user, Predicted: predicted);
        RaiseLocalEvent(ref ev);

        return ev.Cancelled;
    }

    /// <remarks>
    ///     This is done because you can only HasComp
    ///         a registered comp, not something like an abstract
    ///         component definition.
    /// </remarks>
    /// <returns>Whether the specified entity has a component that derives from <see cref="SharedSpeczoneComponent"/>.</returns>
    protected abstract bool HasSpeczoneComponent(EntityUid uid);

    /// <returns>True if the entity is in a speczone.</returns>
    public bool CheckEntityIsInSpeczone(EntityUid uid, out TransformComponent transformComponent)
    {
        transformComponent = Transform(uid);
        if (transformComponent.MapUid is not { } mapUid ||
            !HasSpeczoneComponent(mapUid))
            return false;

        return true;
    }

    /// <returns>True if the use of an item was cancelled.</returns>
    public bool TryInterfereUse(EntityUid uid, EntityUid? user = null, bool predictEffects = false)
    {
        if (_alwaysAllowedQuery.HasComponent(uid))
            return false;

        if (!CheckEntityIsInSpeczone(user ?? uid, out var transformComponent))
            return false;

        _sparksSystem.DoSpark(
            transformComponent.Coordinates,
            SharedSparksSystem.DefaultSparkPrototype,
            soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier,
            user: predictEffects ? user : null // ts sucks but whatever
        );

        if (predictEffects)
        {
            if (!_gameTiming.IsFirstTimePredicted)
                return true;

            _popupSystem.PopupPredicted(
                Loc.GetString("speczone-invincibility-use-interrupted", ("entity", Identity.Name(uid, EntityManager))),
                uid,
                user,
                PopupType.SmallCaution
            );
        }
        else
        {
            _popupSystem.PopupEntity(
                Loc.GetString("speczone-invincibility-use-interrupted", ("entity", Identity.Name(uid, EntityManager))),
                uid,
                PopupType.SmallCaution
            );
        }

        return true;
    }

    private void OnAttemptInterfere(ref AttemptGeneralSpeczoneInterferableEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.Uid, user: args.User, predictEffects: args.Predicted);
    }

    private void OnAttemptShoot(Entity<BlockShootingInSpeczoneComponent> entity, ref ShotAttemptedEvent args)
    {
        if (TryInterfereUse(entity.Owner, user: args.User, predictEffects: true))
            args.Cancel();
    }
}

/// <summary>
///     General-use event raised on things that can be blocked in speczones.
/// </summary>
[ByRefEvent]
public record struct AttemptGeneralSpeczoneInterferableEvent(EntityUid Uid, EntityUid? User = null, bool Predicted = false, bool Cancelled = false);
