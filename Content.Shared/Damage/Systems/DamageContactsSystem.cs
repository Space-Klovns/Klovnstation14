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
            damaged.NextSecond = _timing.CurTime + TimeSpan.FromSeconds(1);
            Dirty(ent, damaged); // KS14: NextSecond is networked, so the client ticks with the server

            // KS14 start: tell whatever cares that the damage landed (KsDamagedSoundSystem's rattle)
            /*
            if (damaged.Damage != null)
                _damageable.TryChangeDamage(ent, damaged.Damage, interruptsDoAfters: false);
            */
            if (damaged.Damage != null && _damageable.TryChangeDamage(ent, damaged.Damage, interruptsDoAfters: false))
            {
                var damagedEvent = new Content.Shared._KS14.Damage.Events.KsDamagedByContactEvent();
                RaiseLocalEvent(ent, ref damagedEvent);
            }
            // KS14 end
        }
    }

    private void OnEntityExit(EntityUid uid, DamageContactsComponent component, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;

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
    }

    private void OnEntityEnter(EntityUid uid, DamageContactsComponent component, ref StartCollideEvent args)
    {
        var otherUid = args.OtherEntity;

        if (HasComp<DamagedByContactComponent>(otherUid))
            return;

        if (_whitelistSystem.IsWhitelistPass(component.IgnoreWhitelist, otherUid))
            return;

        var damagedByContact = EnsureComp<DamagedByContactComponent>(otherUid);
        damagedByContact.Damage = component.Damage;
    }
}
