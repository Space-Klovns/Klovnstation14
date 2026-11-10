using Content.Shared.Projectiles;
using Content.Shared._Trauma.Projectiles;
using Robust.Client.GameObjects;
using Robust.Client.Physics;

namespace Content.Client._Trauma.Projectiles;

/// <summary>
/// Hides the server-spawned projectile when firing a predicted gun.
/// KS14: hiding moved to Content.Client._KS14.PredictedSpawning.KsPredictedSpawnSystem, which keeps the shooter's own copy
/// </summary>
public sealed partial class PredictedProjectileSystem : EntitySystem
{
    /* [Dependency] private SpriteSystem _sprite = default!; */ // KS14: removed, see the summary
    [Dependency] private Robust.Client.Player.IPlayerManager _playerManager = default!; // KS14

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
        /* SubscribeNetworkEvent<ShotPredictedProjectileEvent>(OnShotPredictedProjectile); */ // KS14: removed, see the summary
    }

    private void OnUpdateIsPredicted(Entity<ProjectileComponent> ent, ref UpdateIsPredictedEvent args)
    {
        // KS14 start: only this client's own shots run ahead of the server. Anyone else's, predicted, ran the client's
        //      whole lead ahead of its last state, so it appeared that far from where it was fired (#410).
        if (IsClientSide(ent) || ent.Comp.Shooter is { } shooterUid && shooterUid == _playerManager.LocalEntity)
            args.IsPredicted = true;
        // KS14 end
    }

    // KS14: removed, see the summary
    /*
    private void OnShotPredictedProjectile(ShotPredictedProjectileEvent args)
    {
        var uid = GetEntity(args.Projectile);
        if (uid.IsValid()) // client may not have received the projectile state yet
            _sprite.SetVisible(uid, false);
    }
    */
}
