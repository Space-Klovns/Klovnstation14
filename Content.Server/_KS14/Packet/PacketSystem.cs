using Content.Server._KS14.Packet.Components;
using Content.Server.GameTicking.Events;
using Content.Shared._KS14.Packets.BUI;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Handles events and network.
/// </summary>
public sealed partial class PacketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        InitializeModules();

        SubscribeLocalEvent<ExecutorComponent, BoundUIOpenedEvent>(SubscribeUpdateUiState);
        SubscribeLocalEvent<ExecutorComponent, SaveExecutorCommandMessage>(OnSave);
        SubscribeLocalEvent<ExecutorComponent, StartExecutionMessage>(OnExecute);

        SubscribeLocalEvent<ExecuteOnInteractComponent, UseInHandEvent>(OnUse);

        SubscribeLocalEvent<PacketReceiverComponent, ComponentInit>(OnPacketInit);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    public override void Update(float frameTime)
    {
        foreach (var (uid, _) in _executorEntities)
        {
            if (uid.Comp.Log == string.Empty)
                continue;

            _userInterfaceSystem.ServerSendUiMessage(uid.Owner, ExecutorUiKey.Key, new LogExecutorMessage(uid.Comp.Log));
            uid.Comp.Log = string.Empty;
        }
    }

    private void SubscribeUpdateUiState<T>(Entity<ExecutorComponent> ent, ref T ev)
    {
        UpdateUiState(ent);
    }

    private void OnSave(Entity<ExecutorComponent> ent, ref SaveExecutorCommandMessage args)
    {
        ent.Comp.Command = args.Command;
    }

    private void OnExecute(Entity<ExecutorComponent> ent, ref StartExecutionMessage args)
    {
        ExecuteCommand(ent.Comp.Command, ent);
    }

    private void UpdateUiState(Entity<ExecutorComponent> ent)
    {
        var maxStatements = ent.Comp.MaximumExecutionStatements;
        var maxMemory = ent.Comp.MemoryAllocation;
        var command = ent.Comp.Command;

        _userInterfaceSystem.SetUiState(ent.Owner, ExecutorUiKey.Key, new ExecutorBoundUserInterfaceState(maxStatements, maxMemory, command));
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        RandomizeFrequencies();
    }

    private void OnPacketInit(Entity<PacketReceiverComponent> ent, ref ComponentInit ev)
    {
        SetupAddress(ent);
    }

    private void OnUse(Entity<ExecuteOnInteractComponent> ent, ref UseInHandEvent args)
    {
        if (!TryComp<PaperComponent>(ent, out var paper) ||
            !TryComp<ExecutorComponent>(ent, out var executor))
            return;

        ExecuteCommand(paper.Content, (ent.Owner, executor));
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        Dispose();
    }

    private void Dispose()
    {
        _packetEntities.Clear();
        _executorEntities.Clear();
    }
}
