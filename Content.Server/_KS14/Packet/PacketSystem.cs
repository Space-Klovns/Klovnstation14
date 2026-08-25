using Content.Server._KS14.Packet.Components;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Popups;
using Content.Shared._KS14.Packets.BUI;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Jint;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Handles events and network.
/// </summary>
public sealed partial class PacketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;

    internal int _mainThreadId;

    public override void Initialize()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;

        InitializeModules();

        SubscribeLocalEvent<ExecutorComponent, BoundUIOpenedEvent>(SubscribeUpdateUiState);
        SubscribeLocalEvent<ExecutorComponent, SaveExecutorCommandMessage>(OnSave);
        SubscribeLocalEvent<ExecutorComponent, StartExecutionMessage>(OnExecute);

        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, AfterInteractEvent>(OnNetworkInteract);
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, UseInHandEvent>(OnNetworkActivate);

        SubscribeLocalEvent<ExecuteOnInteractComponent, UseInHandEvent>(OnUse);

        SubscribeLocalEvent<PacketNetworkComponent, ComponentInit>(OnPacketInit);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    public override void Update(float frameTime)
    {
        UpdateSystemCalls();
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

    private void OnNetworkInteract(Entity<PacketNetworkConfiguratorComponent> ent, ref AfterInteractEvent ev)
    {
        if (!TryComp<PacketNetworkComponent>(ev.Target, out var receiver)
            || ent.Comp.Addresses.Contains(receiver.Address))
            return;

        _popupSystem.PopupEntity(receiver.Address, ev.User, ev.User, PopupType.Medium);
        ent.Comp.Addresses.Add(receiver.Address);
    }

    private void OnNetworkActivate(Entity<PacketNetworkConfiguratorComponent> ent, ref UseInHandEvent ev)
    {
        if (ent.Comp.Frequency == null)
            return;

        _popupSystem.PopupEntity(CreateNetwork([..ent.Comp.Addresses], GetFrequency(ent.Comp.Frequency.Value)), ev.User, ev.User, PopupType.LargeCaution);
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

    private void OnPacketInit(Entity<PacketNetworkComponent> ent, ref ComponentInit ev)
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
