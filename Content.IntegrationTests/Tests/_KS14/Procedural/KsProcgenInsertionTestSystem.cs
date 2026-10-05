using System;
using System.Collections.Generic;
using System.Threading;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Shuttles.Components;
using Content.Shared.Placeable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

/// <summary>Pair-local insertion/initialization fault fixture; inactive without an explicit target prototype.</summary>
public sealed class KsProcgenInsertionTestSystem : EntitySystem
{
    public string? TargetPrototypeId;
    public int ItemSlotAttempts;
    public int ContainerAttempts;
    public int CancelItemSlotOnAttempt;
    public int CancelContainerOnAttempt;
    public int ThrowItemSlotOnAttempt;
    public int ThrowContainerOnAttempt;
    public bool ThrowOnMapInit;
    public int MapInitAttempts;
    public CancellationTokenSource? CancellationSource;
    public int CancelTokenOnContainerAttempt;
    public bool CancelTokenOnMapInit;
    public int SpawnExtraOnMapInit;
    public bool SpawnExtraOutsideMap;
    public readonly List<EntityUid> ExtraSpawnedEntities = [];
    public Action? MapInitAction;
    public Action? StartupAction;
    public int SpawnExtraOnStartup;
    public bool ThrowOnStartup;
    public Action<EntityUid, EntityUid>? ItemPlacedAction;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleDestinationCoordinatesComponent, ItemSlotInsertAttemptEvent>(OnItemSlotAttempt);
        SubscribeLocalEvent<ShuttleDestinationCoordinatesComponent, ContainerGettingInsertedAttemptEvent>(OnContainerAttempt);
        SubscribeLocalEvent<ShuttleDestinationCoordinatesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShuttleDestinationCoordinatesComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemPlacerComponent, ItemPlacedEvent>(OnItemPlaced);
    }

    public void Reset()
    {
        TargetPrototypeId = null;
        ItemSlotAttempts = ContainerAttempts = CancelItemSlotOnAttempt = CancelContainerOnAttempt = 0;
        ThrowItemSlotOnAttempt = ThrowContainerOnAttempt = MapInitAttempts = CancelTokenOnContainerAttempt = 0;
        ThrowOnMapInit = CancelTokenOnMapInit = false;
        CancellationSource = null;
        SpawnExtraOnMapInit = 0;
        SpawnExtraOutsideMap = false;
        ExtraSpawnedEntities.Clear();
        MapInitAction = null;
        StartupAction = null;
        SpawnExtraOnStartup = 0;
        ThrowOnStartup = false;
        ItemPlacedAction = null;
    }

    private bool Selected(EntityUid uid) => TargetPrototypeId != null &&
        MetaData(uid).EntityPrototype?.ID == TargetPrototypeId;

    private void OnItemPlaced(Entity<ItemPlacerComponent> entity, ref ItemPlacedEvent args)
    {
        if (Selected(entity.Owner))
            ItemPlacedAction?.Invoke(args.OtherEntity, entity.Owner);
    }

    private void OnItemSlotAttempt(Entity<ShuttleDestinationCoordinatesComponent> entity, ref ItemSlotInsertAttemptEvent args)
    {
        if (!Selected(entity.Owner))
            return;
        ItemSlotAttempts++;
        if (ItemSlotAttempts == ThrowItemSlotOnAttempt)
            throw new InvalidOperationException("KS procgen fixture item-slot failure.");
        if (ItemSlotAttempts == CancelItemSlotOnAttempt)
            args.Cancelled = true;
    }

    private void OnContainerAttempt(EntityUid uid, ShuttleDestinationCoordinatesComponent component,
        ContainerGettingInsertedAttemptEvent args)
    {
        if (!Selected(uid))
            return;
        ContainerAttempts++;
        if (ContainerAttempts == ThrowContainerOnAttempt)
            throw new InvalidOperationException("KS procgen fixture container failure.");
        if (ContainerAttempts == CancelTokenOnContainerAttempt)
            CancellationSource!.Cancel();
        if (ContainerAttempts == CancelContainerOnAttempt)
            args.Cancel();
    }

    private void OnMapInit(Entity<ShuttleDestinationCoordinatesComponent> entity, ref MapInitEvent args)
    {
        if (!Selected(entity.Owner))
            return;
        MapInitAttempts++;
        MapInitAction?.Invoke();
        for (var index = 0; index < SpawnExtraOnMapInit; index++)
        {
            ExtraSpawnedEntities.Add(SpawnExtraOutsideMap ? Spawn("Paper", doMapInit: false) :
                Spawn("Paper", Transform(entity.Owner).Coordinates));
        }
        if (CancelTokenOnMapInit)
            CancellationSource!.Cancel();
        if (ThrowOnMapInit)
            throw new InvalidOperationException("KS procgen fixture map initialization failure.");
    }

    private void OnStartup(Entity<ShuttleDestinationCoordinatesComponent> entity, ref ComponentStartup args)
    {
        if (!Selected(entity.Owner))
            return;
        StartupAction?.Invoke();
        for (var index = 0; index < SpawnExtraOnStartup; index++)
            ExtraSpawnedEntities.Add(Spawn("Paper", doMapInit: false));
        if (ThrowOnStartup)
            throw new InvalidOperationException("KS procgen fixture startup failure.");
    }
}
