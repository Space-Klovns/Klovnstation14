using Content.Shared._KS14.Sparks;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.RCD.Components;
using Content.Shared.Teleportation.Components;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
public abstract class SharedSpeczoneSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedSparksSystem _sparksSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeleteOnEnteringSpeczoneComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DeleteOnEnteringSpeczoneComponent, EntParentChangedMessage>(OnEntParentChanged);

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
    private bool CheckEntityIsInSpeczone(EntityUid uid, out TransformComponent transformComponent)
    {
        transformComponent = Transform(uid);
        if (transformComponent.MapUid is not { } mapUid ||
            !HasSpeczoneComponent(mapUid))
            return false;

        return true;
    }

    private void OnStartup(Entity<DeleteOnEnteringSpeczoneComponent> entity, ref ComponentStartup args)
    {
        if (CheckEntityIsInSpeczone(entity, out _))
            PredictedQueueDel(entity);
    }

    private void OnEntParentChanged(Entity<DeleteOnEnteringSpeczoneComponent> entity, ref EntParentChangedMessage args)
    {
        if (CheckEntityIsInSpeczone(entity, out _))
            PredictedQueueDel(entity);
    }

    /// <returns>True if the use of an item was cancelled.</returns>
    private bool TryInterfereUse(EntityUid item, EntityUid? user = null)
    {
        if (!CheckEntityIsInSpeczone(item, out var transformComponent))
            return false;

        _popupSystem.PopupEntity(
            Loc.GetString("speczone-invincibility-use-interrupted", ("entity", Identity.Name(item, EntityManager))),
            item,
            PopupType.SmallCaution
        );

        _sparksSystem.DoSpark(
            transformComponent.Coordinates,
            SharedSparksSystem.DefaultSparkPrototype,
            soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier,
            user: user
        );
        return true;
    }

    private void OnAttemptUseHandTeleporter(ref AttemptUpdateHandTeleporterPortalsEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.Teleporter);
    }

    private void OnAttemptUseRcd(ref AttemptUseRcdEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.RcdUid, user: args.User);
    }
}
