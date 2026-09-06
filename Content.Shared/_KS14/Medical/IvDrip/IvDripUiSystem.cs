using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.UserInterface;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Provides the IV drip configuration interface.
/// </summary>
public sealed partial class IvDripUiSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SolutionChangedEvent>(OnSolutionChanged);

        Subs.BuiEvents<IvDripComponent>(IvDripUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBoundUiOpened);
            subs.Event<IvDripSetEnabledMessage>(OnSetEnabled);
            subs.Event<IvDripSetAmountMessage>(OnSetAmount);
            subs.Event<IvDripSetIntervalMessage>(OnSetInterval);
        });
    }

    private void OnSolutionChanged(ref SolutionChangedEvent args)
    {
        if (!TryComp<ContainedSolutionComponent>(args.Solution, out var containedSolutionComponent) ||
            !TryComp<IvDripComponent>(containedSolutionComponent.Container, out var ivDripComponent))
            return;

        UpdateUserInterface((containedSolutionComponent.Container, ivDripComponent));
    }

    private void OnBoundUiOpened(Entity<IvDripComponent> entity, ref BoundUIOpenedEvent args)
    {
        UpdateUserInterface(entity);
    }

    private void OnSetEnabled(Entity<IvDripComponent> entity, ref IvDripSetEnabledMessage args)
    {
        entity.Comp.InjectionEnabled = args.Enabled;
        Dirty(entity);
        UpdateUserInterface(entity);
    }

    private void OnSetAmount(Entity<IvDripComponent> entity, ref IvDripSetAmountMessage args)
    {
        if (!entity.Comp.CanSetInjectionAmount)
            return;

        entity.Comp.InjectionAmount = FixedPoint2.Clamp(args.Amount, entity.Comp.MinimumInjectionAmount, entity.Comp.MaximumInjectionAmount);
        Dirty(entity);
        UpdateUserInterface(entity);
    }

    private void OnSetInterval(Entity<IvDripComponent> entity, ref IvDripSetIntervalMessage args)
    {
        if (!entity.Comp.CanSetInjectionInterval)
            return;

        entity.Comp.InjectionInterval = Math.Clamp(args.Interval, entity.Comp.MinimumInjectionInterval, entity.Comp.MaximumInjectionInterval);
        Dirty(entity);
        UpdateUserInterface(entity);
    }

    private void UpdateUserInterface(Entity<IvDripComponent> entity)
    {
        var solutionVolume = FixedPoint2.Zero;
        var solutionMaxVolume = FixedPoint2.Zero;
        if (_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out _, out var solution))
        {
            solutionVolume = solution.Volume;
            solutionMaxVolume = solution.MaxVolume;
        }

        _userInterfaceSystem.SetUiState(entity.Owner, IvDripUiKey.Key, new IvDripBoundUserInterfaceState(
            entity.Comp.InjectionEnabled,
            entity.Comp.InjectionAmount,
            entity.Comp.InjectionInterval,
            solutionVolume,
            solutionMaxVolume,
            entity.Comp.CanSetInjectionAmount,
            entity.Comp.CanSetInjectionInterval,
            entity.Comp.MinimumInjectionAmount,
            entity.Comp.MaximumInjectionAmount,
            entity.Comp.MinimumInjectionInterval,
            entity.Comp.MaximumInjectionInterval));
    }
}