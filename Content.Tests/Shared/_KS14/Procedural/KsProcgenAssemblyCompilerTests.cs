using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAssemblyCompilerTests
{
    [Test]
    public void VariantsReplaceTheWholeCoreAndKeepAuthoredPriority()
    {
        var standing = new KsProcgenAssemblyVariant
        {
            Id = "ZStanding", AnchorMember = "Console",
            Members = [new() { Id = "Console", Binding = "StandingDevice", RequiresInteractionApproach = true }],
            Relations = [new() { Id = "Open", Subject = "Console", Kind = KsProcgenRelationKind.FacingOpenSpace }],
        };
        var last = Workstation();
        last.Id = "ALast";
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariants("Workstation", [Workstation(), standing, last],
            new Dictionary<string, string> { ["StandingDevice"] = "Computer" }, Exists,
            out var variants, out var issue), Is.True, issue?.Code);
        Assert.That(variants.Select(variant => variant.VariantId), Is.EqualTo(new[] { "Base", "ZStanding", "ALast" }));
        Assert.That(variants[1].Members.Select(member => member.Id), Is.EqualTo(new[] { "Console" }));
        Assert.That(variants[1].Relations.Select(relation => relation.Id), Is.EqualTo(new[] { "Open" }));
        Assert.That(variants[1].Members.Single().Entry.Entity, Is.EqualTo("Computer"));
        standing.Members.Clear();
        standing.Relations.Clear();
        Assert.That(variants[1].Members.Count, Is.EqualTo(1));
        Assert.That(variants[1].Relations.Count, Is.EqualTo(1));
    }

    [TestCase("UnknownEntity", "InvalidAssemblyEntity")]
    [TestCase("MissingBinding", "MissingAssemblyBinding")]
    [TestCase("DuplicateVariant", "DuplicateAssemblyVariant")]
    [TestCase("UnknownBinding", "UnknownAssemblyBinding")]
    public void InvalidAlternativeReturnsNoCompiledFamily(string mutation, string expectedCode)
    {
        var alternative = Workstation();
        alternative.Id = "Alternative";
        var bindings = new Dictionary<string, string>();
        switch (mutation)
        {
            case "UnknownEntity": alternative.Members[0].Entity = "Unknown"; break;
            case "MissingBinding": alternative.Members[0].Entity = null; alternative.Members[0].Binding = "Missing"; break;
            case "DuplicateVariant": alternative.Id = "Base"; break;
            case "UnknownBinding": bindings.Add("Typo", "Computer"); break;
        }
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariants("Workstation", [Workstation(), alternative],
            bindings, Exists, out var variants, out var issue), Is.False);
        Assert.That(variants, Is.Empty, "A valid base must not survive a malformed alternative as a partial family.");
        Assert.That(issue?.Code, Is.EqualTo(expectedCode));
    }

    [TestCase("Variants", "AssemblyVariantBudget")]
    [TestCase("LateMembers", "InvalidAssemblyDefinition")]
    [TestCase("LateRelations", "InvalidAssemblyDefinition")]
    [TestCase("LateBinding", "InvalidAssemblyMember")]
    public void FamilyBoundsAreCheckedBeforeInspectingAnyPrototype(string mutation, string expectedCode)
    {
        var definitions = new List<KsProcgenAssemblyVariant> { Workstation() };
        var alternative = Workstation();
        alternative.Id = "Alternative";
        definitions.Add(alternative);
        switch (mutation)
        {
            case "Variants":
                while (definitions.Count <= KsProcgenAssemblyCompiler.MaximumVariants)
                    definitions.Add(Workstation());
                break;
            case "LateMembers":
                alternative.Members = Enumerable.Range(0, 65)
                    .Select(index => new KsProcgenAssemblyMember { Id = $"Member{index}", Entity = "Table" }).ToList();
                break;
            case "LateRelations":
                alternative.Relations = Enumerable.Range(0, 257)
                    .Select(index => new KsProcgenAssemblyRelation { Id = $"Relation{index}" }).ToList();
                break;
            case "LateBinding":
                alternative.Members[0].Entity = null;
                alternative.Members[0].Binding = new string('x', KsProcgenAssemblyCompiler.MaximumNameLength + 1);
                break;
        }
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariants("Workstation", definitions,
            new Dictionary<string, string>(),
            _ => throw new System.InvalidOperationException("No earlier variant may inspect prototypes before family preflight."),
            out var variants, out var issue), Is.False);
        Assert.That(variants, Is.Empty);
        Assert.That(issue?.Code, Is.EqualTo(expectedCode));
    }

    [Test]
    public void OversizedBindingsFailBeforePrototypeInspection()
    {
        var bindings = Enumerable.Range(0, KsProcgenAssemblyCompiler.MaximumBindings + 1)
            .ToDictionary(index => $"Binding{index}", _ => "Computer");
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Workstation", Workstation(), bindings,
            _ => throw new System.InvalidOperationException("Oversized input must not inspect prototypes."),
            out var resolved, out var issue), Is.False);
        Assert.That(resolved, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAssemblyDefinition"));
    }

    [TestCase("Member", "InvalidAssemblyMember")]
    [TestCase("Relation", "InvalidAssemblyRelation")]
    [TestCase("Variant", "InvalidAssemblyDefinition")]
    [TestCase("Binding", "InvalidAssemblyMember")]
    public void NamesHaveExplicitBounds(string kind, string expectedCode)
    {
        var definition = Workstation();
        var oversized = new string('x', KsProcgenAssemblyCompiler.MaximumNameLength + 1);
        switch (kind)
        {
            case "Member": definition.Members[1].Id = oversized; break;
            case "Relation": definition.Relations[0].Id = oversized; break;
            case "Variant": definition.Id = oversized; break;
            case "Binding": definition.Members[2].Entity = null; definition.Members[2].Binding = oversized; break;
        }
        Assert.That(Compile(definition, out var resolved, out var issue), Is.False);
        Assert.That(resolved, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo(expectedCode));
    }

    [Test]
    public void MaximumLengthMemberNameStillAllowsExplicitRepeatedInstanceTargets()
    {
        var definition = Workstation();
        var memberName = new string('x', KsProcgenAssemblyCompiler.MaximumNameLength);
        definition.Members[1].Id = memberName;
        definition.Members[1].MaximumCount = 2;
        definition.Relations[0].Subject = memberName;
        definition.Relations[1].Target = memberName + "/0";
        Assert.That(Compile(definition, out var resolved, out var issue), Is.True, issue?.Code);
        Assert.That(resolved!.Relations.Single(relation => relation.Kind == KsProcgenRelationKind.UsesSeat).Target,
            Is.EqualTo(memberName + "/0"));
    }

    [Test]
    public void EquivalentDeclarationsCompileCanonicallyAndDetachFromTheirSources()
    {
        var first = Workstation();
        first.Members[0].Footprint = [new(0, 0), new(1, 0)];
        var reordered = Workstation();
        reordered.Members[0].Footprint = [new(1, 0), new(0, 0)];
        reordered.Members.ForEach(member => member.AllowedQuarterTurns.Reverse());
        reordered.Members.Reverse();
        reordered.Relations.Reverse();
        Assert.That(Compile(first, out var expected, out _), Is.True);
        Assert.That(Compile(reordered, out var actual, out _), Is.True);
        Assert.That(actual!.Relations, Is.EqualTo(expected!.Relations));
        Assert.That(actual.Members.Select(member => member.Id), Is.EqualTo(expected.Members.Select(member => member.Id)));
        foreach (var member in actual.Members)
        {
            var other = expected.Members.Single(candidate => candidate.Id == member.Id);
            Assert.That(member.Entry.Footprint, Is.EqualTo(other.Entry.Footprint));
            Assert.That(member.Entry.AllowedQuarterTurns, Is.EqualTo(other.Entry.AllowedQuarterTurns));
        }
        reordered.Members.ForEach(member => { member.Footprint.Clear(); member.AllowedQuarterTurns.Clear(); });
        reordered.Relations[0].Subject = "Changed";
        Assert.That(actual.Members.Single(member => member.Id == "Table").Entry.Footprint.Count, Is.EqualTo(2));
        Assert.That(actual.Members.All(member => member.Entry.AllowedQuarterTurns.Count == 4), Is.True);
        Assert.That(actual.Relations, Is.EqualTo(expected.Relations));
    }

    [TestCase("SlotOnSpatialRelation")]
    [TestCase("ContainerOnSpatialRelation")]
    [TestCase("BlankSurfaceSlot")]
    [TestCase("LongContainerId")]
    [TestCase("ApproachOnSpatialRelation")]
    [TestCase("DistanceOnFacingRelation")]
    [TestCase("ContainerOnSurface")]
    [TestCase("SlotOnContainer")]
    [TestCase("BlankContainerId")]
    public void FieldsWithoutImplementedSemanticsCannotBeIgnored(string mutation)
    {
        var definition = Workstation();
        var relation = definition.Relations[0];
        switch (mutation)
        {
            case "SlotOnSpatialRelation": relation.Slot = "Top"; break;
            case "ContainerOnSpatialRelation": relation.ContainerId = "Storage"; break;
            case "BlankSurfaceSlot": relation.Kind = KsProcgenRelationKind.OnSurface; relation.Slot = " "; break;
            case "LongContainerId": relation.Kind = KsProcgenRelationKind.InContainer; relation.ContainerId = new string('x', 257); break;
            case "ApproachOnSpatialRelation": relation.ApproachPolicy = KsProcgenApproachPolicy.EmptyOrAssociatedSeat; break;
            case "DistanceOnFacingRelation": relation.MaximumDistance = 10; break;
            case "ContainerOnSurface": relation.Kind = KsProcgenRelationKind.OnSurface; relation.ContainerId = "Storage"; break;
            case "SlotOnContainer": relation.Kind = KsProcgenRelationKind.InContainer; relation.ContainerId = "Storage"; relation.Slot = "Top"; break;
            case "BlankContainerId": relation.Kind = KsProcgenRelationKind.InContainer; relation.ContainerId = " "; break;
        }
        Assert.That(Compile(definition, out var resolved, out var issue), Is.False);
        Assert.That(resolved, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("InapplicableAssemblyRelationField"));
    }

    [Test]
    public void SpatialRelationsCanShareTargetsWithoutCreatingSupportCycles()
    {
        var definition = Workstation();
        definition.Relations.Add(new KsProcgenAssemblyRelation
        {
            Id = "SeatNearTable", Subject = "Seat", Target = "Table", Kind = KsProcgenRelationKind.AdjacentTo,
        });
        definition.Relations.Add(new KsProcgenAssemblyRelation
        {
            Id = "TableFacesSeat", Subject = "Table", Target = "Seat", Kind = KsProcgenRelationKind.FacingTarget,
        });
        Assert.That(Compile(definition, out var resolved, out var issue), Is.True, issue?.Code);
        Assert.That(resolved!.Members.Count, Is.EqualTo(3));
        Assert.That(resolved.Relations.Count, Is.EqualTo(4));
    }

    [Test]
    public void RepeatedSubjectExpandsAndKeepsMandatoryCounts()
    {
        var definition = Workstation();
        definition.Members[1].MaximumCount = 2;
        definition.Relations.RemoveAt(1);
        Assert.That(Compile(definition, out var resolved, out var issue), Is.True, issue?.Code);
        Assert.That(resolved!.Members.Where(member => member.SourceMemberId == "Seat")
            .Select(member => (member.Id, member.Required)),
            Is.EqualTo(new[] { ("Seat/0", true), ("Seat/1", false) }));
        Assert.That(resolved.Relations.Select(relation => relation.Subject),
            Is.EquivalentTo(new[] { "Seat/0", "Seat/1" }));
        definition.Relations.Add(new KsProcgenAssemblyRelation
        {
            Id = "AmbiguousSeat", Subject = "Console", Target = "Seat", Kind = KsProcgenRelationKind.UsesSeat,
        });
        Assert.That(Compile(definition, out resolved, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAssemblyRelationTarget"));
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void SupportCycleFailsWithoutAResolvedCore()
    {
        var definition = Workstation();
        definition.Relations =
        [
            new() { Id = "A", Subject = "Console", Target = "Table", Kind = KsProcgenRelationKind.OnSurface },
            new() { Id = "B", Subject = "Table", Target = "Console", Kind = KsProcgenRelationKind.OnSurface },
        ];
        Assert.That(Compile(definition, out var resolved, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("AssemblySupportCycle"));
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void RequiredMachineCannotDependOnOptionalSeat()
    {
        var definition = Workstation();
        definition.Members[1].MinimumCount = 0;
        Assert.That(Compile(definition, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("AssemblyMandatoryDependencyUnmet"));
    }

    [Test]
    public void BindingsResolveToInspectableEntities()
    {
        var definition = Workstation();
        definition.Members[2].Entity = null;
        definition.Members[2].Binding = "Device";
        Assert.That(Compile(definition, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("MissingAssemblyBinding"));
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Workstation", definition,
            new Dictionary<string, string> { ["Device"] = "Computer" },
            Exists, out var resolved, out issue), Is.True, issue?.Code);
        Assert.That(resolved!.Members.Single(member => member.Id == "Console").Entry.Entity,
            Is.EqualTo("Computer"));
    }

    [Test]
    public void OversizedAssemblyFailsBeforeExpansion()
    {
        var definition = Workstation();
        definition.Members[1].MaximumCount = 64;
        Assert.That(Compile(definition, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAssemblyMember"));
    }

    [Test]
    public void MultipleSupportParentsAreRejected()
    {
        var definition = Workstation();
        definition.Relations =
        [
            new() { Id = "A", Subject = "Console", Target = "Table", Kind = KsProcgenRelationKind.OnSurface },
            new() { Id = "B", Subject = "Console", Target = "Seat", Kind = KsProcgenRelationKind.OnSurface },
        ];
        Assert.That(Compile(definition, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("AssemblySupportConflict"));
    }

    [TestCase("MissingEntity", "InvalidAssemblyEntity")]
    [TestCase("DuplicateMember", "InvalidAssemblyMember")]
    [TestCase("MissingAnchor", "InvalidAssemblyAnchor")]
    [TestCase("UnaryTarget", "InvalidAssemblyRelationTarget")]
    [TestCase("ContainerWithoutId", "AssemblySupportConflict")]
    public void MalformedCoreFailsWithSpecificReason(string mutation, string expectedCode)
    {
        var definition = Workstation();
        switch (mutation)
        {
            case "MissingEntity":
                definition.Members[0].Entity = "Unknown";
                break;
            case "DuplicateMember":
                definition.Members[1].Id = "Table";
                break;
            case "MissingAnchor":
                definition.AnchorMember = "Unknown";
                break;
            case "UnaryTarget":
                definition.Relations[0].Kind = KsProcgenRelationKind.AtCorner;
                break;
            case "ContainerWithoutId":
                definition.Relations[0].Kind = KsProcgenRelationKind.InContainer;
                break;
        }
        Assert.That(Compile(definition, out var resolved, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo(expectedCode));
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void SingletonCopiesEntryWithoutInventingRelations()
    {
        var entry = new KsProcgenEntityEntry { Id = "Table", Entity = "Table" };
        var compiled = KsProcgenAssemblyCompiler.CompileSingleton(entry);
        entry.Footprint.Clear();
        Assert.That(compiled.Members.Single().Entry.Footprint.Count, Is.EqualTo(1));
        Assert.That(compiled.Members.Single().Required, Is.True);
        Assert.That(compiled.Relations, Is.Empty);
    }

    private static KsProcgenAssemblyVariant Workstation() => new()
    {
        Id = "Base", AnchorMember = "Table",
        Members =
        [
            new() { Id = "Table", Entity = "Table", Role = KsProcgenEntityRole.PrimaryFurniture },
            new() { Id = "Seat", Entity = "Chair", Role = KsProcgenEntityRole.Seat },
            new() { Id = "Console", Entity = "Computer", Role = KsProcgenEntityRole.Equipment,
                RequiresInteractionApproach = true },
        ],
        Relations =
        [
            new() { Id = "SeatFace", Subject = "Seat", Target = "Table", Kind = KsProcgenRelationKind.FacingTarget },
            new() { Id = "ConsoleSeat", Subject = "Console", Target = "Seat", Kind = KsProcgenRelationKind.UsesSeat },
        ],
    };

    private static bool Compile(KsProcgenAssemblyVariant definition,
        out KsProcgenResolvedAssembly? resolved, out KsProcgenIssue? issue) =>
        KsProcgenAssemblyCompiler.TryCompileVariant("Workstation", definition,
            new Dictionary<string, string>(), Exists, out resolved, out issue);

    private static bool Exists(string id) => id is "Table" or "Chair" or "Computer";
}
