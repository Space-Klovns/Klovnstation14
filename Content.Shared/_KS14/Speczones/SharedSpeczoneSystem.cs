// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Sparks;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.RCD.Components;
using Content.Shared.Teleportation.Components;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AttemptUpdateHandTeleporterPortalsEvent>(OnAttemptUseHandTeleporter); // Only raised on server
        SubscribeLocalEvent<AttemptUseRcdEvent>(OnAttemptUseRcd);
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
    private bool TryInterfereUse(EntityUid item, EntityUid? user = null, bool predictEffects = false)
    {
        if (!CheckEntityIsInSpeczone(item, out var transformComponent))
            return false;

        _sparksSystem.DoSpark(
            transformComponent.Coordinates,
            SharedSparksSystem.DefaultSparkPrototype,
            soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier,
            user: user
        );

        if (predictEffects && !_gameTiming.IsFirstTimePredicted)
            return true;

        _popupSystem.PopupEntity(
            Loc.GetString("speczone-invincibility-use-interrupted", ("entity", Identity.Name(item, EntityManager))),
            item,
            PopupType.SmallCaution
        );
        return true;
    }

    private void OnAttemptUseHandTeleporter(ref AttemptUpdateHandTeleporterPortalsEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.Teleporter);
    }

    private void OnAttemptUseRcd(ref AttemptUseRcdEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.RcdUid, user: args.User, predictEffects: true);
    }
}
