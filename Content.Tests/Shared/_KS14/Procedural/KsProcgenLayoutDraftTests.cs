#nullable enable
using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed partial class KsProcgenLayoutDraftTests
{
    [Test]
    public void CompiledAlternativesRemainWholeDuringAutomaticFittingAndSourceEdits()
    {
        var source = Spec(); var draft = Capture(source);
        Assert.That(draft.TryLibrary("Rooms", out var library), Is.True);
        var family = library!.Families.Single();
        Assert.That(family.Options.Select(option => option.Rooms.Count), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        var request = new KsProcgenRequest
        {
            RequestId = "Fit", Seed = 7, Mode = KsProcgenMode.Prefabs,
            Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 4) } } },
        };
        var packed = KsProcgenPackingPlanner.Plan(request, library.Families);
        Assert.That(packed.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady), packed.Issue?.Code);
        Assert.That(packed.Placements.Count, Is.EqualTo(1));
        var chosen = packed.Placements.Single();
        var option = family.Options.Single(item => item.Id == chosen.OptionId);
        Assert.That(chosen.FamilyId, Is.EqualTo("Alternatives"));
        Assert.That(chosen.Claims.Count, Is.EqualTo(24));
        Assert.That(option.Rooms.Select(room => room.Id).Distinct().Count(), Is.EqualTo(option.Rooms.Count));
        var hash = draft.DeclarationHash;
        source.Rooms["Big"].Cells.Clear(); source.Families["Alternatives"].Options.Clear(); source.Libraries.Clear();
        Assert.That(Capture(draft.CopySpec()).DeclarationHash, Is.EqualTo(hash));
        Assert.That(library.Families.Single().Options.Count, Is.EqualTo(4));
        var copy = draft.CopySpec(); copy.Rooms["Big"].Cells.Clear();
        Assert.That(draft.CopySpec().Rooms["Big"].Cells.Count, Is.EqualTo(24));
    }

    [Test]
    public void LocalTransformsRotatePortsAndFacadesAndRetainSourceBindings()
    {
        var room = new KsProcgenRoomAssetSpec
        {
            SourceKey = "rooms/booth", ContentFingerprint = "expected-v1", Cells = Rectangle(3, 2),
            Ports = new() { new() { Id = "East", Threshold = new(2, 0), OutwardNormal = new(1, 0) } },
            ExteriorFacades = new() { new() { Cell = new(2, 0), OutwardNormal = new(1, 0) } },
        };
        var source = new KsProcgenLayoutDraftSpec
        {
            DocumentId = "Transforms", Rooms = new() { ["Booth"] = room },
            Families = new() { ["Rotated"] = new() { AllowedQuarterTurns = new() { 0 }, Options = new() { new()
                { Id = "Turn", ReservationCells = Rectangle(2, 3), Members = new() { new() { Id = "Instance", Room = "Booth", Origin = new(0, 2), QuarterTurns = 1 } } } } } },
            Libraries = new() { ["Library"] = new() { Families = new() { "Rotated" }, Rooms = new() { new() { Room = "Booth", AllowedQuarterTurns = new() { 3, 1 } } } } },
        };
        var draft = Capture(source);
        var rotated = draft.Libraries.Single().Families.Single(family => family.Id == "Rotated").Options.Single().Rooms.Single();
        Assert.That(rotated.Ports.Single().Threshold, Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(rotated.Ports.Single().OutwardNormal, Is.EqualTo(new Vector2i(0, -1)));
        Assert.That(rotated.ExteriorFacades.Single().OutwardNormal, Is.EqualTo(new Vector2i(0, -1)));
        Assert.That(draft.RoomBindings.Single(binding => binding.FamilyId == "Rotated").RoomAssetId, Is.EqualTo("Booth"));
        Assert.That(draft.Libraries.Single().Families.Single(family => family.Id == "Room/Booth").AllowedQuarterTurns, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(draft.RoomSources.Single().SourceKey, Is.EqualTo("rooms/booth"));
        Assert.That(draft.HasUnverifiedRoomSources, Is.True);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    [TestCase(15)] [TestCase(16)] [TestCase(17)] [TestCase(18)] [TestCase(19)]
    [TestCase(20)] [TestCase(21)] [TestCase(22)] [TestCase(23)]
    public void InvalidGeometryAndReferencesProduceNoPartialLibrary(int invalid)
    {
        var source = Spec();
        switch (invalid)
        {
            case 0: source.SchemaVersion = 3; break;
            case 1: source.Rooms = null!; break;
            case 2: source.Rooms["Big"] = null!; break;
            case 3: source.Rooms["Big"].Cells.Add(new(0, 0)); break;
            case 4: source.Rooms["Big"].Cells.Clear(); break;
            case 5: source.Rooms["Big"].SourceKey = "ExpectedSource"; break;
            case 6: source.Families["Alternatives"].Options[0].Members[0].Room = "Missing"; break;
            case 7: source.Families["Alternatives"].Options[0].Members[0].QuarterTurns = 4; break;
            case 8: source.Families["Alternatives"].Options[0].ReservationCells.RemoveAt(0); break;
            case 9: source.Families["Alternatives"].Options[0].Members.Add(new() { Id = "Overlap", Room = "Big" }); break;
            case 10: source.Families["Alternatives"].Options[0].Members.Clear(); break;
            case 11: source.Families["Alternatives"].AllowedQuarterTurns.Add(0); break;
            case 12: source.Libraries["Rooms"].Families.Add("Missing"); break;
            case 13: source.Libraries["Rooms"].Families.Add("Alternatives"); break;
            case 14: source.Libraries["Rooms"].Rooms.Add(new() { Room = "Missing" }); break;
            case 15: source.Rooms["Big"].Ports.Add(new() { Id = "Bad", Threshold = new(0, 0), OutwardNormal = new(1, 1) }); break;
            case 16: source.Rooms["Big"].ExteriorFacades.Add(new() { Cell = new(1, 1), OutwardNormal = new(1, 0) }); break;
            case 17: source.Families["Alternatives"].Options[0].Members[0].Origin = new(int.MaxValue, 0); break;
            case 18: source.Libraries["Rooms"].Families.Clear(); break;
            case 19: source.Families["Room/Collision"] = new(); break;
            case 20: source.Families["Alternatives"].Options[0].Id = source.Families["Alternatives"].Options[1].Id; break;
            case 21: source.Rooms["Big"].Ports = null!; break;
            case 22: source.Libraries["Rooms"].Rooms.Add(null!); break;
            case 23: source.Rooms["Big"].Cells[0] = new(1_000_001, 0); break;
        }
        Assert.That(KsProcgenLayoutDraft.TrySnapshot(source, out var draft, out var issue), Is.False);
        Assert.That(draft, Is.Null); Assert.That(issue, Is.Not.Null);
    }

    [Test]
    public void MasksAndTurnsAreCanonicalWhileSourceAndMemberDeclarationsAffectIdentity()
    {
        var source = Spec(); var hash = Capture(source).DeclarationHash;
        source.DocumentId = "OtherDocument"; source.Revision = 7;
        foreach (var room in source.Rooms.Values) room.Cells.Reverse();
        foreach (var option in source.Families["Alternatives"].Options) { option.ReservationCells.Reverse(); option.GenerateCells.Reverse(); }
        source.Families["Alternatives"].AllowedQuarterTurns.Reverse();
        source.Rooms = source.Rooms.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.That(Capture(source).DeclarationHash, Is.EqualTo(hash));
        source.Rooms["Big"].SourceKey = "ExpectedSource"; source.Rooms["Big"].ContentFingerprint = "expected-v1";
        Assert.That(Capture(source).DeclarationHash, Is.Not.EqualTo(hash));
    }

    [Test]
    public void CumulativeCellAndAssetBudgetsBoundImport()
    {
        var source = new KsProcgenLayoutDraftSpec { DocumentId = "CellBudget" };
        source.Rooms.Add("First", new() { Cells = Rectangle(256, 128) });
        source.Rooms.Add("Second", new() { Cells = Rectangle(256, 129) });
        Assert.That(KsProcgenLayoutDraft.TrySnapshot(source, out _, out _), Is.False);
        source.Rooms.Clear();
        for (var index = 0; index < 257; index++) source.Rooms.Add($"Room{index}", new() { Cells = new() { new(0, 0) } });
        Assert.That(KsProcgenLayoutDraft.TrySnapshot(source, out _, out _), Is.False);
    }

    private static KsProcgenLayoutDraft Capture(KsProcgenLayoutDraftSpec spec)
    { Assert.That(KsProcgenLayoutDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code); return draft!; }
    private static System.Collections.Generic.List<Vector2i> Rectangle(int width, int height) =>
        Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => new Vector2i(x, y))).ToList();
    private static KsProcgenLayoutDraftSpec Spec()
    {
        var full = Rectangle(6, 4);
        KsProcgenLayoutOptionSpec Option(string id, params KsProcgenLayoutMemberSpec[] members)
        {
            var used = members.SelectMany(member => (member.Room == "Big" ? full : member.Room == "Medium" ? Rectangle(3, 4) : Rectangle(2, 2))
                .Select(cell => cell + member.Origin)).ToHashSet();
            return new() { Id = id, ReservationCells = full.ToList(), GenerateCells = full.Where(cell => !used.Contains(cell)).ToList(), Members = members.ToList() };
        }
        return new()
        {
            DocumentId = "Layouts",
            Rooms = new() { ["Big"] = new() { Cells = full.ToList() }, ["Medium"] = new() { Cells = Rectangle(3, 4) }, ["Small"] = new() { Cells = Rectangle(2, 2) } },
            Families = new() { ["Alternatives"] = new() { AllowedQuarterTurns = new() { 0, 2 }, Options = new()
            {
                Option("Big", new KsProcgenLayoutMemberSpec { Id = "BigOnly", Room = "Big" }),
                Option("Medium", new() { Id = "Left", Room = "Medium" }, new() { Id = "Right", Room = "Medium", Origin = new(3, 0) }),
                Option("Three", new() { Id = "L1", Room = "Small" }, new() { Id = "L2", Room = "Small", Origin = new(2, 0) }, new() { Id = "L3", Room = "Small", Origin = new(0, 2) }),
                Option("Four", new() { Id = "F1", Room = "Small" }, new() { Id = "F2", Room = "Small", Origin = new(2, 0) }, new() { Id = "F3", Room = "Small", Origin = new(4, 0) }, new() { Id = "F4", Room = "Small", Origin = new(4, 2) }),
            } } },
            Libraries = new() { ["Rooms"] = new() { Families = new() { "Alternatives" } } },
        };
    }
}
