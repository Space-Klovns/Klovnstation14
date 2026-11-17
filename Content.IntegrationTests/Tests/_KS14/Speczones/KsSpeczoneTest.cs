#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Speczones;
using Content.Server.GameTicking.Events;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Speczones;
using Content.Server.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using NUnit.Framework;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Speczones;

/// <summary>
///     Speczones: they load when a round starts (and only if the cvar allows it), keep their map paused until somebody is
///         inserted, make their structures invincible, and block things that should not work inside them.
/// </summary>
/// <remarks>
///     Starting a round loads every speczone prototype there is, the real ones as well as the test one, so these
///         tests only look at the test speczone. They are dirty because that leaves maps and cached speczones behind.
/// </remarks>
[TestFixture]
[TestOf(typeof(SpeczoneSystem))]
public sealed class KsSpeczoneTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true, DummyTicker = true };

    private readonly ProtoId<SpeczonePrototype> _testSpeczone = "KsTestSpeczone";
    private static readonly EntProtoId Wall = "WallSolid";

    /// <summary>
    ///     Loaded into this test's own pair rather than through <c>[TestPrototypes]</c>: those are loaded into every pair,
    ///         and every round any other test starts would then load this speczone's map too, leaving a second grid
    ///         behind for tests that expect only their own (<c>InteractionTestTests.MapLoadingTest</c>). These tests
    ///         are dirty, so the pair goes away with it.
    /// </summary>
    private const string Prototypes = @"
- type: speczone
  id: KsTestSpeczone
  mapPath: /Maps/Test/_KS14/speczone_test.yml
