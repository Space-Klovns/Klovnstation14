using System.Linq;
using Content.Shared._KS14.Damage.Events;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageContactsSystem
{
    /// <summary>
    ///     How fast, in metres a second, an entity has to be going to count as moving for
    ///         <see cref="DamageContactsComponent.OnlyWhileMoving"/>. Well below a slowed walk, well above the creep of a
    ///         body coming to rest.
    /// </summary>
    public const float KsMovingSpeed = 0.1f;

    [Dependency] private EntityQuery<PhysicsComponent> _ksPhysicsQuery = default!;
    [Dependency] private EntityQuery<DamagedByContactComponent> _ksDamagedQuery = default!;

    /// <summary>
    ///     The sources being worked on right now: what a refresh is putting together, or a copy of what a damage tick is
    ///         going through, as damage can end a contact and so change the list it came from.
    /// </summary>
    private readonly List<EntityUid> _ksSources = new();

    private readonly HashSet<string> _ksClaimedGroups = new();

    /// <summary>
    ///     What each source dealt this damage tick, to wear it by once the tick is done.
    /// </summary>
    private readonly List<(EntityUid SourceUid, FixedPoint2 Dealt)> _ksDealt = new();

    /// <summary>
    ///     Works out again which of the sources <paramref name="otherUid"/> touches count for it
    ///         (<see cref="DamagedByContactComponent.Sources"/>): those that already did and are still touched, in the
    ///         order they were, then any other whose <see cref="DamageContactsComponent.ExclusiveGroup"/> none of them has.
    ///         With none left the entity is no longer damaged by contact.
    /// </summary>
    /// <param name="startedUid">A source whose contact is just starting, which may not be among its contacts yet.</param>
    /// <param name="endedUid">A source whose contact is just ending, which is still among them.</param>
    private void KsRefreshSources(EntityUid otherUid, EntityUid? startedUid = null, EntityUid? endedUid = null)
    {
        if (!_ksPhysicsQuery.TryComp(otherUid, out var physicsComponent))
            return;

        var contactingUids = _physics.GetContactingEntities(otherUid, physicsComponent);
        if (startedUid != null)
            contactingUids.Add(startedUid.Value);
        if (endedUid != null)
            contactingUids.Remove(endedUid.Value);

        _ksSources.Clear();
        _ksClaimedGroups.Clear();
        var ungroupedClaimed = false;
        var hasComponent = _ksDamagedQuery.TryComp(otherUid, out var damagedComponent);

        if (hasComponent)
        {
            foreach (var sourceUid in damagedComponent!.Sources)
            {
                if (contactingUids.Remove(sourceUid) &&
                    _damageQuery.TryComp(sourceUid, out var sourceComponent) &&
                    KsTryClaimGroup(sourceComponent.ExclusiveGroup, ref ungroupedClaimed))
                    _ksSources.Add(sourceUid);
            }
        }

        foreach (var contactingUid in contactingUids)
        {
            if (_damageQuery.TryComp(contactingUid, out var sourceComponent) &&
                !_whitelistSystem.IsWhitelistPass(sourceComponent.IgnoreWhitelist, otherUid) &&
                KsTryClaimGroup(sourceComponent.ExclusiveGroup, ref ungroupedClaimed))
                _ksSources.Add(contactingUid);
        }

        if (_ksSources.Count == 0)
        {
            if (hasComponent)
                RemComp<DamagedByContactComponent>(otherUid);

            return;
        }

        if (hasComponent && damagedComponent!.Sources.SequenceEqual(_ksSources))
            return;

        damagedComponent = EnsureComp<DamagedByContactComponent>(otherUid);
        damagedComponent.Sources = new List<EntityUid>(_ksSources);
        // Upstream's field, which nothing here reads any more: kept as upstream would have it, the first source's.
        damagedComponent.Damage = _damageQuery.Comp(_ksSources[0]).Damage;
        Dirty(otherUid, damagedComponent);
    }

    /// <summary>
    ///     Whether a source of <paramref name="group"/> may count beside those that already do: the first of its group,
    ///         or the first of those with none.
    /// </summary>
    private bool KsTryClaimGroup(string? group, ref bool ungroupedClaimed)
    {
        if (group != null)
            return _ksClaimedGroups.Add(group);

        if (ungroupedClaimed)
            return false;

        ungroupedClaimed = true;
        return true;
    }

    /// <summary>
    ///     Whether contact damage that is due lands now: whether any source that counts would hurt the entity as it is.
    ///         A source that only hurts the moving holds back while the entity stands still, and if they all do the tick
    ///         is not moved on, so the first step after standing still hurts at once and the usual cadence runs from
    ///         there.
    /// </summary>
    private bool KsCanTakeContactDamage(Entity<DamagedByContactComponent> entity)
    {
        var moving = KsIsMoving(entity.Owner);

        foreach (var sourceUid in entity.Comp.Sources)
        {
            if (_damageQuery.TryComp(sourceUid, out var sourceComponent) && KsHurtsNow(sourceComponent, moving))
                return true;
        }

        return false;
    }

    private static bool KsHurtsNow(DamageContactsComponent sourceComponent, bool moving)
    {
        return moving || !sourceComponent.OnlyWhileMoving;
    }

    /// <summary>
    ///     The body's own velocity, relative to what it stands on: what a player's input or an NPC's steering sets, and
    ///         what a push or a pull gives it. Predicted for the local player's mob, from the server's state otherwise.
    /// </summary>
    private bool KsIsMoving(EntityUid uid)
    {
        return _ksPhysicsQuery.TryComp(uid, out var physicsComponent)
               && physicsComponent.LinearVelocity.LengthSquared() > KsMovingSpeed * KsMovingSpeed;
    }

    /// <summary>
    ///     A damage tick: each source that counts deals its own damage, whatever cares is told of each that landed
    ///         (<c>KsDamagedSoundSystem</c>'s rattle), and then each source is worn by what it dealt
    ///         (<see cref="DamageContactsComponent.SelfDamageRatio"/>). Worn last: wear is damage to the source, which has
    ///         a sound of its own to make, and the rattle of the contact, the one a client predicts, is to come first.
    /// </summary>
    private void KsDealContactDamage(Entity<DamagedByContactComponent> entity)
    {
        var moving = KsIsMoving(entity.Owner);
        _ksDealt.Clear();
        _ksSources.Clear();
        _ksSources.AddRange(entity.Comp.Sources);

        foreach (var sourceUid in _ksSources)
        {
            // Gone, or on a client not in view: the server's state settles what it did.
            if (!_damageQuery.TryComp(sourceUid, out var sourceComponent) || !KsHurtsNow(sourceComponent, moving))
                continue;

            if (_damageable.TryChangeDamage(entity.Owner, sourceComponent.Damage, out var dealtDamage, interruptsDoAfters: false))
                _ksDealt.Add((sourceUid, dealtDamage.GetTotal()));
        }

        if (_ksDealt.Count == 0)
            return;

        foreach (var (sourceUid, _) in _ksDealt)
        {
            var damagedEvent = new KsDamagedByContactEvent(sourceUid);
            RaiseLocalEvent(entity.Owner, ref damagedEvent);
        }

        foreach (var (sourceUid, dealt) in _ksDealt)
        {
            if (_damageQuery.TryComp(sourceUid, out var sourceComponent))
                KsWear((sourceUid, sourceComponent), dealt);
        }
    }

    /// <summary>
    ///     Has <paramref name="source"/> take its share of the <paramref name="dealt"/> damage it just did, if it wears
    ///         (<see cref="DamageContactsComponent.SelfDamageRatio"/>). Past its own resistances, so that the ratio is
    ///         what it says. On a client this is predicted like the damage it follows; breaking is the server's.
    /// </summary>
    private void KsWear(Entity<DamageContactsComponent> source, FixedPoint2 dealt)
    {
        if (source.Comp.SelfDamageRatio is not { } ratio || ratio <= 0f || dealt <= FixedPoint2.Zero)
            return;

        var wear = new DamageSpecifier(ProtoMan.Index(source.Comp.SelfDamageType), dealt * ratio);
        _damageable.TryChangeDamage(source.Owner, wear, ignoreResistances: true, interruptsDoAfters: false);
    }
}
