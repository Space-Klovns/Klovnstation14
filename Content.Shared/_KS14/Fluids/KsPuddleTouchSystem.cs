using Content.Shared.Fluids.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.Fluids;

/// <summary>
///     Keeps the puddle's touch fixture (<see cref="TouchFixtureId"/>) to the things it is for.
/// </summary>
/// <remarks>
///     <para>
///         A puddle's <c>slipFixture</c> (layer <c>SlipLayer</c>, mask <c>ItemMask</c>) never touches an item, whose
///             fixture has an <c>ItemMask</c> mask and no layer. So puddles carry a second, non-hard fixture of the same
///             bounds on <c>HighImpassable</c>, which <c>ItemMask</c> includes: whatever lies in a puddle touches it
///             through that, and is counted once per puddle by whoever needs to know (<c>KsHotPuddleSystem</c>).
///     </para>
///     <para>
///         <c>FlyingMobMask</c> is exactly <c>ItemMask</c>, so a fixture an item touches is one a bat, a carp or a
///             dragon touches as well, and none of them touch the <c>slipFixture</c>. Left alone, they would start
///             slipping on puddles (the step trigger takes any hard fixture that touches any of the puddle's) and
///             being slowed by sticky ones. Mobs' hard fixtures are therefore kept off the touch fixture altogether:
///             a walking mob touches the puddle through its <c>slipFixture</c> exactly as before, a flying one still
///             does not, and a mob that is hot is so by being on fire, so it touches the touch fixture through its
///             non-hard <c>flammable</c> fixture, which steps on nothing.
///     </para>
///     <para>
///         Shared, so that a client predicting a step trigger sees the same contacts as the server.
///     </para>
/// </remarks>
public sealed partial class KsPuddleTouchSystem : EntitySystem
{
    /// <summary>
    ///     The id of the puddle fixture that the things lying in it touch. Defined on the <c>Puddle</c> prototype.
    /// </summary>
    public const string TouchFixtureId = "ksTouchFixture";

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    [SubscribeLocalEvent]
    private void OnPuddlePreventCollide(Entity<PuddleComponent> entity, ref PreventCollideEvent args)
    {
        // Cheapest test first: this is raised for every contact either of the puddle's fixtures could make.
        if (!args.OtherFixture.Hard || !_mobStateQuery.HasComp(args.OtherEntity))
            return;

        if (!_fixturesQuery.TryComp(entity.Owner, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(TouchFixtureId, out var touchFixture) ||
            !ReferenceEquals(touchFixture, args.OurFixture))
            return;

        args.Cancelled = true;
    }
}
