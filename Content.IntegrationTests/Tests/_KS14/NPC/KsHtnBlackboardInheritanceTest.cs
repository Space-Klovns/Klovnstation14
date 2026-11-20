#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.HTN;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Random humanoid settings that set an HTN blackboard replace their parent's whole blackboard rather than adding
///         to it, so a child that only meant to change a key or two loses every other one. The Field Commander's
///         operatives lost theirs that way (ranges, hold times, the reload delay, and the Nav keys that let them open
///         doors and climb), and one missing key read with <c>GetValue</c> throws in an operator every update, which
///         stops the HTN update for every NPC after it.
/// </summary>
[TestFixture]
[TestOf(typeof(HTNComponent))]
public sealed class KsHtnBlackboardInheritanceTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task ChildBlackboardsKeepTheirParentsKeys()
    {
        await Server.WaitAssertion(() =>
        {
            var problems = new List<string>();

            // Read from each prototype's mapping, as inherited, which abstract parents have too.
            HashSet<string>? Keys(Type kind, string id)
            {
                if (!SProtoMan.TryGetMapping(kind, id, out var mapping) ||
                    !mapping.TryGet<SequenceDataNode>("components", out var components))
                    return null;

                foreach (var node in components)
                {
                    if (node is not MappingDataNode component ||
                        !component.TryGet<ValueDataNode>("type", out var type) ||
                        type.Value != "HTN")
                        continue;

                    return component.TryGet<MappingDataNode>("blackboard", out var blackboard)
                        ? blackboard.Keys.ToHashSet()
                        : null;
                }

                return null;
            }

            void Check<T>() where T : class, IPrototype, IInheritingPrototype
            {
                foreach (var prototype in SProtoMan.EnumeratePrototypes<T>())
                {
                    if (prototype.Parents == null || Keys(typeof(T), prototype.ID) is not { } childKeys)
                        continue;

                    foreach (var parent in prototype.Parents)
                    {
                        if (Keys(typeof(T), parent) is not { } parentKeys)
                            continue;

                        var missing = parentKeys.Except(childKeys).OrderBy(key => key).ToList();
                        if (missing.Count > 0)
                            problems.Add($"{prototype.ID} has lost {string.Join(", ", missing)} from {parent}'s blackboard");
                    }
                }
            }

            // Entities are left out: a few upstream turrets, and test ones built on them, drop a key they never use.
            Check<RandomHumanoidSettingsPrototype>();

            Assert.That(problems, Is.Empty,
                "a child that sets a blackboard must restate every key of its parent's, as setting one replaces the whole of it:\n" +
                string.Join("\n", problems));
        });
    }
}
