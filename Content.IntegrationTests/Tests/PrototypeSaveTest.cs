#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text; // KS14
using Content.IntegrationTests.Fixtures;
using Content.Shared.Coordinates;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.IntegrationTests.Tests;

/// <summary>
///     This test ensure that when an entity prototype is spawned into an un-initialized map, its component data is not
///     modified during init. I.e., when the entity is saved to the map, its data is simply the default prototype data (ignoring transform component).
/// </summary>
/// <remarks>
///     If you are here because this test is failing on your PR, then one easy way of figuring out how to fix the prototype is to just
///     spawn it into a new empty map and seeing what the map yml looks like.
/// </remarks>
[TestFixture]
public sealed class PrototypeSaveTest : GameTest
{
    [Test]
    public async Task UninitializedSaveTest()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.ResolveDependency<IEntityManager>();
        var prototypeMan = server.ResolveDependency<IPrototypeManager>();
        var seriMan = server.ResolveDependency<ISerializationManager>();
        var compFact = server.ResolveDependency<IComponentFactory>();
        var mapSystem = server.System<SharedMapSystem>();

        var prototypes = new List<EntityPrototype>();
        EntityUid uid;

        await pair.CreateTestMap(false, "FloorSteel"); // Wires n such disable ambiance while under the floor
        var mapId = pair.TestMap.MapId;
        var grid = pair.TestMap.Grid;

        await server.WaitRunTicks(5);

        //Generate list of non-abstract prototypes to test
        foreach (var prototype in prototypeMan.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract)
                continue;

            if (pair.IsTestPrototype(prototype))
                continue;

            // Yea this test just doesn't work with this, it parents a grid to another grid and causes game logic to explode.
            if (prototype.Components.ContainsKey("MapGrid"))
                continue;

            // Currently mobs and such can't be serialized, but they aren't flagged as serializable anyways.
            if (!prototype.MapSavable)
                continue;

            if (prototype.SetSuffix == "DEBUG")
                continue;

