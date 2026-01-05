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

    private EntityQuery<SharedSpeczoneComponent> _sharedSpeczoneQuery;

    public override void Initialize()
    {
        base.Initialize();

        _sharedSpeczoneQuery = GetEntityQuery<SharedSpeczoneComponent>();

        SubscribeLocalEvent<AttemptUpdateHandTeleporterPortalsEvent>(OnAttemptUseHandTeleporter);
        SubscribeLocalEvent<AttemptUseRcdEvent>(OnAttemptUseRcd);
    }

    /// <returns>True if the use of an item was cancelled.</returns>
    private bool TryInterfereUse(EntityUid item)
    {
        var teleporterTransform = Transform(item);
        if (teleporterTransform.MapUid is not { } mapUid ||
            !_sharedSpeczoneQuery.HasComponent(mapUid))
            return false;

        _popupSystem.PopupEntity(
            Loc.GetString("speczone-invincibility-use-interrupted", ("entity", Identity.Name(item, EntityManager))),
            item,
            PopupType.SmallCaution
        );

        _sparksSystem.DoSpark(teleporterTransform.Coordinates, SharedSparksSystem.DefaultSparkPrototype, soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier);
        return true;
    }

    private void OnAttemptUseHandTeleporter(ref AttemptUpdateHandTeleporterPortalsEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.Teleporter);
    }

    private void OnAttemptUseRcd(ref AttemptUseRcdEvent args)
    {
        args.Cancelled |= TryInterfereUse(args.RcdUid);
    }
}
