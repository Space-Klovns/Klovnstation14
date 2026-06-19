using Content.Server._Starlight.Plumbing.Components;
using Content.Shared._Starlight.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

public sealed class PlumbingStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingStorageComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<PlumbingStorageComponent, PlumbingDeviceUpdateEvent>(OnStorageUpdate);
    }

    private void OnUIOpened(Entity<PlumbingStorageComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void OnStorageUpdate(Entity<PlumbingStorageComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        UpdateUI(ent);
    }

    private void UpdateUI(Entity<PlumbingStorageComponent> ent)
    {
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution))
            return;

        var contents = new Dictionary<string, FixedPoint2>();
        foreach (var reagent in solution.Contents)
        {
            contents[reagent.Reagent.Prototype] = reagent.Quantity;
        }

        var state = new PlumbingStorageBuiState(contents, solution.Volume, solution.MaxVolume);
        _ui.SetUiState(ent.Owner, PlumbingStorageUiKey.Key, state);
        }
        }
