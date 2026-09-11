using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking; // KS14
using Content.Shared.DeviceLinking.Events; // KS14
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Inventory.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Runs wearable IV drips. Injection and solution changes are predicted on both client and server.
/// </summary>
public sealed partial class SharedIvDripSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedDeviceLinkSystem _deviceLinkSystem = default!; // KS14
    [Dependency] private IvDripUiSystem _ivDripUiSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ReactiveSystem _reactiveSystem = default!;
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IvDripComponent, ComponentStartup>(OnComponentStartup); // KS14: create signal ports
        SubscribeLocalEvent<IvDripComponent, SignalReceivedEvent>(OnSignalReceived); // KS14: control injection by signal
        SubscribeLocalEvent<IvDripComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<ToggleIvDripActionEvent>(OnToggleAction);
        SubscribeLocalEvent<IvDripComponent, GotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotEquipped(Entity<IvDripComponent> entity, ref GotEquippedEvent args)
    {
        entity.Comp.Wearer = args.EquipTarget;
        entity.Comp.NextInjection = _gameTiming.CurTime;
        _actionsSystem.AddAction(args.EquipTarget, ref entity.Comp.ToggleActionEntity, entity.Comp.ToggleAction, entity);
        _actionsSystem.SetToggled(entity.Comp.ToggleActionEntity, entity.Comp.InjectionEnabled);
        Dirty(entity);
    }

    private void OnGotUnequipped(Entity<IvDripComponent> entity, ref GotUnequippedEvent args)
    {
        entity.Comp.Wearer = null;
        _actionsSystem.RemoveAction(args.EquipTarget, entity.Comp.ToggleActionEntity);
        entity.Comp.ToggleActionEntity = null;
        Dirty(entity);
    }

    private void OnToggleAction(ToggleIvDripActionEvent args)
    {
        if (args.Action.Comp.Container is not { } ivDripUid ||
            !TryComp<IvDripComponent>(ivDripUid, out var ivDripComponent) || ivDripComponent.Wearer != args.Performer)
            return;

        SetInjectionEnabled((ivDripUid, ivDripComponent), !ivDripComponent.InjectionEnabled);
        args.Handled = true;
    }

    private void OnComponentStartup(Entity<IvDripComponent> entity, ref ComponentStartup args)
    {
        _deviceLinkSystem.EnsureSinkPorts(entity, entity.Comp.TogglePort, entity.Comp.OnPort, entity.Comp.OffPort);
    }

    private void OnSignalReceived(Entity<IvDripComponent> entity, ref SignalReceivedEvent args)
    {
        if (args.Port == entity.Comp.TogglePort)
            SetInjectionEnabled(entity, !entity.Comp.InjectionEnabled);
        else if (args.Port == entity.Comp.OnPort)
            SetInjectionEnabled(entity, true);
        else if (args.Port == entity.Comp.OffPort)
            SetInjectionEnabled(entity, false);
    }

    private void SetInjectionEnabled(Entity<IvDripComponent> entity, bool enabled)
    {
        if (entity.Comp.InjectionEnabled == enabled)
            return;

        entity.Comp.InjectionEnabled = enabled;
        _actionsSystem.SetToggled(entity.Comp.ToggleActionEntity, enabled);
        _ivDripUiSystem.UpdateUserInterface(entity);
        Dirty(entity);
    }

    public override void Update(float frameTime)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<IvDripComponent>();
        while (query.MoveNext(out var uid, out var ivDripComponent))
        {
            if (!ivDripComponent.InjectionEnabled || ivDripComponent.Wearer is not { } wearer ||
                _gameTiming.CurTime < ivDripComponent.NextInjection)
                continue;

            ivDripComponent.NextInjection = _gameTiming.CurTime + TimeSpan.FromSeconds(ivDripComponent.InjectionInterval);
            Inject((uid, ivDripComponent), wearer);
            Dirty(uid, ivDripComponent);
        }
    }

    private void Inject(Entity<IvDripComponent> entity, EntityUid wearer)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var sourceSolutionEntity, out var sourceSolution) ||
            sourceSolution.Volume <= FixedPoint2.Zero ||
            !_solutionContainerSystem.TryGetInjectableSolution(wearer, out var targetSolutionEntity, out var targetSolution))
            return;

        var transferAmount = FixedPoint2.Min(entity.Comp.InjectionAmount, sourceSolution.Volume);
        transferAmount = FixedPoint2.Min(transferAmount, targetSolution.AvailableVolume);
        if (transferAmount <= FixedPoint2.Zero)
            return;

        var transferredSolution = _solutionContainerSystem.SplitSolution(sourceSolutionEntity.Value, transferAmount);
        _reactiveSystem.DoEntityReaction(wearer, transferredSolution, ReactionMethod.Injection);
        _solutionContainerSystem.TryAddSolution(targetSolutionEntity.Value, transferredSolution);
    }

    private void Spill(Entity<IvDripComponent> entity)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var solutionEntity, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
            return;

        var spillAmount = FixedPoint2.Min(entity.Comp.SpillAmount, solution.Volume);
        if (spillAmount <= FixedPoint2.Zero)
            return;

        var spilledSolution = _solutionContainerSystem.SplitSolution(solutionEntity.Value, spillAmount);
        _puddleSystem.TrySpillAt(Transform(entity).Coordinates, spilledSolution, out _);
    }
}
