#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking.Presets;
using Content.Server.Maps;
using Content.Shared.Maps;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.GameTicking;

/// <summary>
///     Every preset with its own map pool has a map in it for a lone player. Setting a preset picks a map from its
///         pool among those whose player limits fit who is on, and with none (and no fallback map) the pick throws,
///         so an admin couldn't start the mode on a quiet server. The preset's own player limits are what keep it out
///         of votes and rotation.
/// </summary>
[TestFixture]
[TestOf(typeof(GameMapManager))]
public sealed class KsPresetMapPoolTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task EveryPresetPoolHasAMapForOnePlayer()
    {
        var server = Pair.Server;
        var failures = new List<string>();

        await server.WaitAssertion(() =>
        {
            foreach (var preset in server.ProtoMan.EnumeratePrototypes<GamePresetPrototype>())
            {
                if (preset.MapPool is not { } poolId)
                    continue;

                var pool = server.ProtoMan.Index(poolId);
                var anyFits = pool.Maps.Any(mapId =>
                    server.ProtoMan.TryIndex<GameMapPrototype>(mapId, out var map) &&
                    map.MinPlayers <= 1 &&
                    map.MaxPlayers >= 1);

                if (!anyFits)
                    failures.Add($"{preset.ID} ({poolId})");
            }
        });

        Assert.That(failures, Is.Empty, "these presets' pools have no map a single player could start");
    }
}
