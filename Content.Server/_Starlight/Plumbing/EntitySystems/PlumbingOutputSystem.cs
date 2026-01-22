using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing output machine behavior: A small tank that players can fill from.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingOutputSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingOutputComponent, PlumbingDeviceUpdateEvent>(OnOutputUpdate);
        SubscribeLocalEvent<PlumbingOutputComponent, InteractUsingEvent>(OnOutputInteractUsing);
    }

    private void OnOutputUpdate(Entity<PlumbingOutputComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        if (solution.AvailableVolume <= 0)
            return;

        if (!_nodeContainer.TryGetNode<PlumbingNode>(ent.Owner, ent.Comp.InletName, out var inletNode))
            return;

        if (inletNode.PlumbingNet == null)
            return;

        // Pull from network (with round-robin for fair source selection)
        var (_, nextIndex) = _pullSystem.PullFromNetwork(ent.Owner, inletNode.PlumbingNet, solutionEnt.Value, ent.Comp.RequestAmount, ent.Comp.RoundRobinIndex);
        ent.Comp.RoundRobinIndex = nextIndex;
    }

    private void OnOutputInteractUsing(Entity<PlumbingOutputComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_solutionSystem.TryGetRefillableSolution(args.Used, out var refillableSolutionEnt, out var refillableSolution))
            return;

        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var outputSolutionEnt, out var outputSolution))
            return;

        var transferAmount = outputSolution.Volume;
        if (TryComp<SolutionTransferComponent>(args.Used, out var transferComp))
            transferAmount = FixedPoint2.Min(transferAmount, transferComp.TransferAmount);

        var space = refillableSolution.AvailableVolume;
        var toTransfer = FixedPoint2.Min(transferAmount, space);

        if (toTransfer <= 0)
        {
            _popup.PopupEntity(Loc.GetString("plumbing-output-empty"), ent.Owner, args.User);
            return;
        }

        var split = _solutionSystem.SplitSolution(outputSolutionEnt.Value, toTransfer);
        _solutionSystem.TryAddSolution(refillableSolutionEnt.Value, split);

        _popup.PopupEntity(Loc.GetString("plumbing-output-filled", ("amount", toTransfer)), ent.Owner, args.User);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/drink.ogg"), ent.Owner);

        args.Handled = true;
    }
}
