using Content.Shared.Actions;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Runs wearable IV drips: the hotbar action, the device-link ports, and the injection tick itself.
/// </summary>
/// <remarks>
///     Injection is deliberately server-authoritative. Predicting it would mean predicting
///         <see cref="ReactiveSystem.DoEntityReaction"/>, whose reagent effects (popups, sounds, damage)
///         are not rollback-safe, and the resulting solution state is replicated anyway.
///     The window lives in <c>IvDripSystem.Ui.cs</c>; spilling on damage lives in the server-only
///         <c>IvDripSpillageSystem</c>, because puddles can only be made server-side.
/// </remarks>
public sealed partial class IvDripSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedDeviceLinkSystem _deviceLinkSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ReactiveSystem _reactiveSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    /// <summary>
    ///     Creates the device-link sink ports the drip listens on.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnComponentStartup(Entity<IvDripComponent> entity, ref ComponentStartup args)
    {
        _deviceLinkSystem.EnsureSinkPorts(entity, entity.Comp.TogglePort, entity.Comp.OnPort, entity.Comp.OffPort);
    }

    /// <summary>
    ///     Binds the drip to its new wearer and hands them the toggle action.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGotEquipped(Entity<IvDripComponent> entity, ref GotEquippedEvent args)
    {
        entity.Comp.WearerUid = args.EquipTarget;
        entity.Comp.NextInjection = _gameTiming.CurTime + TimeSpan.FromSeconds(entity.Comp.InjectionInterval);

        EnsureComp<IvDripWearerComponent>(args.EquipTarget).DripUids.Add(entity);

        _actionsSystem.AddAction(args.EquipTarget, ref entity.Comp.ToggleActionEntity, entity.Comp.ToggleAction, container: entity);
        _actionsSystem.SetToggled(entity.Comp.ToggleActionEntity, entity.Comp.InjectionEnabled);
        Dirty(entity);
    }

    /// <summary>
    ///     Unbinds the drip from its wearer and takes the toggle action back.
    /// </summary>
    /// <remarks>
    ///     <see cref="IvDripComponent.InjectionEnabled"/> is deliberately left alone, so that taking a
    ///         running drip off and putting it back on resumes it rather than silently stopping it. An
    ///         unworn drip never injects regardless, because <see cref="Update"/> skips a null wearer.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnGotUnequipped(Entity<IvDripComponent> entity, ref GotUnequippedEvent args)
    {
        entity.Comp.WearerUid = null;

        if (TryComp<IvDripWearerComponent>(args.EquipTarget, out var wearerComponent))
        {
            wearerComponent.DripUids.Remove(entity);
            if (wearerComponent.DripUids.Count == 0)
                RemComp(args.EquipTarget, wearerComponent);
        }

        // SharedActionsSystem reclaims actions provided by unequipped gear itself, and by the time this
        // runs it usually has. Removing an action that is no longer attached is an error, so only do it
        // when the wearer really does still hold it.
        if (_actionsSystem.GetAction(entity.Comp.ToggleActionEntity) is { } toggleActionEntity &&
            toggleActionEntity.Comp.AttachedEntity == args.EquipTarget)
        {
            _actionsSystem.RemoveAction(args.EquipTarget, (toggleActionEntity.Owner, toggleActionEntity.Comp));
        }

        entity.Comp.ToggleActionEntity = null;
        Dirty(entity);
    }

    /// <summary>
    ///     Toggles injection when the wearer presses the drip's action.
    /// </summary>
    /// <remarks>
    ///     Directed at the drip rather than the performer: <see cref="SharedActionsSystem"/> raises an
    ///         action event on the action's container when the action is not flagged to raise on its user.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnToggleAction(EntityUid uid, IvDripComponent ivDripComponent, ToggleIvDripActionEvent args)
    {
        if (args.Handled || ivDripComponent.WearerUid != args.Performer)
            return;

        SetInjectionEnabled((uid, ivDripComponent), !ivDripComponent.InjectionEnabled);
        args.Handled = true;
    }

    /// <summary>
    ///     Starts, stops or flips injection in response to a device-link signal.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSignalReceived(Entity<IvDripComponent> entity, ref SignalReceivedEvent args)
    {
        if (args.Port == entity.Comp.TogglePort)
            SetInjectionEnabled(entity, !entity.Comp.InjectionEnabled);
        else if (args.Port == entity.Comp.OnPort)
            SetInjectionEnabled(entity, true);
        else if (args.Port == entity.Comp.OffPort)
            SetInjectionEnabled(entity, false);
    }

    /// <summary>
    ///     The one place <see cref="IvDripComponent.InjectionEnabled"/> is written.
    /// </summary>
    /// <remarks>
    ///     Everything that can start or stop a drip - the action, the window, a device-link signal - goes
    ///         through here, so that the action's toggled state and the open window cannot disagree with
    ///         whether the pump is actually running.
    /// </remarks>
    public void SetInjectionEnabled(Entity<IvDripComponent> entity, bool enabled)
    {
        if (entity.Comp.InjectionEnabled == enabled)
            return;

        entity.Comp.InjectionEnabled = enabled;

        // Restart the clock, so that flicking a drip on does not immediately inject off the back of
        // however long it spent switched off.
        if (enabled)
            entity.Comp.NextInjection = _gameTiming.CurTime + TimeSpan.FromSeconds(entity.Comp.InjectionInterval);

        _actionsSystem.SetToggled(entity.Comp.ToggleActionEntity, enabled);
        UpdateUserInterface(entity);
        Dirty(entity);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        // Server-authoritative, per the class remarks.
        if (!_netManager.IsServer)
            return;

        var query = EntityQueryEnumerator<IvDripComponent>();
        while (query.MoveNext(out var uid, out var ivDripComponent))
        {
            if (!ivDripComponent.InjectionEnabled || ivDripComponent.WearerUid is not { } wearerUid ||
                _gameTiming.CurTime < ivDripComponent.NextInjection)
                continue;

            ivDripComponent.NextInjection = _gameTiming.CurTime + TimeSpan.FromSeconds(ivDripComponent.InjectionInterval);
            Inject((uid, ivDripComponent), wearerUid);
        }
    }

    /// <summary>
    ///     Moves one dose out of the drip and into the wearer's injectable solution.
    /// </summary>
    private void Inject(Entity<IvDripComponent> entity, EntityUid wearerUid)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var sourceSolutionEntity, out var sourceSolution) ||
            sourceSolution.Volume <= FixedPoint2.Zero ||
            !_solutionContainerSystem.TryGetInjectableSolution(wearerUid, out var targetSolutionEntity, out var targetSolution))
            return;

        var transferAmount = FixedPoint2.Min(entity.Comp.InjectionAmount, sourceSolution.Volume);
        transferAmount = FixedPoint2.Min(transferAmount, targetSolution.AvailableVolume);
        if (transferAmount <= FixedPoint2.Zero)
            return;

        var transferredSolution = _solutionContainerSystem.SplitSolution(sourceSolutionEntity.Value, transferAmount);

        if (!_solutionContainerSystem.TryAddSolution(targetSolutionEntity.Value, transferredSolution))
        {
            // Put it back rather than destroying the reagents.
            _solutionContainerSystem.TryAddSolution(sourceSolutionEntity.Value, transferredSolution);
            return;
        }

        _reactiveSystem.DoEntityReaction(wearerUid, transferredSolution, ReactionMethod.Injection);
    }
}
