using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.UserInterface;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     The IV drip configuration window: pushing state to it, and acting on what it sends back.
/// </summary>
public sealed partial class SharedIvDripSystem
{
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Lambda subscriptions, so these cannot be expressed as [SubscribeLocalEvent] attributes.
        Subs.BuiEvents<IvDripComponent>(IvDripUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBoundUiOpened);
            subs.Event<IvDripSetEnabledMessage>(OnSetEnabled);
            subs.Event<IvDripSetAmountMessage>(OnSetAmount);
            subs.Event<IvDripSetIntervalMessage>(OnSetInterval);
        });
    }

    /// <summary>
    ///     Keeps the volume readout honest as the drip fills and empties.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSolutionChanged(Entity<IvDripComponent> entity, ref SolutionChangedEvent args)
    {
        UpdateUserInterface(entity);
    }

    private void OnBoundUiOpened(Entity<IvDripComponent> entity, ref BoundUIOpenedEvent args)
    {
        UpdateUserInterface(entity);
    }

    private void OnSetEnabled(Entity<IvDripComponent> entity, ref IvDripSetEnabledMessage args)
    {
        SetInjectionEnabled(entity, args.Enabled);
    }

    private void OnSetAmount(Entity<IvDripComponent> entity, ref IvDripSetAmountMessage args)
    {
        SetInjectionAmount(entity, args.Amount);
    }

    private void OnSetInterval(Entity<IvDripComponent> entity, ref IvDripSetIntervalMessage args)
    {
        SetInjectionInterval(entity, args.Interval);
    }

    /// <summary>
    ///     The one place <see cref="IvDripComponent.InjectionAmount"/> is written.
    /// </summary>
    /// <remarks>
    ///     Refuses the change outright on a drip that cannot be reconfigured, and otherwise clamps to the
    ///         drip's own bounds - the window is free to ask for anything.
    /// </remarks>
    public void SetInjectionAmount(Entity<IvDripComponent> entity, FixedPoint2 amount)
    {
        if (!entity.Comp.CanSetInjectionAmount)
            return;

        entity.Comp.InjectionAmount = FixedPoint2.Clamp(amount, entity.Comp.MinimumInjectionAmount, entity.Comp.MaximumInjectionAmount);
        Dirty(entity);
        UpdateUserInterface(entity);
    }

    /// <summary>
    ///     The one place <see cref="IvDripComponent.InjectionInterval"/> is written.
    /// </summary>
    /// <remarks>
    ///     Rounded to the two decimal places the window shows, so the value sent up matches the one that
    ///         comes back, then clamped to the drip's own bounds.
    /// </remarks>
    public void SetInjectionInterval(Entity<IvDripComponent> entity, float interval)
    {
        if (!entity.Comp.CanSetInjectionInterval)
            return;

        entity.Comp.InjectionInterval = Math.Clamp((float)Math.Round(interval, 2, MidpointRounding.AwayFromZero),
            entity.Comp.MinimumInjectionInterval,
            entity.Comp.MaximumInjectionInterval);
        Dirty(entity);
        UpdateUserInterface(entity);
    }

    /// <summary>
    ///     Pushes the drip's current settings and contents to whoever has its window open.
    /// </summary>
    public void UpdateUserInterface(Entity<IvDripComponent> entity)
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
