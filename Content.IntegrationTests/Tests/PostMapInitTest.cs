using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Utility;
using YamlDotNet.RepresentationModel;
using Content.Server.Administration.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Roles;
using Content.Shared.Station; // KS14
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    public sealed class PostMapInitTest : GameTest
    {
        public override PoolSettings PoolSettings => new PoolSettings()
        {
            Connected = true,
            Dirty = true,
        };

        private const bool SkipTestMaps = true;
        private const string TestMapsPath = "/Maps/Test/";

        private static readonly string[] NoSpawnMaps =
        {
            "CentComm",
            "Dart"
        };

        private static readonly string[] Grids =
        {
            "/Maps/centcomm.yml",
            AdminTestArenaSystem.ArenaMapPath
        };

        /// <summary>
        /// A dictionary linking maps to collections of entity prototype ids that should be exempt from "DoNotMap" restrictions.
        /// </summary>
        /// <remarks>
        /// This declares that the listed entity prototypes are allowed to be present on the map
        /// despite being categorized as "DoNotMap", while any unlisted prototypes will still
        /// cause the test to fail.
        /// </remarks>
        private static readonly Dictionary<string, HashSet<EntProtoId>> DoNotMapWhitelistSpecific = new()
        {
            {"/Maps/bagel.yml", ["RubberStampMime"]},
            {"/Maps/reach.yml", ["HandheldCrewMonitor"]},
            {"/Maps/Shuttles/ShuttleEvent/honki.yml", ["GoldenBikeHorn", "RubberStampClown"]},
            {"/Maps/Shuttles/ShuttleEvent/syndie_evacpod.yml", ["RubberStampSyndicate"]},
            {"/Maps/Shuttles/ShuttleEvent/cruiser.yml", ["ShuttleGunPerforator"]},
            {"/Maps/Shuttles/ShuttleEvent/instigator.yml", ["ShuttleGunFriendship"]},
            {"/Maps/_KS14/Grids/Scenarios/tiderfall/listeningpost.yml", ["RubberStampSyndicate"]}, //KS14
        };

        /// <summary>
        /// Maps listed here are given blanket freedom to contain "DoNotMap" entities. Use sparingly.
        /// </summary>
        /// <remarks>
        /// It is also possible to whitelist entire directories here. For example, adding
        /// "/Maps/Shuttles/**" will whitelist all shuttle maps.
        /// </remarks>
        private static readonly string[] DoNotMapWhitelist =
        {
            "/Maps/centcomm.yml",
            "/Maps/_Moffstation/frezon.yml", // Contains handheld crew monitor & other head of staff items
            "/Maps/Shuttles/AdminSpawn/ERT-Small-Deathsquad.yml" // handheld crew mon
        };

        /// <summary>
        /// Converts the above globs into regex so your eyes dont bleed trying to add filepaths.
        /// </summary>
        private static readonly Regex[] DoNotMapWhiteListRegexes = DoNotMapWhitelist
            .Select(glob => new Regex(GlobToRegex(glob), RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();

        public /* KS14: made public */ static readonly string[] GameMaps = GameDataScrounger.PrototypesOfKind<GameMapPrototype>().Where(x => x != PoolManager.TestMap).ToArray();
        private static readonly ResPath[] AllMapFiles = GameDataScrounger.FilesInDirectoryInVfs("/Maps", "*.yml");
        private static readonly ResPath[] ShuttleMapFiles = GameDataScrounger.FilesInDirectoryInVfs("/Maps/Shuttles", "*.yml");

        private static readonly ProtoId<EntityCategoryPrototype> DoNotMapCategory = "DoNotMap";

        /// <summary>
        /// Asserts that specific files have been saved as grids and not maps.
        /// </summary>
        [Test, TestCaseSource(nameof(Grids))]
        [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
        public async Task GridsLoadableTest(string mapFile)
        {
            var pair = Pair;
            var server = pair.Server;

            var entityManager = server.ResolveDependency<IEntityManager>(); // KS14
            var mapLoaderSystem = entityManager.System<MapLoaderSystem>(); // KS14
            var sharedMapSystem = entityManager.System<SharedMapSystem>(); // KS14
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var path = new ResPath(mapFile);

            await server.WaitPost(() =>
            {
                sharedMapSystem.CreateMap(out var mapId); // KS14
                try
                {
                    Assert.That(mapLoaderSystem.TryLoadGrid(mapId, path, out var grid)); //KS14
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load map {mapFile}, was it saved as a map instead of a grid?", ex);
                }

                sharedMapSystem.DeleteMap(mapId); // KS14
            });
        }

        /// <summary>
        /// Asserts that shuttles are loadable and have been saved as grids and not maps.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(ShuttleMapFiles))]
        [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
        public async Task ShuttlesLoadableTest(ResPath path)
        {
            var pair = Pair;
            var server = pair.Server;

            var entityManager = server.ResolveDependency<IEntityManager>(); // KS14
            var mapLoaderSystem = entityManager.System<MapLoaderSystem>(); // KS14
            var sharedMapSystem = entityManager.System<SharedMapSystem>(); // KS14
            var cfg = server.ResolveDependency<IConfigurationManager>();

            await server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    sharedMapSystem.CreateMap(out var mapId); // KS14
                    try
                    {
                        Assert.That(mapLoaderSystem.TryLoadGrid(mapId, path, out _), // KS14
                            $"Failed to load shuttle {path}, was it saved as a map instead of a grid?");
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to load shuttle {path}, was it saved as a map instead of a grid?",
                            ex);
                    }
                    sharedMapSystem.DeleteMap(mapId); // KS14
                });
            });
        }

        [Test]
        [TestCaseSource(nameof(AllMapFiles))]
        public async Task NoSavedPostMapInitTest(ResPath map)
        {
            var pair = Pair;
            var server = pair.Server;

            var resourceManager = server.ResolveDependency<IResourceManager>();
            var protoManager = server.ResolveDependency<IPrototypeManager>();
            var loader = server.System<MapLoaderSystem>();

            var rootedPath = map.ToRootedPath();

            var isV7Map = false;

            // ReSharper disable once RedundantLogicalConditionalExpressionOperand
            if (SkipTestMaps && rootedPath.ToString().StartsWith(TestMapsPath, StringComparison.Ordinal))
            {
                return; // We just pass immediately.
            }

            if (!resourceManager.TryContentFileRead(rootedPath, out var fileStream))
            {
                Assert.Fail($"Map not found: {rootedPath}");
            }

            using var reader = new StreamReader(fileStream);
            var yamlStream = new YamlStream();

            yamlStream.Load(reader);

            var root = yamlStream.Documents[0].RootNode;
            var meta = root["meta"];
            var version = meta["format"].AsInt();

            // TODO MAP TESTS
            // Move this to some separate test?
            CheckDoNotMap(map, root, protoManager);

            if (version >= 7)
            {
                isV7Map = true;
            }
            else
            {
                var postMapInit = meta["postmapinit"].AsBool();
                Assert.That(postMapInit, Is.False, $"Map {map.Filename} was saved postmapinit");
            }

            var deps = server.ResolveDependency<IEntitySystemManager>().DependencyCollection;
            var ev = new BeforeEntityReadEvent();
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ev);

            if (isV7Map)
            {
                Assert.That(IsPreInit(map, loader, deps, ev.RenamedPrototypes, ev.DeletedPrototypes));
            }

            // Check that the test actually does manage to catch post-init maps and isn't just blindly passing everything.
            // To that end, create a new post-init map and try verify it.
            var mapSys = server.System<SharedMapSystem>();
            MapId id = default;
            await server.WaitPost(() => mapSys.CreateMap(out id, runMapInit: false));
            await server.WaitPost(() => server.EntMan.Spawn(null, new MapCoordinates(0, 0, id)));

            // First check that a pre-init version passes
            var path = new ResPath($"{nameof(NoSavedPostMapInitTest)}.yml");
            Assert.That(loader.TrySaveMap(id, path));
            Assert.That(IsPreInit(path, loader, deps, ev.RenamedPrototypes, ev.DeletedPrototypes));

            // and the post-init version fails.
            await server.WaitPost(() => mapSys.InitializeMap(id));
            Assert.That(loader.TrySaveMap(id, path));
            Assert.That(IsPreInit(path, loader, deps, ev.RenamedPrototypes, ev.DeletedPrototypes), Is.False);
        }

        private bool IsWhitelistedForMap(EntProtoId protoId, ResPath map)
        {
            if (!DoNotMapWhitelistSpecific.TryGetValue(map.ToString(), out var allowedProtos))
                return false;

            return allowedProtos.Contains(protoId);
        }

        /// <summary>
        /// Check that maps do not have any entities that belong to the DoNotMap entity category
        /// </summary>
        private void CheckDoNotMap(ResPath map, YamlNode node, IPrototypeManager protoManager)
        {
            foreach (var regex in DoNotMapWhiteListRegexes)
            {
                if (regex.IsMatch(map.ToString()))
                    return;
            }

            var yamlEntities = node["entities"];
            var dnmCategory = protoManager.Index(DoNotMapCategory);

            // Make a set containing all the specific whitelisted proto ids for this map
            HashSet<EntProtoId> unusedExemptions = DoNotMapWhitelistSpecific.TryGetValue(map.ToString(), out var exemptions) ? new(exemptions) : [];
            Assert.Multiple(() =>
            {
                foreach (var yamlEntity in (YamlSequenceNode)yamlEntities)
                {
                    var protoId = yamlEntity["proto"].AsString();

                    // This doesn't properly handle prototype migrations, but thats not a significant issue.
                    if (!protoManager.TryIndex(protoId, out var proto))
                        continue;

                    Assert.That(!proto.Categories.Contains(dnmCategory) || IsWhitelistedForMap(protoId, map),
                        $"\nMap {map} contains entities in the DO NOT MAP category ({proto.Name})");

                    // The proto id is used on this map, so remove it from the set
                    unusedExemptions.Remove(protoId);
                }
            });

            // If there are any proto ids left, they must not have been used in the map!
            Assert.That(unusedExemptions, Is.Empty,
                $"Map {map} has DO NOT MAP entities whitelisted that are not present in the map: {string.Join(", ", unusedExemptions)}");
        }

        private bool IsPreInit(ResPath map,
            MapLoaderSystem loader,
            IDependencyCollection deps,
            Dictionary<string, string> renamedPrototypes,
            HashSet<string> deletedPrototypes)
        {
            if (!loader.TryReadFile(map, out var data))
            {
                Assert.Fail($"Failed to read {map}");
                return false;
            }

            var reader = new EntityDeserializer(deps,
                data,
                DeserializationOptions.Default,
                renamedPrototypes,
                deletedPrototypes);

            if (!reader.TryProcessData())
            {
                Assert.Fail($"Failed to process {map}");
                return false;
            }

            foreach (var mapId in reader.MapYamlIds)
            {
                var mapData = reader.YamlEntities[mapId];
                if (mapData.PostInit)
                    return false;
            }

            return true;
        }

        [Test, TestCaseSource(nameof(GameMaps))]
        [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
        public async Task GameMapsLoadableTest(string mapProto)
        {
            var pair = Pair;
            var server = pair.Server;

            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>(); // KS14
            var mapLoaderSystem = entityManager.System<MapLoaderSystem>(); // KS14
            var sharedMapSystem = entityManager.System<SharedMapSystem>();  // KS14
            var prototypeManager = server.ResolveDependency<IPrototypeManager>(); // KS14
            var gameTicker = entityManager.EntitySysManager.GetEntitySystem<GameTicker>(); // KS14
            var shuttleSystem = entityManager.EntitySysManager.GetEntitySystem<ShuttleSystem>(); // KS14
            var cfg = server.ResolveDependency<IConfigurationManager>();

            await server.WaitPost(() =>
            {
                MapId mapId;
                try
                {
                    var opts = DeserializationOptions.Default with { InitializeMaps = true };
                    gameTicker.LoadGameMap(prototypeManager.Index<GameMapPrototype>(mapProto), out mapId, opts); // KS14
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load map {mapProto}", ex);
                }

                sharedMapSystem.CreateMap(out var shuttleMap); // KS14

                try
                {
                    var grids = mapManager.GetAllGrids(mapId).ToList();

                    // KS14 - Start
                    // Collect all unique stations that physically exist on our newly loaded map
                    var stationsOnMap = new HashSet<EntityUid>();
                    foreach (var grid in grids)
                    {
                        if (entityManager.TryGetComponent<StationMemberComponent>(grid.Owner, out var member))
                            stationsOnMap.Add(member.Station);
                    }

                    var sharedStationSystem = entityManager.System<SharedStationSystem>();

                    // Test shuttle docking and job spawn points for EVERY station on this map
                    foreach (var stationUid in stationsOnMap)
                    {
                        if (entityManager.TryGetComponent<StationEmergencyShuttleComponent>(stationUid, out var stationEvac))
                        {
                            var stationDataEntity = new Entity<StationDataComponent>(stationUid, entityManager.GetComponent<StationDataComponent>(stationUid));

                            // Get the largest grid for this specific station.
                            var targetGrid = sharedStationSystem.GetLargestGrid(stationDataEntity);

                            Assert.That(targetGrid, Is.Not.Null, $"Station {stationUid} on map {mapProto} has no grids for the docking test.");

                            // Do not attempt to physically dock shuttles to 0-area grids (spawners) as they lack physical colliders
                            var aabb = entityManager.GetComponent<MapGridComponent>(targetGrid.Value).LocalAABB;
                            if (aabb.Width > 0 && aabb.Height > 0)
                            {
                                var shuttlePath = stationEvac.EmergencyShuttlePath;
                                Assert.That(mapLoaderSystem.TryLoadGrid(shuttleMap, shuttlePath, out var shuttle), $"Failed to load {shuttlePath}");

                                var shuttleEntity = new Entity<ShuttleComponent>(shuttle!.Value.Owner, entityManager.GetComponent<ShuttleComponent>(shuttle!.Value.Owner));

                                Assert.That(
                                    shuttleSystem.TryFTLDock(shuttleEntity.Owner,
                                        shuttleEntity.Comp,
                                        targetGrid.Value),
                                    $"Shuttle failed to dock to station {stationUid} on map {mapProto}");
                            }
                        }

                        // Skip job spawn validation for scenario maps like Tiderfall
                        if (entityManager.HasComponent<StationJobsComponent>(stationUid) && mapProto != "Tiderfall")
                        {
                            var stationGrids = entityManager.GetComponent<StationDataComponent>(stationUid).Grids;

                            var lateSpawns = 0;
                            var comp = entityManager.GetComponent<StationJobsComponent>(stationUid);
                            var jobs = new HashSet<ProtoId<JobPrototype>>(comp.SetupAvailableJobs.Keys);

                            // Scope spawn point checks tightly to grids belonging to THIS station using performant flat queries.
                            // Instead of recursively searching the map hierarchy which visits thousands of irrelevant entities,
                            // we directly query only the entities possessing spawn point components, reducing the scope significantly.
                            var spawnQuery = entityManager.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
                            while (spawnQuery.MoveNext(out var uid, out var spawn, out var xform))
                            {
                                var spawnEntity = new Entity<SpawnPointComponent>(uid, spawn);

                                // Filter the globally queried components to ensure they reside on a grid belonging to the current station.
                                if (xform.GridUid == null || !stationGrids.Contains(xform.GridUid.Value))
                                    continue;

                                // Track valid late join spawn points.
                                if (spawnEntity.Comp.SpawnType == SpawnPointType.LateJoin)
                                    lateSpawns++;

                                // Remove fulfilled job spawn points from the required jobs hashset.
                                if (spawnEntity.Comp.SpawnType == SpawnPointType.Job && spawnEntity.Comp.Job != null)
                                    jobs.Remove(spawnEntity.Comp.Job.Value);
                            }

                            // We also need to check ContainerSpawnPointComponent, as some jobs spawn inside lockers/containers (e.g., cryo-pods, survivor lockers).
                            var containerSpawnQuery = entityManager.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
                            while (containerSpawnQuery.MoveNext(out var uid, out var containerSpawn, out var xform))
                            {
                                var containerSpawnEntity = new Entity<ContainerSpawnPointComponent>(uid, containerSpawn);

                                if (xform.GridUid == null || !stationGrids.Contains(xform.GridUid.Value))
                                    continue;

                                if (containerSpawnEntity.Comp.SpawnType == SpawnPointType.LateJoin)
                                    lateSpawns++;

                                if ((containerSpawnEntity.Comp.SpawnType == SpawnPointType.Job || containerSpawnEntity.Comp.SpawnType == SpawnPointType.Unset) && containerSpawnEntity.Comp.Job != null)
                                    jobs.Remove(containerSpawnEntity.Comp.Job.Value);
                            }

                            if (!NoSpawnMaps.Contains(mapProto))
                            {
                                Assert.That(lateSpawns, Is.GreaterThan(0), $"Found no latejoin spawn points for station {stationUid} on {mapProto}");
                            }

                            Assert.That(jobs, Is.Empty, $"There are no spawnpoints for {string.Join(", ", jobs)} on station {stationUid} ({mapProto}).");
                        }
                    }
                    // KS14 - End
                }
                finally
                {
                    // Guarantee memory cleanup even if an Assert fails above. Prevents OnDirtyDispose masks.
                    sharedMapSystem.DeleteMap(shuttleMap); // KS14

                    try
                    {
                        sharedMapSystem.DeleteMap(mapId); // KS14
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to delete map {mapProto}", ex);
                    }
                }
            });
        }

        [Test]
        [TestCaseSource(nameof(AllMapFiles))]
        [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
        public async Task NonGameMapsLoadableTest(ResPath mapPath)
        {
            var pair = Pair;
            var server = pair.Server;

            var mapLoader = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<MapLoaderSystem>();
            var resourceManager = server.ResolveDependency<IResourceManager>();
            var protoManager = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();

            var gameMaps = protoManager.EnumeratePrototypes<GameMapPrototype>().Select(o => o.MapPath).ToHashSet();


            if (gameMaps.Contains(mapPath))
            {
                // TODO: You might be able to save like, 1-2 seconds of test time if you eliminate these before
                //       actually needing a pair.
                return;
            }

            var rootedPath = mapPath.ToRootedPath();

            if (SkipTestMaps && rootedPath.ToString().StartsWith(TestMapsPath, StringComparison.Ordinal))
            {
                return;
            }

            await server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    // This bunch of files contains a random mixture of both map and grid files.
                    // TODO MAPPING organize files
                    var opts = MapLoadOptions.Default with
                    {
                        DeserializationOptions = DeserializationOptions.Default with
                        {
                            InitializeMaps = true,
                            LogOrphanedGrids = false
                        }
                    };

                    HashSet<Entity<MapComponent>> maps;

                    try
                    {
                        Assert.That(mapLoader.TryLoadGeneric(mapPath, out maps, out _, opts));
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to load map {mapPath}", ex);
                    }

                    try
                    {
                        foreach (var map in maps)
                        {
                            server.EntMan.DeleteEntity(map);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to delete map {mapPath}", ex);
                    }
                });
            });
        }

        /// <summary>
        /// Lets us the convert the filepaths to regex without eyeglaze trying to add new paths.
        /// </summary>
        private static string GlobToRegex(string glob)
        {
            var regex = Regex.Escape(glob)
                .Replace(@"\*\*", "**") // replace **
                .Replace(@"\*", "*")    // replace *
                .Replace("**", ".*")    // ** → match across folders
                .Replace("*", @"[^/]*") // * → match within a single folder
                .Replace(@"\?", ".");   // ? → any single character

            return $"^{regex}$";
        }
    }
}
