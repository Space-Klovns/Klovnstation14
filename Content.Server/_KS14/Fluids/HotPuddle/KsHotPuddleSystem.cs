using Content.Shared._KS14.Fluids;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Temperature;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluids.HotPuddle;

/// <summary>
///     Hot things lying in a puddle heat it: a lit welder dropped in welding fuel sets it alight.
/// </summary>
/// <remarks>
///     <para>
///         Whatever touches a puddle gets a <see cref="KsTouchingPuddleComponent"/>, and is asked every
///             <see cref="CheckInterval"/> whether it is hot. If it is, it is a heat source of <see cref="HeatPower"/>
///             at <see cref="SourceTemperature"/>, like a small flame: the power is split between the puddles it
///             touches, and a puddle is never heated past the source's temperature. It adds energy at a bounded rate
///             rather than setting a temperature, so a big puddle takes longer to heat than a small one.
///     </para>
///     <para>
///         The numbers are constants rather than data fields: <see cref="IsHotEvent"/> only says whether something
///             is hot, not how hot, so there is nothing on a source to vary them by, and a field on the puddle would
///             mean restating every puddle prototype for the same values.
///     </para>
///     <para>
///         Only contacts with the puddle's <see cref="KsPuddleTouchSystem.TouchFixtureId"/> fixture count. Items
///             touch puddles through nothing else (see <see cref="KsPuddleTouchSystem"/>), and counting one fixture
///             counts each toucher once per puddle, however many of the puddle's fixtures it touches.
///     </para>
/// </remarks>
public sealed partial class KsHotPuddleSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    [Dependency] private EntityQuery<PuddleComponent> _puddleQuery = default!;
    [Dependency] private EntityQuery<KsTouchingPuddleComponent> _touchingQuery = default!;

    /// <summary>
    ///     How often something touching a puddle is asked whether it is hot.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1d);

    /// <summary>
    ///     The heat, in watts, that a hot thing puts into the puddles it touches, split between them. A puddle's heat
    ///         capacity is about 1 J/K per unit, so this warms 100u of welding fuel from room temperature to its 300 K
    ///         ignition point in a little over a second.
    /// </summary>
    public const float HeatPower = 500f;

    /// <summary>
    ///     The temperature, in kelvin, of a hot thing: about that of a flame. A puddle is never heated past it.
    /// </summary>
    public const float SourceTemperature = 700f;

    private readonly List<(EntityUid PuddleUid, float Energy)> _pendingHeat = [];
    private readonly List<EntityUid> _staleEntries = [];
    private readonly List<EntityUid> _emptiedTouchers = [];
    private readonly List<EntityUid> _releasedTouchers = [];

    #region Contacts

    /// <summary>
    ///     Something that is not fixed in place touching a puddle is touching it until it touches it no more.
    /// </summary>
    /// <remarks>
    ///     Only the touch fixture counts, so something touching both of a puddle's fixtures is in it once. Static
    ///         bodies are left out: puddles touch the walls, tables and machines around them, and none of those lie in
    ///         one. Nothing is heated here, inside the physics step; that waits for <see cref="Update"/>.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnPuddleStartCollide(Entity<PuddleComponent> entity, ref StartCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        if (args.OurFixtureId != KsPuddleTouchSystem.TouchFixtureId ||
            args.OtherBody.BodyType == BodyType.Static ||
            TerminatingOrDeleted(otherUid) ||
            TerminatingOrDeleted(entity.Owner))
            return;

        if (!_touchingQuery.TryComp(otherUid, out var touchingComponent))
        {
            touchingComponent = AddComp<KsTouchingPuddleComponent>(otherUid);
            // Checked as soon as it lands: a lit welder dropped in fuel starts heating it on the next tick.
            touchingComponent.NextCheck = _gameTiming.CurTime;
        }

        touchingComponent.Puddles.Add(entity.Owner);
    }

    /// <summary>
    ///     Leaving a puddle, which for something touching its touch fixture with several fixtures of its own is when
    ///         the last of them stops.
    /// </summary>
    /// <remarks>
    ///     Also raised when the contact is destroyed with the puddle still touching, as it is when either entity is
    ///         deleted or stops colliding (<c>SharedPhysicsSystem.DestroyContact</c>).
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnPuddleEndCollide(Entity<PuddleComponent> entity, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        if (args.OurFixtureId != KsPuddleTouchSystem.TouchFixtureId ||
            !_touchingQuery.TryComp(otherUid, out var touchingComponent) ||
            !touchingComponent.Puddles.Contains(entity.Owner) ||
            IsTouching(otherUid, entity.Owner))
            return;

        LeavePuddle((otherUid, touchingComponent), entity.Owner);
    }

    /// <summary>
    ///     A puddle on its way out (evaporated, mopped up, or merged into the one already on its tile) is left by
    ///         everything touching it, before its contacts go.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPuddleTerminating(Entity<PuddleComponent> entity, ref EntityTerminatingEvent args)
    {
        // Collected first: removing a component while walking the contacts is not something to rely on.
        _releasedTouchers.Clear();
        var contacts = _physicsSystem.GetContacts(entity.Owner, includeDeleting: true);
        while (contacts.MoveNext(out var contact))
            _releasedTouchers.Add(contact.EntityA == entity.Owner ? contact.EntityB : contact.EntityA);

        foreach (var toucherUid in _releasedTouchers)
        {
            if (_touchingQuery.TryComp(toucherUid, out var touchingComponent))
                LeavePuddle((toucherUid, touchingComponent), entity.Owner);
        }

        _releasedTouchers.Clear();
    }

    /// <summary>
    ///     Whether any of the entity's contacts, other than ones being destroyed, is touching the puddle's touch
    ///         fixture.
    /// </summary>
    private bool IsTouching(EntityUid uid, EntityUid puddleUid)
    {
        var contacts = _physicsSystem.GetContacts(uid);
        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching)
                continue;

            if (contact.EntityA == puddleUid && contact.FixtureAId == KsPuddleTouchSystem.TouchFixtureId ||
                contact.EntityB == puddleUid && contact.FixtureBId == KsPuddleTouchSystem.TouchFixtureId)
                return true;
        }

        return false;
    }

    private void LeavePuddle(Entity<KsTouchingPuddleComponent> entity, EntityUid puddleUid)
    {
        if (!entity.Comp.Puddles.Remove(puddleUid) || entity.Comp.Puddles.Count > 0)
            return;

        RemComp(entity.Owner, entity.Comp);
    }

    #endregion

    #region Heating

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        _pendingHeat.Clear();
        _emptiedTouchers.Clear();

        var query = EntityQueryEnumerator<KsTouchingPuddleComponent>();
        while (query.MoveNext(out var uid, out var touchingComponent))
        {
            if (curTime < touchingComponent.NextCheck)
                continue;

            touchingComponent.NextCheck = curTime + CheckInterval;

            // Checked rather than trusted: a puddle that went without its touchers hearing of it is dropped here.
            _staleEntries.Clear();
            foreach (var puddleUid in touchingComponent.Puddles)
            {
                if (!_puddleQuery.HasComp(puddleUid) || TerminatingOrDeleted(puddleUid))
                    _staleEntries.Add(puddleUid);
            }

            foreach (var puddleUid in _staleEntries)
                touchingComponent.Puddles.Remove(puddleUid);

            _staleEntries.Clear();

            if (touchingComponent.Puddles.Count == 0)
            {
                _emptiedTouchers.Add(uid);
                continue;
            }

            var isHotEvent = new IsHotEvent();
            RaiseLocalEvent(uid, ref isHotEvent);
            if (!isHotEvent.IsHot)
                continue;

            var energy = HeatPower * (float)CheckInterval.TotalSeconds / touchingComponent.Puddles.Count;
            foreach (var puddleUid in touchingComponent.Puddles)
                _pendingHeat.Add((puddleUid, energy));
        }

        foreach (var toucherUid in _emptiedTouchers)
            RemComp<KsTouchingPuddleComponent>(toucherUid);

        _emptiedTouchers.Clear();

        // Applied once the components are no longer being walked: heating a solution updates its chemicals, which can
        //     run reactions and tile effects that spawn or delete puddles, and so add or remove touching components.
        foreach (var (puddleUid, energy) in _pendingHeat)
            HeatPuddle(puddleUid, energy);

        _pendingHeat.Clear();
    }

    /// <summary>
    ///     Puts up to <paramref name="energy"/> joules into a puddle's solution, but never past
    ///         <see cref="SourceTemperature"/>.
    /// </summary>
    /// <returns>The energy actually put in.</returns>
    private float HeatPuddle(EntityUid puddleUid, float energy)
    {
        if (TerminatingOrDeleted(puddleUid) ||
            !_puddleQuery.TryComp(puddleUid, out var puddleComponent) ||
            !_solutionContainerSystem.ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
            return 0f;

        var heatCapacity = solution.GetHeatCapacity(ProtoMan);
        if (heatCapacity <= 0f)
            return 0f;

        var added = MathF.Min(energy, (SourceTemperature - solution.Temperature) * heatCapacity);
        if (added <= 0f)
            return 0f;

        _solutionContainerSystem.AddThermalEnergy(puddleComponent.Solution.Value, added);
        return added;
    }

    #endregion
}
