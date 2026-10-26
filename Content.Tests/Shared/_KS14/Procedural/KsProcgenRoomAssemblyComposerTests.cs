using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenRoomAssemblyComposerTests
{
    [Test]
    public void RepeatedCoresKeepSupportTargetsInTheirOwnInstance()
    {
        var assembly = Assembly();
        var result = KsProcgenRoomAssemblyComposer.Compose("Room", Instances(assembly));
        Assert.That(result.Issue, Is.Null);
        Assert.That(result.Assembly!.Members.Count, Is.EqualTo(4));
        Assert.That(result.Assembly.Relations.Count, Is.EqualTo(2));
        foreach (var relation in result.Assembly.Relations)
        {
            var subject = result.Bindings.Single(binding => binding.MemberId == relation.Subject);
            var target = result.Bindings.Single(binding => binding.MemberId == relation.Target);
            Assert.That(subject.CopyIndex, Is.EqualTo(target.CopyIndex));
            Assert.That(subject.SourceMemberId, Is.EqualTo("Device"));
            Assert.That(target.SourceMemberId, Is.EqualTo("Table"));
        }
        Assert.That(result.EnginePlacementVerified, Is.False);
    }

    [Test]
    public void CompositionOrderReplaysAndInputEntriesAreDetached()
    {
        var source = Assembly();
        var instances = Instances(source);
        var first = KsProcgenRoomAssemblyComposer.Compose("Room", instances);
        var permuted = source with { Members = source.Members.Reverse().ToArray(), Relations = source.Relations.Reverse().ToArray() };
        var second = KsProcgenRoomAssemblyComposer.Compose("Room", Instances(permuted).Reverse().ToArray());
        Assert.That(second.Assembly!.VariantId, Is.EqualTo(first.Assembly!.VariantId));
        Assert.That(second.Bindings, Is.EqualTo(first.Bindings));
        Assert.That(second.Assembly.Relations, Is.EqualTo(first.Assembly.Relations));
        source = source.WithEntry(source.Members[0].Id, entry => entry with { Footprint = [new(0, 0), new(1, 0)], AllowedQuarterTurns = [] });
        Assert.That(source.Members[0].Entry.Footprint.Count, Is.EqualTo(2));
        Assert.That(first.Assembly.Members.All(member => member.Entry.Footprint.Count == 1), Is.True);
        Assert.That(first.Assembly.Members.All(member => member.Entry.AllowedQuarterTurns.Count == 4), Is.True);
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) first.Assembly.Members[0].Entry.Footprint).Add(new(2, 0)));
        Assert.That(first.Assembly.Members[2].Entry.Footprint.Count, Is.EqualTo(1));
    }

    [Test]
    public void UnknownTargetCannotBindToAnotherCore()
    {
        var source = Assembly();
        var bad = source with { Relations = source.Relations.Select(relation => relation with { Target = "OtherTable" }).ToArray() };
        var result = KsProcgenRoomAssemblyComposer.Compose("Room", Instances(bad));
        Assert.That(result.Issue!.Code, Is.EqualTo("InvalidRoomAssemblyRelations"));
        Assert.That(result.Assembly, Is.Null);
        Assert.That(result.Bindings, Is.Empty);
    }

    [Test]
    public void DuplicateCopiesRejectAtomically()
    {
        var source = Assembly();
        var result = KsProcgenRoomAssemblyComposer.Compose("Room", new[]
            { new KsProcgenRoomAssemblyInstance("Pack", "Core", 0, source), new("Pack", "Core", 0, source) });
        Assert.That(result.Issue!.Code, Is.EqualTo("DuplicateRoomAssemblyInstance"));
        Assert.That(result.Assembly, Is.Null);
    }

    [Test]
    public void MemberBudgetAppliesToTheWholeRoom()
    {
        var source = Assembly() with { Members = Enumerable.Range(0, 5).Select(index =>
            new KsProcgenResolvedAssemblyMember($"M{index}", $"M{index}", true, new()
                { Id = $"M{index}", Entity = "Table" }, KsProcgenMemberRotation.Independent, 0)).ToArray() };
        var result = KsProcgenRoomAssemblyComposer.Compose("Room", Enumerable.Range(0, 13)
            .Select(index => new KsProcgenRoomAssemblyInstance("Pack", "Core", index, source)).ToArray());
        Assert.That(result.Issue!.Code, Is.EqualTo("RoomAssemblyCompositionBudget"));
        Assert.That(result.Assembly, Is.Null);
    }

    [Test]
    public void InfrastructureAndAuthoredRolesWithTheSameNamesRemainSeparateInstances()
    {
        var assembly = Assembly(); var authored = new KsProcgenRoomAssemblyInstance("Electrical", "Apc", 0, assembly);
        var infrastructure = authored with { Origin = KsProcgenRoomMemberOrigin.Infrastructure };
        var result = KsProcgenRoomAssemblyComposer.Compose("Room", new[] { authored, infrastructure });
        Assert.That(result.Issue, Is.Null);
        Assert.That(result.Bindings.Count(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored), Is.EqualTo(2));
        Assert.That(result.Bindings.Count(binding => binding.Origin == KsProcgenRoomMemberOrigin.Infrastructure), Is.EqualTo(2));
        foreach (var relation in result.Assembly!.Relations)
            Assert.That(result.Bindings.Single(binding => binding.MemberId == relation.Subject).Origin,
                Is.EqualTo(result.Bindings.Single(binding => binding.MemberId == relation.Target).Origin));
        var replay = KsProcgenRoomAssemblyComposer.Compose("Room", new[] { infrastructure, authored });
        Assert.That(replay.Bindings, Is.EqualTo(result.Bindings));
        Assert.That(replay.Assembly!.VariantId, Is.EqualTo(result.Assembly.VariantId));
        var changed = KsProcgenRoomAssemblyComposer.Compose("Room", new[] { authored, infrastructure with { PackId = "Other", Origin = KsProcgenRoomMemberOrigin.Authored } });
        Assert.That(changed.Assembly!.VariantId, Is.Not.EqualTo(result.Assembly.VariantId));
    }

    private static KsProcgenRoomAssemblyInstance[] Instances(KsProcgenResolvedAssembly assembly) =>
        [new("Pack", "Workstation", 0, assembly), new("Pack", "Workstation", 1, assembly)];
    private static KsProcgenResolvedAssembly Assembly() => new("Workstation", "Base", "Table",
        new[]
        {
            new KsProcgenResolvedAssemblyMember("Table", "Table", true, new KsProcgenEntityEntry
                { Id = "Table", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired }, KsProcgenMemberRotation.Independent, 0),
            new KsProcgenResolvedAssemblyMember("Device", "Device", true, new KsProcgenEntityEntry
                { Id = "Device", Entity = "CoordinatesDisk", Movement = KsProcgenMovementClass.Clear }, KsProcgenMemberRotation.Independent, 0),
        }, new[] { new KsProcgenResolvedAssemblyRelation("Top", "Device", KsProcgenRelationKind.OnSurface,
            "Table", KsProcgenRelationSeverity.Required, 0, 1, null, null, KsProcgenApproachPolicy.EmptyFloor) });
}
