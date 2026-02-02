using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.UserInterface;
using Content.Shared._Starlight.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Power;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using SharedAppearanceSystem = Robust.Shared.GameObjects.SharedAppearanceSystem;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing reactor machine behavior: reactor pulls reagents into a buffer until they reach
///     target quantities, and then fully reacts the buffer and moves products to output container.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingReactorSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly ChemicalReactionSystem _reactionSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    /// <summary>
    ///     Temperature tolerance for considering target reached (in Kelvin).
    /// </summary>
    private const float TemperatureTolerance = 0.5f;

    /// <summary>
    ///     Minimum allowed target temperature (absolute zero).
    /// </summary>
    private const float MinTemperature = 0f;

    /// <summary>
    ///     Maximum allowed target temperature.
    /// </summary>
    private const float MaxTemperature = 1000f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingDeviceUpdateEvent>(OnReactorUpdate);
        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingReactorToggleMessage>(OnToggle);
        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingReactorSetTargetMessage>(OnSetTarget);
        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingReactorRemoveTargetMessage>(OnRemoveTarget);
        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingReactorClearTargetsMessage>(OnClearTargets);
        SubscribeLocalEvent<PlumbingReactorComponent, PlumbingReactorSetTemperatureMessage>(OnSetTemperature);
        SubscribeLocalEvent<PlumbingReactorComponent, BoundUIOpenedEvent>(OnUIOpened);
    }

    private void OnReactorUpdate(Entity<PlumbingReactorComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionName, out var bufferEnt, out var buffer))
            return;

        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.OutputSolutionName, out var outputEnt, out var output))
            return;

        // Don't process if output buffer is full
        if (output.AvailableVolume <= 0)
        {
            UpdateUI(ent);
            return;
        }

        // If we have no targets, don't pull anything
        if (ent.Comp.ReagentTargets.Count == 0)
        {
            UpdateUI(ent);
            return;
        }

        // Get inlet node network
        if (!_nodeContainer.TryGetNode<PlumbingNode>(ent.Owner, ent.Comp.InletName, out var inletNode))
            return;

        if (inletNode.PlumbingNet == null)
            return;

        // Build target dictionary with amounts still needed, checking if any are needed
        var neededTargets = new Dictionary<string, FixedPoint2>();
        var allMet = true;

        foreach (var (reagentProtoId, targetAmount) in ent.Comp.ReagentTargets)
        {
            var reagentId = reagentProtoId.Id;
            var currentAmount = buffer.GetReagentQuantity(new ReagentId(reagentId, null));
            var needed = targetAmount - currentAmount;
            if (needed > 0)
            {
                allMet = false;
                neededTargets[reagentId] = needed;
                // Start sprite animation when targets set
                _appearance.SetData(ent.Owner, PlumbingVisuals.Running, true); 
            }
        }

        // Pull specific reagents if any are needed
        if (neededTargets.Count > 0)
            _pullSystem.PullSpecificReagents(ent.Owner, inletNode.PlumbingNet, bufferEnt.Value, neededTargets, ent.Comp.TransferAmount);

        // If all targets are met, heat and react
        if (allMet)
        {
            // Start heating only once reagents are ready
            if (_power.IsPowered(ent.Owner))
            {
                ApplyTemperatureControl(ent, bufferEnt.Value, buffer, args.dt);
            }

            // Dont do the reaction until we reach the the target temperature or close enough, but update the ui.
            if (!MathHelper.CloseTo(buffer.Temperature, ent.Comp.TargetTemperature, TemperatureTolerance))
            {
                UpdateUI(ent);
                return;
            }

            // Trigger reactions in the buffer
            _reactionSystem.FullyReactSolution(bufferEnt.Value);

            var products = new List<(ReagentId Reagent, FixedPoint2 Quantity)>();
            foreach (var reagent in buffer.Contents)
            {
                // Skip target reagents still in the input buffer and only move products
                if (ent.Comp.ReagentTargets.ContainsKey(new ProtoId<ReagentPrototype>(reagent.Reagent.Prototype)))
                    continue;

                products.Add((reagent.Reagent, reagent.Quantity));
            }

            // Move products to output and reset temperature only if we produced something
            if (products.Count > 0)
            {
                foreach (var (reagent, quantity) in products)
                {
                    var removed = _solutionSystem.RemoveReagent(bufferEnt.Value, reagent, quantity);
                    if (removed > 0)
                        _solutionSystem.TryAddReagent(outputEnt.Value, reagent, removed, out _);
                }

                // Reset buffer to ambient temperature after products are transferred.
                _solutionSystem.SetTemperature(bufferEnt.Value, Atmospherics.T20C);

                // Stop sprite animation when products are made
                _appearance.SetData(ent.Owner, PlumbingVisuals.Running, false); 
            }
        }

        UpdateUI(ent);
    }

    /// <summary>
    ///     Applies temperature control by adding thermal energy to reach target temperature. Adds negative thermal energy for cooling.
    /// </summary>
    private void ApplyTemperatureControl(
        Entity<PlumbingReactorComponent> ent,
        Entity<SolutionComponent> solutionEnt,
        Solution solution,
        float dt)
    {
        var currentTemp = solution.Temperature;
        var targetTemp = ent.Comp.TargetTemperature;

        // Skip if already at target or close enough
        if (MathHelper.CloseTo(currentTemp, targetTemp, TemperatureTolerance))
            return;

        var heatCap = solution.GetHeatCapacity(_prototypeManager);
        if (heatCap <= 0f)
            return; // Don't heat empty solution

        // Calculate max energy we can transfer this tick in joules, watts * seconds = joules
        var maxEnergyTransfer = ent.Comp.HeatTransferPower * dt;

        // Calculate energy needed to reach target exactly
        var tempDiff = targetTemp - currentTemp;
        var energyNeeded = tempDiff * heatCap;

        // Energy added should be either the exact amounted needed or the max we can do this tick
        var energyTransfer = Math.Clamp(energyNeeded, -maxEnergyTransfer, maxEnergyTransfer);

        _solutionSystem.AddThermalEnergy(solutionEnt, energyTransfer);
    }

    private void OnToggle(Entity<PlumbingReactorComponent> ent, ref PlumbingReactorToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.Enabled));
        UpdateUI(ent);

        // Turn off visual animation when disabled
        if (!args.Enabled)
            _appearance.SetData(ent.Owner, PlumbingVisuals.Running, false);
    }

    private void OnSetTarget(Entity<PlumbingReactorComponent> ent, ref PlumbingReactorSetTargetMessage args)
    {
        var reagentId = FindReagentCaseInsensitive(args.ReagentId);

        if (args.Quantity <= 0)
        {
            if (reagentId != null)
                ent.Comp.ReagentTargets.Remove(new ProtoId<ReagentPrototype>(reagentId));
            DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.ReagentTargets));
            UpdateUI(ent);
            return;
        }

        // Validate that the reagent ID exists
        if (reagentId == null)
        {
            if (args.Actor is { Valid: true })
            {
                _popup.PopupEntity(Loc.GetString("plumbing-reactor-invalid-reagent", ("reagent", args.ReagentId)), ent.Owner, args.Actor);
            }
            return;
        }

        ent.Comp.ReagentTargets[new ProtoId<ReagentPrototype>(reagentId)] = args.Quantity;
        DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.ReagentTargets));
        UpdateUI(ent);
    }

    private void OnRemoveTarget(Entity<PlumbingReactorComponent> ent, ref PlumbingReactorRemoveTargetMessage args)
    {
        ent.Comp.ReagentTargets.Remove(new ProtoId<ReagentPrototype>(args.ReagentId));
        DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.ReagentTargets));
        UpdateUI(ent);
    }

    private void OnClearTargets(Entity<PlumbingReactorComponent> ent, ref PlumbingReactorClearTargetsMessage args)
    {
        ent.Comp.ReagentTargets.Clear();
        DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.ReagentTargets));
        UpdateUI(ent);
    }

    private void OnSetTemperature(Entity<PlumbingReactorComponent> ent, ref PlumbingReactorSetTemperatureMessage args)
    {
        // Clamp to reasonable values 
        ent.Comp.TargetTemperature = Math.Clamp(args.Temperature, MinTemperature, MaxTemperature);
        DirtyField(ent, ent.Comp, nameof(PlumbingReactorComponent.TargetTemperature));
        UpdateUI(ent);
    }

    private void OnUIOpened(Entity<PlumbingReactorComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void UpdateUI(Entity<PlumbingReactorComponent> ent)
    {
        var bufferContents = new Dictionary<string, FixedPoint2>();
        var outputContents = new Dictionary<string, FixedPoint2>();
        var currentTemperature = Atmospherics.T20C;

        if (_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionName, out _, out var buffer))
        {
            foreach (var reagent in buffer.Contents)
            {
                bufferContents[reagent.Reagent.Prototype] = reagent.Quantity;
            }
            currentTemperature = buffer.Temperature;
        }

        if (_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.OutputSolutionName, out _, out var output))
        {
            foreach (var reagent in output.Contents)
            {
                outputContents[reagent.Reagent.Prototype] = reagent.Quantity;
            }
        }

        // Convert ProtoId to string for UI state
        var reagentTargets = new Dictionary<string, FixedPoint2>();
        foreach (var (protoId, quantity) in ent.Comp.ReagentTargets)
        {
            reagentTargets[protoId.Id] = quantity;
        }

        var state = new PlumbingReactorBoundUserInterfaceState(
            reagentTargets,
            bufferContents,
            outputContents,
            ent.Comp.Enabled,
            ent.Comp.TargetTemperature,
            currentTemperature
        );

        _ui.SetUiState(ent.Owner, PlumbingReactorUiKey.Key, state);
    }

    private string? FindReagentCaseInsensitive(string input)
    {
        // Try exact match first
        if (_prototypeManager.HasIndex<ReagentPrototype>(input))
            return input;

        // Search case-insensitively
        foreach (var proto in _prototypeManager.EnumeratePrototypes<ReagentPrototype>())
        {
            if (string.Equals(proto.ID, input, StringComparison.OrdinalIgnoreCase))
                return proto.ID;
        }

        return null;
    }
}
