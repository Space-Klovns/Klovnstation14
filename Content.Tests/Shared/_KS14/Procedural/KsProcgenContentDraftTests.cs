#nullable enable
using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenContentDraftTests
{
    [Test]
    public void NestedAuthoringBodiesAreDetachedAndCopiesCannotChangeRetainedRevision()
    {
        var source = Spec(); var draft = Capture(source); var hash = draft.DeclarationHash;
        source.Themes["Office"].Goals!.FurnishingDensity = 0.9f;
        source.Tiles["Tiles"].Palettes[0].PrimaryTile = "Changed";
        source.Walls["Walls"].Families[0].Door = "Changed";
        source.Lights["Lights"].Fixtures[0].PreferredSpacing = 99;
        source.Entities["Contents"].Assemblies[0].Bindings.Clear();
        source.Assemblies["Desk"].Members[0].Footprint.Clear();
        source.Assemblies["Desk"].Relations[0].Target = "Changed";
        source.Assemblies["Desk"].Variants[0].Members[0].AllowedQuarterTurns.Clear();
        var copy = draft.CopySpec();
        Assert.That(copy.Themes["Office"].Goals!.FurnishingDensity, Is.EqualTo(0.2f));
        Assert.That(copy.Tiles["Tiles"].Palettes[0].PrimaryTile, Is.EqualTo("FloorSteel"));
        Assert.That(copy.Walls["Walls"].Families[0].Door, Is.EqualTo("Airlock"));
        Assert.That(copy.Entities["Contents"].Assemblies[0].Bindings["Device"], Is.EqualTo("ComputerAlert"));
        Assert.That(copy.Assemblies["Desk"].Members[0].Footprint.Count, Is.EqualTo(1));
        Assert.That(copy.Assemblies["Desk"].Relations[0].Target, Is.EqualTo("Table"));
        Assert.That(copy.Assemblies["Desk"].Variants[0].Members[0].AllowedQuarterTurns.Count, Is.EqualTo(4));
        copy.Themes.Clear(); copy.Assemblies["Desk"].Variants.Clear(); copy.Entities["Contents"].Entries[0].Footprint.Clear();
        Assert.That(Capture(draft.CopySpec()).DeclarationHash, Is.EqualTo(hash));
        Assert.That(draft.AssetCount, Is.EqualTo(6));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    [TestCase(15)] [TestCase(16)] [TestCase(17)] [TestCase(18)] [TestCase(19)]
    [TestCase(20)] [TestCase(21)] [TestCase(22)] [TestCase(23)]
    public void InvalidStructuresNeverProducePartialDrafts(int change)
    {
        var source = Spec();
        switch (change)
        {
            case 0: source.SchemaVersion = 2; break;
            case 1: source.DocumentId = "has spaces"; break;
            case 2: source.Revision = -1; break;
            case 3: source.Themes = null!; break;
            case 4: source.Tiles["Tiles"] = null!; break;
            case 5: source.Themes["Office"].Tags = null!; break;
            case 6: source.Walls["Walls"].Families[0] = null!; break;
            case 7: source.Lights["Lights"].Fixtures[0].Entity = null!; break;
            case 8: source.Entities["Contents"].Entries[0].Weight = float.NaN; break;
            case 9: source.Assemblies["Desk"].Variants = null!; break;
            case 10: source.Assemblies["Desk"].Members[0].Footprint = Enumerable.Repeat(new Robust.Shared.Maths.Vector2i(0, 0), 17).ToList(); break;
            case 11: source.Assemblies["Desk"].Members[0].AllowedQuarterTurns.Add(0); break;
            case 12: source.Assemblies["Desk"].Relations[0].Kind = (KsProcgenRelationKind) 250; break;
            case 13: source.Entities["Contents"].Assemblies[0].Bindings = null!; break;
            case 14: source.Tiles["Tiles"].Tags.Add(new string('x', 513)); break;
            case 15: source.Tiles = Enumerable.Range(0, 129).ToDictionary(index => $"Asset{index}", _ => new KsProcgenTilePackSpec()); break;
            case 16: source.Assemblies["Desk"].Members[0] = null!; break;
            case 17: source.Assemblies["Desk"].Variants[0] = null!; break;
            case 18: source.Entities["Contents"].Entries[0].Role = (KsProcgenEntityRole) 250; break;
            case 19: source.Themes["Office"].Goals!.LightingCoverage = float.PositiveInfinity; break;
            case 20: source.Themes["Office"].TilePacks!.Add(null!); break;
            case 21: source.Entities["Contents"].Entries = Enumerable.Repeat(new KsProcgenEntityEntry(), 65).ToList(); break;
            case 22: source.Assemblies["Desk"].Relations = Enumerable.Repeat(new KsProcgenAssemblyRelation(), 257).ToList(); break;
            case 23: source.Themes.Add("Invalid Name", new()); break;
        }
        Assert.That(KsProcgenContentDraft.TrySnapshot(source, out var draft, out var issue), Is.False);
        Assert.That(draft, Is.Null);
        Assert.That(issue, Is.Not.Null);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    public void DraftIdentityIncludesUnselectedAssetsAndOrderedVariants(int change)
    {
        var source = Spec(); var hash = Capture(source).DeclarationHash;
        switch (change)
        {
            case 0: source.Themes["Office"].Parent = "Other"; break;
            case 1: source.Themes["Office"].FallbackTheme = "Other"; break;
            case 2: source.Themes["Office"].Tags.Add("NewTag"); break;
            case 3: source.Themes["Office"].Goals!.KeepCenterOpen = false; break;
            case 4: source.Tiles["Tiles"].Palettes[0].AccentFraction = 0.1f; break;
            case 5: source.Walls["Walls"].Families[0].HullWall = null; break;
            case 6: source.Lights["Lights"].Fixtures[0].Supply = KsProcgenLightingSupply.CallerSupplied; break;
            case 7: source.Entities["Contents"].ActivityTags.Add("NewActivity"); break;
            case 8: source.Entities["Contents"].Entries[0].RequiresInteractionApproach = true; break;
            case 9: source.Entities["Contents"].Assemblies[0].Bindings["Device"] = "OtherComputer"; break;
            case 10: source.Assemblies["Desk"].Relations[0].ApproachPolicy = KsProcgenApproachPolicy.EmptyOrAssociatedSeat; break;
            case 11: source.Assemblies["Desk"].Members[0].ApproachLanding = new(1, 0); break;
            case 12: source.Assemblies["Desk"].Variants[0].AnchorMember = "Other"; break;
            case 13: source.Themes["Office"].AtmosphereProfile = "Air"; break;
            case 14: source.Assemblies["Desk"].Variants.Add(new() { Id = "Another", AnchorMember = "Chair", Members = new() { new() { Id = "Chair", Entity = "ChairOfficeLight" } } }); break;
        }
        Assert.That(Capture(source).DeclarationHash, Is.Not.EqualTo(hash));
    }

    [Test]
    public void DocumentMetadataAndDictionaryInsertionOrderAreNotContentIdentity()
    {
        var source = Spec(); source.Tiles.Add("Other", new()); var hash = Capture(source).DeclarationHash;
        source.DocumentId = "OtherDocument"; source.Revision = 90;
        source.Tiles = source.Tiles.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.That(Capture(source).DeclarationHash, Is.EqualTo(hash));
        source.Assemblies["Desk"].Variants.Add(new() { Id = "Second" });
        var ordered = Capture(source).DeclarationHash; source.Assemblies["Desk"].Variants.Reverse();
        Assert.That(Capture(source).DeclarationHash, Is.Not.EqualTo(ordered));
    }

    [Test]
    public void RevisionReplacementIsAtomicAndDoesNotPretendToValidateReferences()
    {
        var source = Spec(); var original = Capture(source);
        source.Themes["Office"].Parent = "NotLoadedYet";
        Assert.That(original.TryRevise(source, out var revised, out var issue), Is.True, issue?.Code);
        Assert.That(revised!.Revision, Is.EqualTo(4)); Assert.That(original.Revision, Is.EqualTo(3));
        Assert.That(revised.CopySpec().Themes["Office"].Parent, Is.EqualTo("NotLoadedYet"));
        Assert.That(revised.TryRevise(source, out var rejected, out issue), Is.False);
        Assert.That(rejected, Is.Null); Assert.That(issue!.Code, Is.EqualTo("ContentDraftRevisionConflict"));
        source.Revision = 4; source.DocumentId = "Foreign";
        Assert.That(revised.TryRevise(source, out _, out issue), Is.False);
        source.DocumentId = revised.DocumentId; source.Lights["Lights"].Fixtures = null!;
        Assert.That(revised.TryRevise(source, out _, out issue), Is.False);
        Assert.That(revised.Revision, Is.EqualTo(4));
    }

    [TestCase(0)] [TestCase(1)]
    public void CumulativeBudgetsApplyAcrossIndividuallyBoundedAssets(int budget)
    {
        var source = new KsProcgenContentDraftSpec { DocumentId = "Budget" };
        for (var index = 0; index < 128; index++)
        {
            source.Themes.Add($"Theme{index}", new()
            {
                Tags = budget == 0 ? Enumerable.Repeat(new string('x', 512), 64).ToList() : new(),
                TilePacks = budget == 1 ? Enumerable.Range(0, 64).Select(i => new KsProcgenWeightedPackReference { Pack = $"Tile{i}" }).ToList() : null,
                WallPacks = budget == 1 ? Enumerable.Range(0, 64).Select(i => new KsProcgenWeightedPackReference { Pack = $"Wall{i}" }).ToList() : null,
                LightingPacks = budget == 1 ? Enumerable.Range(0, 64).Select(i => new KsProcgenWeightedPackReference { Pack = $"Light{i}" }).ToList() : null,
                EntityPacks = budget == 1 ? Enumerable.Range(0, 64).Select(i => new KsProcgenWeightedPackReference { Pack = $"Entity{i}" }).ToList() : null,
            });
        }
        Assert.That(KsProcgenContentDraft.TrySnapshot(source, out var draft, out _), Is.False);
        Assert.That(draft, Is.Null);
    }

    private static KsProcgenContentDraft Capture(KsProcgenContentDraftSpec spec)
    { Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code); return draft!; }

    [Test]
    public void NamesAreScopedByKindButTheAssetBudgetIsShared()
    {
        var source = new KsProcgenContentDraftSpec { DocumentId = "SharedBudget", Themes = new() { ["SameName"] = new() }, Tiles = new() { ["SameName"] = new() } };
        Assert.That(Capture(source).AssetCount, Is.EqualTo(2));
        source.Themes = Enumerable.Range(0, 64).ToDictionary(index => $"Theme{index}", _ => new KsProcgenRoomThemeSpec());
        source.Tiles = Enumerable.Range(0, 65).ToDictionary(index => $"Tile{index}", _ => new KsProcgenTilePackSpec());
        Assert.That(KsProcgenContentDraft.TrySnapshot(source, out var draft, out var issue), Is.False);
        Assert.That(draft, Is.Null); Assert.That(issue!.Code, Is.EqualTo("InvalidContentDraftStructure"));
    }

    private static KsProcgenContentDraftSpec Spec() => new()
    {
        DocumentId = "Interiors", Revision = 3,
        Themes = new() { ["Office"] = new() { Parent = "KsProcgenSimpleOffice", TilePacks = new() { new() { Pack = "Tiles" } }, Goals = new() } },
        Tiles = new() { ["Tiles"] = new() { Palettes = new() { new() { Id = "Steel", PrimaryTile = "FloorSteel" } } } },
        Walls = new() { ["Walls"] = new() { Families = new() { new() { Id = "Steel", InteriorWall = "WallSolid", HullWall = "WallReinforced", Door = "Airlock" } } } },
        Lights = new() { ["Lights"] = new() { Fixtures = new() { new() { Id = "Post", Entity = "LightPostSmall" } } } },
        Entities = new() { ["Contents"] = new() { Entries = new() { new() { Id = "Chair", Entity = "ChairOfficeLight" } }, Assemblies = new() { new() { Id = "Desk", Assembly = "Desk", Bindings = new() { ["Device"] = "ComputerAlert" } } } } },
        Assemblies = new() { ["Desk"] = new()
        {
            AnchorMember = "Table", Members = new() { new() { Id = "Table", Entity = "Table" }, new() { Id = "Computer", Binding = "Device" } },
            Relations = new() { new() { Id = "OnDesk", Subject = "Computer", Target = "Table", Kind = KsProcgenRelationKind.OnSurface } },
            Variants = new() { new() { Id = "Small", AnchorMember = "Chair", Members = new() { new() { Id = "Chair", Entity = "ChairOfficeLight" } } } },
        } },
    };
}
