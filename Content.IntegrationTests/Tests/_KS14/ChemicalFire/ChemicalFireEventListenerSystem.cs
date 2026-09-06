using System.Collections.Generic;
using Content.IntegrationTests.Tests.Helpers;
using Content.Server.Atmos;
using Content.Shared.Atmos;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._KS14.ChemicalFire;

/// <summary>
///     Counts the tile fire events entities are handed, per entity.
/// </summary>
/// <remarks>
///     Nothing in content acts on <see cref="TileExtinguishEvent"/> except fire alarms and chemfires
///         themselves, so a chemfire failing to announce that its tile stopped burning is otherwise invisible.
///     Listening on <see cref="TestListenerComponent"/> rather than on anything a fire would ordinarily reach:
///         the event bus allows only one subscription per component and event, and FlammableSystem already
///         holds the one for <see cref="TileFireEvent"/>.
///     Subscriptions are locked once the server has started, so this has to be loaded through
///         <c>LoadExtraSystemType</c> rather than created by a test.
/// </remarks>
[Reflect(false)]
public sealed partial class ChemicalFireEventListenerSystem : EntitySystem
{
    public readonly Dictionary<EntityUid, int> TileFireEventCounts = [];
    public readonly Dictionary<EntityUid, int> TileExtinguishEventCounts = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TestListenerComponent, TileFireEvent>(OnTileFire);
        SubscribeLocalEvent<TestListenerComponent, TileExtinguishEvent>(OnTileExtinguish);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public void Clear()
    {
        TileFireEventCounts.Clear();
        TileExtinguishEventCounts.Clear();
    }

    public int GetTileFireEventCount(EntityUid uid)
        => TileFireEventCounts.GetValueOrDefault(uid);

    public int GetTileExtinguishEventCount(EntityUid uid)
        => TileExtinguishEventCounts.GetValueOrDefault(uid);

    private void OnTileFire(Entity<TestListenerComponent> entity, ref TileFireEvent args)
        => TileFireEventCounts[entity.Owner] = GetTileFireEventCount(entity.Owner) + 1;

    // Not a by-ref event, so this cannot take the Entity<T> form the other handler does.
    private void OnTileExtinguish(EntityUid uid, TestListenerComponent listenerComponent, TileExtinguishEvent args)
        => TileExtinguishEventCounts[uid] = GetTileExtinguishEventCount(uid) + 1;

    private void OnRoundRestart(RoundRestartCleanupEvent args)
        => Clear();
}
