using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Popups;
using Content.Server.UserInterface;
using Content.Shared._Starlight.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing filter behavior - pulls from inlet into a single buffer container.
///     The buffer has two outlet nodes with restricted pulling:
///     - Filter outlet: only allows pulling reagents matching the filter list
///     - Passthrough outlet: only allows pulling reagents NOT matching the filter list
///     Restriction is enforced via PlumbingPullAttemptEvent.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingFilterSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingDeviceUpdateEvent>(OnFilterUpdate);
        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingFilterToggleMessage>(OnToggle);
        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingFilterAddReagentMessage>(OnAddReagent);
        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingFilterRemoveReagentMessage>(OnRemoveReagent);
        SubscribeLocalEvent<PlumbingFilterComponent, PlumbingFilterClearMessage>(OnClear);
        SubscribeLocalEvent<PlumbingFilterComponent, BoundUIOpenedEvent>(OnUIOpened);
    }

    private void OnFilterUpdate(Entity<PlumbingFilterComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        // Get our buffer
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionName, out var bufferSolutionEnt, out var bufferSolution))
            return;

        // Check if we have room in the buffer
        if (bufferSolution.AvailableVolume <= 0)
            return;

        // Get inlet node
        if (!_nodeContainer.TryGetNode<PlumbingNode>(ent.Owner, ent.Comp.InletName, out var inletNode))
            return;

        if (inletNode.PlumbingNet == null)
            return;

        // Pull from network 
        var (_, nextIndex) = _pullSystem.PullFromNetwork(ent.Owner, inletNode.PlumbingNet, bufferSolutionEnt.Value, ent.Comp.TransferAmount, ent.Comp.RoundRobinIndex);
        ent.Comp.RoundRobinIndex = nextIndex;
    }

    /// <summary>
    ///     Handles pull attempts - restricts which reagents can be pulled based on outlet node.
    /// </summary>
    private void OnPullAttempt(Entity<PlumbingFilterComponent> ent, ref PlumbingPullAttemptEvent args)
    {
        // Check which outlet is being pulled from
        var isFilteredReagent = ent.Comp.FilteredReagents.Contains(new ProtoId<ReagentPrototype>(args.ReagentPrototype));

        if (args.NodeName == ent.Comp.FilterNodeName)
        {
            // Filter outlet: only allow filtered reagents
            if (!isFilteredReagent)
                args.Cancelled = true;
        }
        else if (args.NodeName == ent.Comp.PassthroughNodeName)
        {
            // Passthrough outlet: only allow non-filtered reagents
            if (isFilteredReagent)
                args.Cancelled = true;
        }
    }

    private void OnToggle(Entity<PlumbingFilterComponent> ent, ref PlumbingFilterToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        DirtyField(ent, ent.Comp, nameof(PlumbingFilterComponent.Enabled));
        UpdateUI(ent);
    }

    private void OnAddReagent(Entity<PlumbingFilterComponent> ent, ref PlumbingFilterAddReagentMessage args)
    {
        var reagentId = FindReagentCaseInsensitive(args.ReagentId);

        // Validate the reagent ID exists
        if (reagentId == null)
        {
            if (args.Actor is { Valid: true })
            {
                _popup.PopupEntity(Loc.GetString("plumbing-filter-invalid-reagent", ("reagent", args.ReagentId)), ent.Owner, args.Actor);
            }
            return;
        }

        ent.Comp.FilteredReagents.Add(new ProtoId<ReagentPrototype>(reagentId));
        DirtyField(ent, ent.Comp, nameof(PlumbingFilterComponent.FilteredReagents));
        UpdateUI(ent);
    }

    private void OnRemoveReagent(Entity<PlumbingFilterComponent> ent, ref PlumbingFilterRemoveReagentMessage args)
    {
        ent.Comp.FilteredReagents.Remove(new ProtoId<ReagentPrototype>(args.ReagentId));
        DirtyField(ent, ent.Comp, nameof(PlumbingFilterComponent.FilteredReagents));
        UpdateUI(ent);
    }

    private void OnClear(Entity<PlumbingFilterComponent> ent, ref PlumbingFilterClearMessage args)
    {
        ent.Comp.FilteredReagents.Clear();
        DirtyField(ent, ent.Comp, nameof(PlumbingFilterComponent.FilteredReagents));
        UpdateUI(ent);
    }

    private void OnUIOpened(Entity<PlumbingFilterComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void UpdateUI(Entity<PlumbingFilterComponent> ent)
    {
        // Convert ProtoId to string for UI state
        var filteredReagents = new HashSet<string>();
        foreach (var protoId in ent.Comp.FilteredReagents)
        {
            filteredReagents.Add(protoId.Id);
        }

        var state = new PlumbingFilterBoundUserInterfaceState(
            filteredReagents,
            ent.Comp.Enabled);

        _ui.SetUiState(ent.Owner, PlumbingFilterUiKey.Key, state);
    }

    /// <summary>
    ///     Finds a reagent prototype ID case-insensitively.
    /// </summary>
    /// <returns>The correctly-cased reagent ID, or null if not found.</returns>
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