            prototypes.Add(prototype);
        }

        // KS14 start: collect failures instead of failing inline, so the test can end with one message that
        //      names every offending prototype up front. CI runs with NUnit.ConsoleOut=0 and a minimal console
        //      logger, so anything past the head of the assertion message never reaches the GitHub log.
        var failures = new Dictionary<string, List<string>>();

        void AddFailure(string prototypeId, string reason)
        {
            if (!failures.TryGetValue(prototypeId, out var reasons))
                failures[prototypeId] = reasons = new List<string>();

            reasons.Add(reason);
        }
        // KS14 end

        var context = new TestEntityUidContext(seriMan, addFailure: AddFailure /* KS14: added arg */);

        await server.WaitAssertion(() =>
        {
            Assert.That(!mapSystem.IsInitialized(mapId));
            var testLocation = grid.Owner.ToCoordinates();

            // KS14: was an Assert.Multiple, redundant now that every failure below is collected and reported in
            //      one go at the end of the test. The braces stay so the loop keeps its indentation, and upstream
            //      changes inside it keep merging cleanly.
            {
                //Iterate list of prototypes to spawn
                foreach (var prototype in prototypes)
                {
                    uid = entityMan.SpawnEntity(prototype.ID, testLocation);
                    context.Prototype = prototype;

                    // get default prototype data
                    Dictionary<string, MappingDataNode> protoData = new();
                    try
                    {
                        context.WritingReadingPrototypes = true;

                        foreach (var (compType, comp) in prototype.Components)
                        {
                            context.WritingComponent = compType;
                            protoData.Add(compType, seriMan.WriteValueAs<MappingDataNode>(comp.Component.GetType(), comp.Component, alwaysWrite: true, context: context));
                        }

                        context.WritingComponent = string.Empty;
                        context.WritingReadingPrototypes = false;
                    }
                    catch (Exception e)
                    {
                        AddFailure(prototype.ID, $"failed to convert into yaml. Exception: {e.Message}"); // KS14: was Assert.Fail
                        continue;
                    }

                    var comps = new HashSet<IComponent>(entityMan.GetComponents(uid));
                    var compNames = new HashSet<string>(comps.Count);
                    foreach (var component in comps)
                    {
                        var compType = component.GetType();
                        var compName = compFact.GetComponentName(compType);
                        compNames.Add(compName);

                        if (compType == typeof(MetaDataComponent) || compType == typeof(TransformComponent) || compType == typeof(FixturesComponent))
                            continue;

                        MappingDataNode compMapping;
                        try
                        {
                            context.WritingComponent = compName;
                            compMapping = seriMan.WriteValueAs<MappingDataNode>(compType, component, alwaysWrite: true, context: context);
                        }
                        catch (Exception e)
                        {
                            AddFailure(prototype.ID, $"failed to serialize component {compName}. Exception: {e.Message}"); // KS14: was Assert.Fail
                            continue;
                        }

                        if (protoData.TryGetValue(compName, out var protoMapping))
                        {
                            var diff = compMapping.Except(protoMapping);

                            if (diff != null && diff.Children.Count != 0)
                                AddFailure(prototype.ID, $"modifies component on spawn: {compName}. Modified yaml:\n{diff}"); // KS14: was Assert.Fail
                        }
                        else
                        {
                            AddFailure(prototype.ID, $"gains a component on spawn: {compName}"); // KS14: was Assert.Fail
                        }
                    }

                    // An entity may also remove components on init -> check no components are missing.
                    foreach (var (compType, comp) in prototype.Components)
                    {
                        if (!compNames.Contains(compType)) // KS14: was an Assert.That
                            AddFailure(prototype.ID, $"removes component {compType} on spawn.");
                    }

                    if (!entityMan.Deleted(uid))
                        entityMan.DeleteEntity(uid);
                }
            }

            // KS14 start: report everything in one go, prototype ids first.
            if (failures.Count != 0)
                Assert.Fail(BuildFailureMessage(failures));
            // KS14 end
        });
    }

    // KS14 start: the ids go on the first line because that is the part of the message that survives CI - it runs
    //      with NUnit.ConsoleOut=0 and a minimal console logger, so a failure that only names its prototypes deep
    //      inside a wall of yaml diffs tells the reader on GitHub nothing. Details follow, capped so that a bad
    //      merge breaking hundreds of prototypes still leaves a readable log.
    private const int MaxDetailedPrototypes = 20;

    private static string BuildFailureMessage(Dictionary<string, List<string>> failures)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{failures.Count} prototype(s) failed the uninitialized-save test: {string.Join(", ", failures.Keys)}");

        foreach (var (prototypeId, reasons) in failures.Take(MaxDetailedPrototypes))
        {
            builder.AppendLine();
            builder.AppendLine($"{prototypeId}:");

            foreach (var reason in reasons)
            {
                builder.AppendLine($"  - {reason}");
            }
        }

        if (failures.Count > MaxDetailedPrototypes)
        {
            builder.AppendLine();
            builder.AppendLine($"...and {failures.Count - MaxDetailedPrototypes} more prototype(s), named on the first line of this message.");
        }

        return builder.ToString();
    }
    // KS14 end

    public sealed class TestEntityUidContext : ISerializationContext,
        ITypeSerializer<EntityUid, ValueDataNode>
    {
        public SerializationManager.SerializerProvider SerializerProvider { get; }
        public bool WritingReadingPrototypes { get; set; }

        public string WritingComponent = string.Empty;
        public EntityPrototype? Prototype;

        private readonly Action<string, string> _addFailure; // KS14: see the failure collection in the test above

        public TestEntityUidContext(ISerializationManager ser, Action<string, string>? addFailure = null /* KS14: added param */)
        {
            // KS14: PrototypeTests only validates serialization and collects nothing, so a caller that passes no
            //      collector keeps failing inline the way this did before.
            _addFailure = addFailure ?? ((prototypeId, reason) => Assert.Fail($"Prototype {prototypeId} {reason}"));

            SerializerProvider = new(ser);
            SerializerProvider.RegisterSerializer(this);
        }

        ValidationNode ITypeValidator<EntityUid, ValueDataNode>.Validate(ISerializationManager serializationManager,
                ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
        {
            return new ValidatedValueNode(node);
        }

        public DataNode Write(ISerializationManager serializationManager, EntityUid value,
            IDependencyCollection dependencies, bool alwaysWrite = false,
            ISerializationContext? context = null)
        {
            if (WritingComponent != "Transform" && Prototype?.HideSpawnMenu == false)
            {
                // Maybe this will be necessary in the future, but at the moment it just indicates that there is some
                // issue, like a non-nullable entityUid data-field. If a component MUST have an entity uid to work with,
                // then the prototype very likely has to be a no-spawn entity that is never meant to be directly spawned.
                _addFailure(Prototype.ID, $"saves an entity uid while uninitialized. Component: {WritingComponent}"); // KS14: was Assert.Fail
            }

            return new ValueDataNode(value.ToString());
        }

        EntityUid ITypeReader<EntityUid, ValueDataNode>.Read(ISerializationManager serializationManager,
            ValueDataNode node,
            IDependencyCollection dependencies,
            SerializationHookContext hookCtx,
            ISerializationContext? context, ISerializationManager.InstantiationDelegate<EntityUid>? instanceProvider)
        {
            return EntityUid.Parse(node.Value);
        }
    }
}
