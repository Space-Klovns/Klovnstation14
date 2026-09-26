// KS14: added in this fork
using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.Physics.Dynamics;

namespace Content.Shared.Trigger.Systems;

public sealed partial class TriggerSystem
{
    /// <summary>
    ///     Runs <see cref="TriggerOnCollideComponent"/>'s trigger as though <paramref name="entity"/>'s
    ///         <paramref name="ourFixtureId"/> fixture had just started colliding with <paramref name="otherFixture"/>
    ///         on <paramref name="otherUid"/>.
    ///
    ///     <see cref="Robust.Shared.Physics.Events.StartCollideEvent"/> can't be constructed outside the engine, so
    ///         anything that resolves a collision physics never reported - a lag-compensation ghost standing in for
    ///         the real target, for instance - calls this instead of re-raising it.
    /// </summary>
    /// <returns>Whether the trigger went off.</returns>
    public bool TryCollideTrigger(Entity<TriggerOnCollideComponent?> entity, EntityUid otherUid, string ourFixtureId, Fixture otherFixture)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        if (ourFixtureId != entity.Comp.FixtureID ||
            entity.Comp.IgnoreOtherNonHard && !otherFixture.Hard ||
            entity.Comp.MaxTriggers is <= 0)
        {
            return false;
        }

        if (entity.Comp.MaxTriggers != null)
        {
            entity.Comp.MaxTriggers--;
            Dirty(entity);
            if (entity.Comp.MaxTriggers <= 0)
                RemCompDeferred<TriggerOnCollideComponent>(entity);
        }

        Trigger(entity.Owner, otherUid, entity.Comp.KeyOut);
        return true;
    }
}
