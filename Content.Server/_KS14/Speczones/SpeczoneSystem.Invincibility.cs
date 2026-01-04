using Content.Server.Atmos.Components;
using Content.Shared._KS14.Sparks;
using Content.Shared.Damage.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Teleportation.Components;
using Content.Shared.Wires;

namespace Content.Server._KS14.Speczones;

// This file handles making speczones invincible-ish

public sealed partial class SpeczoneSystem : EntitySystem
{
    private void SetupInvincibility()
    {
        SubscribeLocalEvent<AttemptUpdateHandTeleporterPortalsEvent>(OnAttemptUseHandTeleporter);
    }

    private void OnAttemptUseHandTeleporter(ref AttemptUpdateHandTeleporterPortalsEvent args)
    {
        var teleporterTransform = Transform(args.Teleporter);
        if (teleporterTransform.MapUid is not { } mapUid ||
            !_speczoneQuery.TryGetComponent(mapUid, out var speczoneComponent) ||
            !speczoneComponent.Prototype.PreventHandTeleporter)
            return;

        args.Cancelled = true;

        _popupSystem.PopupEntity(
            Loc.GetString("speczone-invincibility-handtele-interrupted", ("entity", Identity.Name(args.Teleporter, EntityManager))),
            args.Teleporter,
            Shared.Popups.PopupType.SmallCaution
        );

        _sparksSystem.DoSpark(teleporterTransform.Coordinates, SharedSparksSystem.DefaultSparkPrototype, soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier);
    }

    /// <summary>
    ///     Processes invincibility of all speczone entities.
    /// </summary>
    private void ProcessSpeczoneInvincibility(SpeczonePrototype prototype, EntityUid mapUid)
    {
        if (!prototype.MakeAirtightInvincible)
            return;

        var eqe = EntityQueryEnumerator<AirtightComponent, DamageableComponent, TransformComponent>();
        while (eqe.MoveNext(out var uid, out var _, out var damageableComponent, out var transformComponent))
        {
            if (transformComponent.MapUid is not { } otherMapUid ||
                otherMapUid != mapUid)
                continue;

            RemComp(uid, damageableComponent);

            if (_rcdDeconstructableQuery.TryGetComponent(uid, out var rcdDeconstructableComponent))
                RemComp(uid, rcdDeconstructableComponent);

            if (_doorQuery.HasComponent(uid) &&
                TryComp<WiresPanelComponent>(uid, out var wirePanelComponent))
                RemComp(uid, wirePanelComponent);
        }
    }
}
