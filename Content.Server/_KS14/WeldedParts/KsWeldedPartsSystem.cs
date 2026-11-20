using System.Numerics;
using Content.Shared.Destructible;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.WeldedParts;

/// <summary>
///     Builds what <see cref="KsWeldedPartsComponent"/> describes and keeps its parts together: welded on at map init, and
///         gone together, whichever goes first. A part destroyed destroys the owner, so whatever pays for or counts a
///         destruction (a kill reward, a spawner's tally) hears it once, on the owner.
/// </summary>
public sealed partial class KsWeldedPartsSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedJointSystem _jointSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private EntityQuery<KsWeldedPartComponent> _partQuery = default!;
    [Dependency] private EntityQuery<KsWeldedPartsComponent> _partsQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    private const string JointIdPrefix = "ks-welded-part-";

    public override void Initialize()
    {
        base.Initialize();
        _mapLoaderSystem.OnIsSerializable += OnIsSerializable;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _mapLoaderSystem.OnIsSerializable -= OnIsSerializable;
    }

    /// <summary>
    ///     A welded whole is not saved once it is built: neither its parts, which an owner saved on its own would weld on again
    ///         when loaded, and which an owner not saved would leave behind on their own, nor its owner, whose weld joints
    ///         point at them. Not yet built (on a map not yet initialised, as when mapping), the owner is saved, and builds
    ///         itself when the map starts. A part's prototype stays savable, for one placed by hand.
    /// </summary>
    private void OnIsSerializable(Entity<MetaDataComponent> entity, ref bool serializable)
    {
        if (!serializable)
            return;

        if (_partQuery.HasComp(entity.Owner) ||
            _partsQuery.TryComp(entity.Owner, out var partsComponent) && partsComponent.Spawned.Count > 0)
            serializable = false;
    }

    /// <summary>
    ///     A round fired by one part of the whole passes through the rest of it: a turret's shell starts inside the hull it
    ///         sits on, and the projectile only ignores the turret itself.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRootPreventCollide(Entity<KsWeldedPartsComponent> entity, ref PreventCollideEvent args)
    {
        if (!args.Cancelled && IsShotPassingThrough(entity.Owner, args.OtherEntity))
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnPartPreventCollide(Entity<KsWeldedPartComponent> entity, ref PreventCollideEvent args)
    {
        if (!args.Cancelled && IsShotPassingThrough(entity.Comp.Root, args.OtherEntity))
            args.Cancelled = true;
    }

    /// <summary>
    ///     Whether <paramref name="projectileUid"/> passes through the whole of <paramref name="rootUid"/>: fired by it or one
    ///         of its parts, or by a welded whole friendly to it. Two tanks of one side shoot past each other, as their gun
    ///         sights look past each other (<see cref="IsFriendlyWhole"/>); anything else firing hits them.
    /// </summary>
    private bool IsShotPassingThrough(EntityUid rootUid, EntityUid projectileUid)
    {
        if (!_projectileQuery.TryComp(projectileUid, out var projectileComponent))
            return false;

        foreach (var shooterUid in new[] { projectileComponent.Shooter, projectileComponent.Weapon })
        {
            if (shooterUid is { } uid && IsFriendlyWhole(uid, rootUid))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The whole <paramref name="uid"/> is part of: its root, or itself if it is one. False for anything not welded.
    /// </summary>
    public bool TryGetWhole(EntityUid uid, out EntityUid rootUid)
    {
        if (_partsQuery.HasComp(uid))
        {
            rootUid = uid;
            return true;
        }

        return TryGetRoot(uid, out rootUid);
    }

    /// <summary>
    ///     Whether <paramref name="vehicleUid"/> belongs to a welded whole that <paramref name="shooterUid"/> is part of, or
    ///         that <paramref name="shooterUid"/>, itself part of a welded whole, is friendly to: what a tank's gun sees and
    ///         shoots past. Only welded wholes: a lone gun or a mob is not let through this way.
    /// </summary>
    public bool IsFriendlyWhole(EntityUid shooterUid, EntityUid vehicleUid)
    {
        if (!TryGetWhole(vehicleUid, out var vehicleRootUid) || !TryGetWhole(shooterUid, out var shooterRootUid))
            return false;

        return vehicleRootUid == shooterRootUid || _npcFactionSystem.IsEntityFriendly(shooterRootUid, vehicleRootUid);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is <paramref name="rootUid"/> or one of the parts welded to it.
    /// </summary>
    public bool IsOfWhole(EntityUid rootUid, EntityUid? uid)
    {
        if (uid is not { } someUid)
            return false;

        if (someUid == rootUid)
            return true;

        return _partQuery.TryComp(someUid, out var partComponent) && partComponent.Root == rootUid;
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsWeldedPartsComponent> entity, ref MapInitEvent args)
    {
        var ownerTransform = Transform(entity);
        var rotation = _transformSystem.GetWorldRotation(ownerTransform);

        // The owner's coordinates are its parent's (a grid's), so the offset turns with its rotation on that parent, not
        //      in the world: a turned grid would otherwise turn it twice.
        var localRotation = ownerTransform.LocalRotation;

        for (var i = 0; i < entity.Comp.Parts.Count; i++)
        {
            var part = entity.Comp.Parts[i];
            var partUid = Spawn(part.Prototype, ownerTransform.Coordinates.Offset(localRotation.RotateVec(part.Offset)));
            _transformSystem.SetWorldRotation(partUid, rotation);

            EnsureComp<KsWeldedPartComponent>(partUid).Root = entity.Owner;
            entity.Comp.Spawned.Add(partUid);

            if (part.CopyFactions)
                CopyFactions(entity.Owner, partUid);

            var joint = _jointSystem.CreateWeldJoint(entity.Owner, partUid, id: JointIdPrefix + i);
            joint.LocalAnchorA = part.Offset;
            joint.LocalAnchorB = Vector2.Zero;
            joint.Stiffness = part.AngularStiffness;
            joint.Damping = part.AngularDamping;
            joint.CollideConnected = false;
        }
    }

    private void CopyFactions(EntityUid fromUid, EntityUid toUid)
    {
        if (!_factionQuery.TryComp(fromUid, out var fromComponent))
            return;

        var factions = new HashSet<ProtoId<NpcFactionPrototype>>(fromComponent.Factions);
        var to = new Entity<NpcFactionMemberComponent?>(toUid, EnsureComp<NpcFactionMemberComponent>(toUid));
        _npcFactionSystem.ClearFactions(to, dirty: false);
        _npcFactionSystem.AddFactions(to, factions);
    }

    /// <summary>
    ///     Whatever is welded to <paramref name="rootUid"/>, while it lasts.
    /// </summary>
    public IReadOnlyList<EntityUid> GetParts(EntityUid rootUid)
    {
        return _partsQuery.TryComp(rootUid, out var partsComponent) ? partsComponent.Spawned : Array.Empty<EntityUid>();
    }

    /// <summary>
    ///     What <paramref name="uid"/> is a welded part of, if it is one.
    /// </summary>
    public bool TryGetRoot(EntityUid uid, out EntityUid rootUid)
    {
        rootUid = default;
        if (!_partQuery.TryComp(uid, out var partComponent) || TerminatingOrDeleted(partComponent.Root))
            return false;

        rootUid = partComponent.Root;
        return true;
    }

    /// <summary>
    ///     A part destroyed is the whole thing destroyed: the owner goes the way any destroyed thing does, and takes the
    ///         rest with it.
    /// </summary>
    // DestructionEventArgs is a by-value class event, so the engine can't give this an Entity<T>.
    [SubscribeLocalEvent]
    private void OnPartDestroyed(EntityUid uid, KsWeldedPartComponent component, DestructionEventArgs args)
    {
        var rootUid = component.Root;
        if (TerminatingOrDeleted(rootUid) || EntityManager.IsQueuedForDeletion(rootUid))
            return;

        _destructibleSystem.DestroyEntity(rootUid);
    }

    /// <summary>
    ///     A part gone any other way (deleted outright) takes the owner with it too, quietly.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPartShutdown(Entity<KsWeldedPartComponent> entity, ref ComponentShutdown args)
    {
        var rootUid = entity.Comp.Root;
        if (_partsQuery.TryComp(rootUid, out var partsComponent))
            partsComponent.Spawned.Remove(entity.Owner);

        if (!Terminating(entity.Owner) || TerminatingOrDeleted(rootUid) || EntityManager.IsQueuedForDeletion(rootUid))
            return;

        QueueDel(rootUid);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsWeldedPartsComponent> entity, ref ComponentShutdown args)
    {
        foreach (var partUid in entity.Comp.Spawned)
        {
            if (!TerminatingOrDeleted(partUid))
                QueueDel(partUid);
        }

        entity.Comp.Spawned.Clear();
    }
}
