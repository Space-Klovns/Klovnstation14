using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Shared.Damage.Systems;

public sealed partial class PassiveDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Shared.Network.INetManager _netManager = default!; // KS14

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassiveDamageComponent, MapInitEvent>(OnPendingMapInit);
    }

    private void OnPendingMapInit(EntityUid uid, PassiveDamageComponent component, MapInitEvent args)
    {
        component.NextDamage = _timing.CurTime + TimeSpan.FromSeconds(1f);
    }

    // Every tick, attempt to damage entities
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // KS14 start: the server's alone. NextDamage is not networked, so a client healed each mob on a clock of its own,
        //      the next state put that back, and the server's heal then arrived: health flickered every second.
        if (_netManager.IsClient)
            return;
        // KS14 end

        var curTime = _timing.CurTime;

        // Go through every entity with the component
        var query = EntityQueryEnumerator<PassiveDamageComponent, DamageableComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damage, out var mobState))
        {
            // Make sure they're up for a damage tick
            if (comp.NextDamage > curTime)
                continue;

            // Set the next time they can take damage
            comp.NextDamage = curTime + TimeSpan.FromSeconds(1f);

            // Damage them
            foreach (var allowedState in comp.AllowedStates)
            {
                if (allowedState == mobState.CurrentState)
                    _damageable.ChangeDamage((uid, damage), comp.Damage, true, false);
            }
        }
    }
}
