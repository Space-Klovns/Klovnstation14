using Content.Shared.Damage.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageContactsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    [Dependency] private EntityQuery<DamageContactsComponent> _damageQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageContactsComponent, StartCollideEvent>(OnEntityEnter);
        SubscribeLocalEvent<DamageContactsComponent, EndCollideEvent>(OnEntityExit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DamagedByContactComponent>();

        while (query.MoveNext(out var ent, out var damaged))
        {
            if (_timing.CurTime < damaged.NextSecond)
                continue;
            // KS14 start: a source may only hurt the moving; the tick stays due until one of them can hurt
            if (!KsCanTakeContactDamage((ent, damaged)))
                continue;
            // KS14 end
            damaged.NextSecond = _timing.CurTime + TimeSpan.FromSeconds(1);
            Dirty(ent, damaged); // KS14: NextSecond is networked, so the client ticks with the server

            // KS14 start: each source that counts deals its own damage, see DamageContactsSystem.Klovn.cs
            /*
            if (damaged.Damage != null)
                _damageable.TryChangeDamage(ent, damaged.Damage, interruptsDoAfters: false);
            */
            KsDealContactDamage((ent, damaged));
            // KS14 end
        }
    }

    private void OnEntityExit(EntityUid uid, DamageContactsComponent component, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;

        // KS14 start: which sources count is worked out again from what is still touched, rather than staying as the
        //      first source left it for as long as any source is touched
        KsRefreshSources(otherUid, endedUid: uid);
        /*
        if (!TryComp<PhysicsComponent>(otherUid, out var body))
            return;

        foreach (var ent in _physics.GetContactingEntities(otherUid, body))
        {
            if (ent == uid)
                continue;

            if (_damageQuery.HasComponent(ent))
                return;
        }

        RemComp<DamagedByContactComponent>(otherUid);
        */
        // KS14 end
    }

    private void OnEntityEnter(EntityUid uid, DamageContactsComponent component, ref StartCollideEvent args)
    {
        var otherUid = args.OtherEntity;

        // KS14 start: a source of another exclusive group counts beside the first; see KsRefreshSources
        KsRefreshSources(otherUid, startedUid: uid);
        /*
        if (HasComp<DamagedByContactComponent>(otherUid))
            return;

        if (_whitelistSystem.IsWhitelistPass(component.IgnoreWhitelist, otherUid))
            return;

        var damagedByContact = EnsureComp<DamagedByContactComponent>(otherUid);
        damagedByContact.Damage = component.Damage;
        */
        // KS14 end
    }
}
