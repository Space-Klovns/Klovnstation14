using Content.Server._Starlight.Plumbing;
using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.EntitySystems;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.Audio;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._KS14.Chain;
using Content.Shared._KS14.Plumbing.IvPump;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.Plumbing.IvPump;

public sealed class KsPlumbingIvPumpSystem : SharedKsPlumbingIvPumpSystem
{
    [Dependency] private readonly ChainSystem _chainSystem = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private readonly AmbientSoundSystem _ambientSoundSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsPlumbingIvPumpChainStartComponent, ChainInitiallyBrokenEvent>(OnChainBroken);

        SubscribeLocalEvent<KsPlumbingIvPumpComponent, PlumbingDeviceUpdateEvent>(OnDeviceUpdate);
        SubscribeLocalEvent<KsPlumbingIvPumpComponent, PlumbingPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<KsPlumbingIvPumpComponent, DragDropDraggedEvent>(OnDragDropDragged);
    }

    private void OnChainBroken(Entity<KsPlumbingIvPumpChainStartComponent> entity, ref ChainInitiallyBrokenEvent args)
    {
        // We dont need ts anymore GEG
        foreach (var linkUid in _chainSystem.GetLinksWithoutEdges((entity.Owner, args.EdgeComponent)))
        {
            if (Terminating(linkUid))
                continue;

            QueueDel(linkUid);
        }

        if (TryComp<KsPlumbingIvPumpComponent>(entity.Comp.PumpUid, out var pumpComponent))
        {
            pumpComponent.ChainStartUid = null;
            pumpComponent.PatientUid = null;
            Dirty(entity.Comp.PumpUid, pumpComponent);

            UpdateState(entity.Comp.PumpUid);
        }

        if (entity.Comp.LifeStage < ComponentLifeStage.Stopping)
            RemComp(entity, entity.Comp);

        if (args.EdgeComponent.LifeStage < ComponentLifeStage.Stopping)
            RemComp(entity, args.EdgeComponent);
    }

    private void OnDeviceUpdate(Entity<KsPlumbingIvPumpComponent> entity, ref PlumbingDeviceUpdateEvent args)
    {
        var patientUid = entity.Comp.PatientUid;
        if (patientUid is not { })
            goto endearly;

        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BufferSolutionName, ref entity.Comp.BufferSolutionEntity, out var bufferSolution))
            goto endearly;

        if (!BloodstreamQuery.TryGetComponent(patientUid, out var bloodstreamComponent) ||
            bloodstreamComponent.BloodSolution is not { } bloodSolutionEntity)
            goto endearly;

        _ambientSoundSystem.SetAmbience(entity.Owner, true);

        var bloodSolution = bloodSolutionEntity.Comp.Solution;
        switch (entity.Comp.Mode)
        {
            case KsPlumbingIvPumpMode.Drawing:
                // Return if no blood left
                if (bloodSolution.Volume <= FixedPoint2.Zero)
                    return;

                _solutionContainerSystem.TryTransferSolution(entity.Comp.BufferSolutionEntity.Value, bloodSolution, UpdateTransfer(entity, args.dt));
                break;
            case KsPlumbingIvPumpMode.Injecting:
                // Return if no space
                if (bloodSolution.AvailableVolume <= FixedPoint2.Zero)
                    return;

                // Try to transfer from buffer too before pulling
                var maxTransferred = UpdateTransfer(entity, args.dt);
                var oldVolume = bloodSolution.Volume;
                _solutionContainerSystem.TryTransferSolution(bloodSolutionEntity, bufferSolution, maxTransferred);

                maxTransferred -= bloodSolution.Volume - oldVolume;
                // Transferred too much already, so return
                if (maxTransferred <= FixedPoint2.Zero)
                    return;

                if (!_nodeContainerSystem.TryGetNode<PlumbingNode>(entity.Owner, entity.Comp.InletNodeName, out var node) ||
                    node.PlumbingNet is not { } plumbingNet)
                    return;

                _pullSystem.PullFromNetwork(entity.Owner, plumbingNet, bloodSolutionEntity, maxTransferred, 0);
                break;
            default:
                RemComp(entity, entity.Comp);
                throw new InvalidOperationException($"Tried to update IV pump with invalid mode: {entity.Comp.Mode}");
        }

        return;
    endearly:
        _ambientSoundSystem.SetAmbience(entity.Owner, false);
    }

    /// <summary>
    ///     Tries to keep a consistent-ish transfer rate regardless of
    ///         FixedPoint2 rounding. Mutates the entity.
    /// </summary>
    /// <returns>Amount of reagent transferred.</returns>
    private static FixedPoint2 UpdateTransfer(Entity<KsPlumbingIvPumpComponent> entity, float deltaTime)
    {
        var toAdd = (float)entity.Comp.TransferRate * deltaTime + entity.Comp.RemainingToTransfer;
        var toAddFp2 = FixedPoint2.New(toAdd);

        var remainder = (float)(toAdd - toAddFp2);
        if (remainder > 0)
            entity.Comp.RemainingToTransfer = remainder;

        return toAddFp2;
    }

    private void OnDragDropDragged(Entity<KsPlumbingIvPumpComponent> entity, ref DragDropDraggedEvent args)
    {
        if (args.Handled ||
            entity.Comp.ChainStartUid is { })
            return;

        var startUid = entity.Owner;
        var targetUid = args.Target;

        var ourCoordinates = Transform(entity.Owner).Coordinates;
        _chainSystem.SpawnChainInbetween("KsWireSegmentIv", ourCoordinates, entity.Comp.ChainInbetweenCount, 0.125f, startUid, targetUid);

        var startComponent = AddComp<KsPlumbingIvPumpChainStartComponent>(startUid);
        startComponent.PumpUid = entity;

        entity.Comp.ChainStartUid = startUid;
        entity.Comp.PatientUid = targetUid;
        Dirty(entity);
        UpdateState(entity!);
    }

    private void OnPullAttempt(Entity<KsPlumbingIvPumpComponent> entity, ref PlumbingPullAttemptEvent args)
    {
        if (args.Cancelled ||
            entity.Comp.Mode != KsPlumbingIvPumpMode.Drawing)
            return;

        args.Cancelled = true;
    }
}
