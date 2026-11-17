using Content.Server._KS14.Fluids;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Internals;

/// <summary>
///     Puts an NPC's internals on and takes them off by what it is breathing. See <see cref="NpcInternalsUserComponent"/>.
/// </summary>
/// <remarks>
///     <para>Nothing polls. Every breath asks it first (<see cref="InhaleLocationEvent"/>, ahead of <see cref="InternalsSystem"/>),
///         so a breath of bad air puts them on in time for that same breath to come from the tank. Smoke carrying something
///         asks it before any gets in (<see cref="KsSmokeExposureEvent"/>), so a mob standing in tear gas gets none of it.</para>
///     <para>Air is judged by <see cref="AtmosphereSystem.IsMixtureProbablySafe"/>: its pressure and temperature, not what
///         is in it.</para>
///     <para>A mob a player is in is left alone.</para>
/// </remarks>
public sealed partial class NpcInternalsSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private GasTankSystem _gasTankSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private InternalsSystem _internalsSystem = default!;

    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;
    [Dependency] private EntityQuery<GasTankComponent> _gasTankQuery = default!;
    [Dependency] private EntityQuery<InternalsComponent> _internalsQuery = default!;
    [Dependency] private EntityQuery<NpcInternalsUserComponent> _internalsUserQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcInternalsUserComponent, InhaleLocationEvent>(OnInhaleLocation, before: [typeof(InternalsSystem)]);
        SubscribeLocalEvent<NpcInternalsUserComponent, KsSmokeExposureEvent>(OnSmokeExposure);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> would go on internals when told to (<see cref="KeepOn"/>): it puts them on by itself,
    ///         nobody is playing it, it has a breath tool on, and it has a tank to hand or already in use.
    /// </summary>
    public bool CanGoOnInternals(EntityUid uid)
    {
        return _internalsUserQuery.HasComp(uid) &&
            !_actorQuery.HasComp(uid) &&
            _internalsQuery.TryComp(uid, out var internalsComponent) &&
            internalsComponent.BreathTools.Count > 0 &&
            (_internalsSystem.AreInternalsWorking(internalsComponent) || _internalsSystem.FindBestGasTank(uid) != null);
    }

    /// <summary>
    ///     Puts <paramref name="entity"/>'s internals on, if they are not already, and keeps them on until at least
    ///         <paramref name="until"/>, whatever the air. For going somewhere about to be gassed.
    /// </summary>
    /// <returns>Whether its internals are working now.</returns>
    public bool KeepOn(Entity<NpcInternalsUserComponent?> entity, TimeSpan until)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        if (until > entity.Comp.OnUntil)
            entity.Comp.OnUntil = until;

        return TryPutOn((entity.Owner, entity.Comp));
    }

    private void OnInhaleLocation(Entity<NpcInternalsUserComponent> entity, ref InhaleLocationEvent args)
    {
        if (_actorQuery.HasComp(entity))
            return;

        // Somewhere with air of its own (a locker, a pod) may have answered already.
        var air = args.Gas ?? _atmosphereSystem.GetContainingMixture(entity.Owner);

        if (!_atmosphereSystem.IsMixtureProbablySafe(air))
        {
            HoldOn(entity);
            return;
        }

        if (entity.Comp.SwitchedOn && _gameTiming.CurTime >= entity.Comp.OnUntil)
            TakeOff(entity);
    }

    private void OnSmokeExposure(Entity<NpcInternalsUserComponent> entity, ref KsSmokeExposureEvent args)
    {
        if (!_actorQuery.HasComp(entity))
            HoldOn(entity);
    }

    private void HoldOn(Entity<NpcInternalsUserComponent> entity)
    {
        var until = _gameTiming.CurTime + entity.Comp.HoldTime;

        if (until > entity.Comp.OnUntil)
            entity.Comp.OnUntil = until;

        TryPutOn(entity);
    }

    private bool TryPutOn(Entity<NpcInternalsUserComponent> entity)
    {
        if (!_internalsQuery.TryComp(entity, out var internalsComponent))
            return false;

        if (_internalsSystem.AreInternalsWorking(internalsComponent))
            return true;

        if (_actorQuery.HasComp(entity) ||
            internalsComponent.BreathTools.Count == 0 ||
            _internalsSystem.FindBestGasTank(entity.Owner) is not { } tank ||
            !_gasTankSystem.ConnectToInternals(tank, user: entity.Owner))
            return false;

        entity.Comp.SwitchedOn = true;
        return _internalsSystem.AreInternalsWorking(internalsComponent);
    }

    private void TakeOff(Entity<NpcInternalsUserComponent> entity)
    {
        // Taken off by someone else already: nothing of its own left on.
        if (!_internalsQuery.TryComp(entity, out var internalsComponent) ||
            !_gasTankQuery.TryComp(internalsComponent.GasTankEntity, out var gasTankComponent))
        {
            entity.Comp.SwitchedOn = false;
            return;
        }

        // The tank's use delay can refuse it; the next breath tries again.
        if (_gasTankSystem.DisconnectFromInternals((internalsComponent.GasTankEntity.Value, gasTankComponent), user: entity.Owner))
            entity.Comp.SwitchedOn = false;
    }
}