";

    private void LoadTestSpeczonePrototype()
    {
        var changes = new Dictionary<Type, HashSet<string>>();
        SProtoMan.LoadString(Prototypes, overwrite: true, changed: changes);
        SProtoMan.ResolveResults();
        SProtoMan.ReloadPrototypes(changes);
    }

    /// <summary>
    ///     Gets rid of any speczones the pair already had, so each test starts from the same "nothing is loaded". The
    ///         pair may or may not have started a round, which loads them, before the test begins.
    /// </summary>
    private async Task ClearExistingSpeczonesAsync()
    {
        await Server.WaitPost(() =>
        {
            LoadTestSpeczonePrototype();

            var existing = SEntMan.AllEntities<SpeczoneComponent>().Select(speczone => speczone.Owner).ToList();
            foreach (var speczoneUid in existing)
                SEntMan.DeleteEntity(speczoneUid);
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Speczones.GetSpeczoneEntities(), Is.Empty, "precondition: no speczone is loaded");
            Assert.That(SEntMan.AllEntities<SpeczoneComponent>(), Is.Empty, "precondition: and no speczone map exists");
        });
    }

    private async Task StartRoundAsync()
    {
        await ClearExistingSpeczonesAsync();

        // Raising RoundStartingEvent for everyone would also run every other system's round start a second time, and
        //      some of those cannot cope with that. Hand it to the speczone system's own handler instead.
        var handler = typeof(SpeczoneSystem).GetMethod("OnRoundStarting", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handler, Is.Not.Null, "the speczone system's round start handler");
        await Server.WaitPost(() => handler!.Invoke(Speczones, [new RoundStartingEvent(1)]));
        await Pair.RunTicksSync(5);
    }

    private SpeczoneSystem Speczones => SEntMan.System<SpeczoneSystem>();

    private Entity<SpeczoneComponent> GetTestSpeczone()
    {
        Assert.That(Speczones.TryGetSpeczoneEntity(_testSpeczone, out var speczone), Is.True, "the test speczone is loaded");
        return speczone;
    }

    private async Task<EntityUid> SpawnOutsideEntityAsync(EntProtoId? prototype = null)
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid uid = default;
        await Server.WaitPost(() => uid = SEntMan.SpawnEntity(prototype, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f))));
        return uid;
    }

    [Test]
    public async Task StartingARoundLoadsThePausedSpeczoneWithItsEntryPoint()
    {
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            var speczone = GetTestSpeczone();
            Assert.That(speczone.Comp.PrototypeId.Id, Is.EqualTo("KsTestSpeczone"));
            Assert.That(speczone.Comp.EntryMarkers, Has.Count.EqualTo(1), "the map has exactly one entry point");
            Assert.That(Speczones.GetSpeczoneEntities(), Does.Contain("KsTestSpeczone"));

            var mapSystem = SEntMan.System<SharedMapSystem>();
            Assert.That(SEntMan.HasComponent<MapComponent>(speczone.Owner), Is.True);
            Assert.That(mapSystem.IsPaused(speczone.Owner), Is.True, "nobody is inside, so the map stays paused");
            Assert.That(mapSystem.IsInitialized(speczone.Owner), Is.True, "but it has been initialised");
        });
    }

    [Test]
    public async Task WithTheCvarOffNoSpeczoneIsLoaded()
    {
        await OverrideCVar(Side.Server, KsCCVars.SpeczonesEnabled, false);
        var outsider = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            Assert.That(Speczones.TryGetSpeczoneEntity(_testSpeczone, out _), Is.False);
            Assert.That(Speczones.GetSpeczoneEntities(), Is.Empty);
            Assert.That(SEntMan.AllEntities<SpeczoneComponent>(), Is.Empty, "no speczone map exists at all");

            var before = SEntMan.GetComponent<TransformComponent>(outsider).Coordinates;
            Assert.That(Speczones.TryInsertIntoSpeczone(outsider, null, out _), Is.False, "and there is nowhere to insert into");
            Assert.That(SEntMan.GetComponent<TransformComponent>(outsider).Coordinates, Is.EqualTo(before));
        });

        // The control: turn it back on, and the very next round start does load them.
        await OverrideCVar(Side.Server, KsCCVars.SpeczonesEnabled, true);
        await StartRoundAsync();
        await Server.WaitAssertion(() => GetTestSpeczone());
    }

    [Test]
    public async Task LoadedStructuresAreInvincibleUnlessMarkedAsSaved()
    {
        var outsideWall = await SpawnOutsideEntityAsync(Wall);
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            var speczone = GetTestSpeczone();

            var invincible = 0;
            var kept = 0;
            var query = SEntMan.AllEntityQueryEnumerator<AirtightComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
            {
                if (transform.MapUid != speczone.Owner)
                    continue;

                if (SEntMan.HasComponent<DamageableComponent>(uid))
                {
                    Assert.That(SEntMan.HasComponent<SpeczoneLoadAsSavedComponent>(uid), Is.True, "only the saved one keeps its damageable");
                    kept++;
                }
                else
                    invincible++;
            }

            Assert.That(invincible, Is.EqualTo(1), "the ordinary wall was made invincible");
            Assert.That(kept, Is.EqualTo(1), "the wall marked as saved was left alone");

            Assert.That(SEntMan.HasComponent<DamageableComponent>(outsideWall), Is.True, "the control: the same wall outside a speczone is untouched");
        });
    }

    [Test]
    public async Task InsertingMovesTheEntityToTheEntryPointAndUnpausesTheMap()
    {
        var wall = await SpawnOutsideEntityAsync(Wall);
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            var speczone = GetTestSpeczone();
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            Assert.That(SEntMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "precondition: anchored outside");

            Assert.That(Speczones.TryInsertIntoSpeczone(wall, "KsTestSpeczone", out var entryCoordinates), Is.True);

            var entryMarker = speczone.Comp.EntryMarkers.Single();
            Assert.That(entryCoordinates, Is.EqualTo(entryMarker.Comp.Coordinates));

            var transform = SEntMan.GetComponent<TransformComponent>(wall);
            Assert.That(transform.MapUid, Is.EqualTo(speczone.Owner), "it is on the speczone's map");
            Assert.That(transformSystem.GetWorldPosition(wall), Is.EqualTo(transformSystem.GetWorldPosition(entryMarker.Owner)).Using(new Vector2Comparer()));
            Assert.That(transform.Anchored, Is.False, "an anchored thing is unanchored on the way in");
            Assert.That(mapSystem.IsPaused(speczone.Owner), Is.False, "somebody is inside now, so the map runs");
        });
    }

    [Test]
    public async Task InsertingWithoutAnIdUsesTheFirstSpeczone()
    {
        var entity = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            // Any loaded speczone is fine: the real ones come along with the test one.
            Assert.That(Speczones.TryInsertIntoSpeczone(entity, null, out _), Is.True);

            var mapUid = SEntMan.GetComponent<TransformComponent>(entity).MapUid;
            Assert.That(mapUid, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<SpeczoneComponent>(mapUid), Is.True, "it ended up on a speczone map");
        });
    }

    [Test]
    public async Task InsertingIntoAnUnknownSpeczoneDoesNothing()
    {
        var entity = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        await Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetComponent<TransformComponent>(entity).Coordinates;

            Assert.That(Speczones.TryInsertIntoSpeczone(entity, "KsTestSpeczoneThatDoesNotExist", out var coordinates), Is.False);
            Assert.That(coordinates, Is.Null);
            Assert.That(SEntMan.GetComponent<TransformComponent>(entity).Coordinates, Is.EqualTo(before));
            Assert.That(SEntMan.System<SharedMapSystem>().IsPaused(GetTestSpeczone().Owner), Is.True, "and the speczone stayed paused");
        });
    }

    [Test]
    public async Task TheCommandInsertsTheEntityIntoTheNamedSpeczone()
    {
        var entity = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        var consoleHost = Server.ResolveDependency<IConsoleHost>();
        await Server.WaitPost(() => consoleHost.ExecuteCommand($"insertintospeczone {entity.Id} KsTestSpeczone"));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<TransformComponent>(entity).MapUid, Is.EqualTo(GetTestSpeczone().Owner)));
    }

    [Test]
    public async Task TheCommandWithAnUnknownSpeczoneLeavesTheEntityWhereItIs()
    {
        var entity = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        var consoleHost = Server.ResolveDependency<IConsoleHost>();
        EntityCoordinates before = default;
        await Server.WaitPost(() =>
        {
            before = SEntMan.GetComponent<TransformComponent>(entity).Coordinates;

            // Not an error, it just has nowhere to put the entity. (The commands that do print an error, such as a
            //      missing entity, cannot be run here: the test console turns error output into a failing log.)
            consoleHost.ExecuteCommand($"insertintospeczone {entity.Id} KsTestSpeczoneThatDoesNotExist");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<TransformComponent>(entity).Coordinates, Is.EqualTo(before), "there was no such speczone, so it stays put"));
    }

    [Test]
    public async Task InterferenceHappensOnlyInsideASpeczoneAndNeverForTheExempt()
    {
        var outside = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        EntityUid inside = default;
        EntityUid exemptInside = default;
        EntityUid bypassingUser = default;
        await Server.WaitPost(() =>
        {
            inside = SEntMan.SpawnEntity(null, GetTestSpeczone().Comp.EntryMarkers.Single().Comp.Coordinates);
            exemptInside = SEntMan.SpawnEntity(null, GetTestSpeczone().Comp.EntryMarkers.Single().Comp.Coordinates);
            SEntMan.AddComponent<AlwaysAllowedInSpeczoneComponent>(exemptInside);
            bypassingUser = SEntMan.SpawnEntity(null, GetTestSpeczone().Comp.EntryMarkers.Single().Comp.Coordinates);
            SEntMan.AddComponent<BypassInteractionChecksComponent>(bypassingUser);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(Speczones.AttemptInterfere(inside), Is.True, "using something inside a speczone is interfered with");
            Assert.That(Speczones.AttemptInterfere(outside), Is.False, "the control: the same outside is not");
            Assert.That(Speczones.AttemptInterfere(exemptInside), Is.False, "things that are always allowed are not interfered with");
            Assert.That(Speczones.AttemptInterfere(inside, bypassingUser), Is.False, "nor is a user who bypasses interaction checks");
        });
    }

    [Test]
    public async Task GunsThatBlockShootingInSpeczonesCannotFireThereButCanElsewhere()
    {
        var outsideGun = await SpawnOutsideEntityAsync();
        await StartRoundAsync();

        EntityUid insideGun = default;
        await Server.WaitPost(() =>
        {
            insideGun = SEntMan.SpawnEntity(null, GetTestSpeczone().Comp.EntryMarkers.Single().Comp.Coordinates);
            foreach (var gun in new[] { insideGun, outsideGun })
            {
                SEntMan.AddComponent<GunComponent>(gun);
                SEntMan.AddComponent<BlockShootingInSpeczoneComponent>(gun);
            }
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(TryShoot(insideGun), Is.False, "blocked inside a speczone");
            Assert.That(TryShoot(outsideGun), Is.True, "the control: allowed outside one");
        });

        bool TryShoot(EntityUid gunUid)
        {
            var ev = new ShotAttemptedEvent
            {
                User = gunUid,
                Used = (gunUid, SEntMan.GetComponent<GunComponent>(gunUid)),
            };
            SEntMan.EventBus.RaiseLocalEvent(gunUid, ref ev);
            return !ev.Cancelled;
        }
    }

    private sealed class Vector2Comparer : System.Collections.Generic.IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => (a - b).Length() < 0.01f;
        public int GetHashCode(Vector2 v) => 0;
    }
}
