using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenElectricalDistribution))]
public sealed class KsProcgenElectricalDistributionTests : KsProcgenGameTest
{
    [Test]
    public async Task LoadedSchemaTwoProfileResolvesRealClosetSuppliesAndRejectsLegacyPlanning()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, "KsProcgenClosetElectrical", out var profile, out var issue), Is.True, issue?.Code);
            Assert.That(profile!.SchemaVersion, Is.EqualTo(2));
            Assert.That(profile.Distribution!.FloorServiceFraction, Is.EqualTo(1f));
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, profile.Distribution.ClosetTheme, out var closet, out issue), Is.True, issue?.Code);
            Assert.That(closet!.Theme.Goals.CoherentContents, Is.True);
            Assert.That(closet.Theme.Goals.AllowLooseItems, Is.True);
            Assert.That(closet.EntityPacks.Single().Cores.Select(core => core.Id), Is.EquivalentTo(new[] { "HighVoltageCoil", "MediumVoltageCoil", "LowVoltageCoil" }));
            var planningSystem = SEntMan.System<KsProcgenPlanningSystem>();
            Assert.That(planningSystem.TryCaptureElectricalContext(closet, profile, out var context, out issue), Is.True, issue?.Code);
            Assert.That(context!.UtilityContent!.Theme.Id, Is.EqualTo(profile.Distribution.ClosetTheme));
            Assert.That(context.Profile.SchemaVersion, Is.EqualTo(2));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task EngineSerializationRetainsNestedPolicyInProfilesDraftsAndRequests()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, "KsProcgenClosetElectrical", out var profile, out var issue), Is.True, issue?.Code);
            var spec = profile!.CopySpec();
            spec.Distribution!.ClosetInteriorSizes = new() { new(2, 1) };
            spec.Distribution.FloorServiceFraction = 0.75f;
            spec.Distribution.LowVoltageSquares = KsProcgenElectricalSquarePolicy.ForbidNew;
            var restored = serializer.Read<KsProcgenElectricalSpec>(serializer.WriteValue(spec, alwaysWrite: true), notNullableOverride: true);
            Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var expected, out issue), Is.True, issue?.Code);
            Assert.That(KsProcgenElectricalProfiles.TrySnapshot(restored, out var actual, out issue), Is.True, issue?.Code);
            Assert.That(actual!.SemanticHash, Is.EqualTo(expected!.SemanticHash));
            var draftSpec = new KsProcgenUtilityDraftSpec { DocumentId = "Editor", Electrical = new() { ["Power"] = restored } };
            var draftCopy = serializer.Read<KsProcgenUtilityDraftSpec>(serializer.WriteValue(draftSpec, alwaysWrite: true), notNullableOverride: true);
            Assert.That(KsProcgenUtilityDraft.TrySnapshot(draftCopy, _ => true, id => manager.HasIndex<EntityPrototype>(id), out var draft, out issue), Is.True, issue?.Code);
            var request = new KsProcgenRequest { RequestId = "Room", Shape = new() { Cells = new() { new(0, 0) } }, ElectricalProfile = "Power" };
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out issue, draft: draft), Is.True, issue?.Code);
            var requestCopy = serializer.Read<KsProcgenRequest>(serializer.WriteValue(snapshot!.CopyRequest(), alwaysWrite: true), notNullableOverride: true);
            Assert.That(KsProcgenRequestSnapshot.TryCapture(requestCopy, out var roundtrip, out issue), Is.True, issue?.Code);
            Assert.That(roundtrip!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
            Assert.That(roundtrip.Electrical!.Distribution!.ClosetInteriorSizes.Single(), Is.EqualTo(new Robust.Shared.Maths.Vector2i(1, 2)));
            Assert.That(roundtrip.Electrical.Distribution.FloorServiceFraction, Is.EqualTo(0.75f));
            Assert.That(roundtrip.Electrical.Distribution.LowVoltageSquares, Is.EqualTo(KsProcgenElectricalSquarePolicy.ForbidNew));
            Assert.That(KsProcgenElectricalProfiles.TryUpgrade(restored, out var twice, out issue), Is.True, issue?.Code);
            Assert.That(KsProcgenElectricalProfiles.TrySnapshot(twice!, out var twiceProfile, out issue), Is.True, issue?.Code);
            Assert.That(twiceProfile!.SemanticHash, Is.EqualTo(actual.SemanticHash));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public async Task ProjectImportsClosetDependenciesFromEveryElectricalSelectionPath(int selection)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            var spec = Project(selection);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(KsProcgenProject.TryCapture(manager, spec, out var project, out var issue), Is.True, issue?.Code);
            var retained = project!.CopySpec();
            Assert.That(retained.Content!.Themes.ContainsKey("KsProcgenElectricalCloset"), Is.True);
            Assert.That(retained.Content.Entities.ContainsKey("KsProcgenElectricalSupplies"), Is.True);
            if (selection != 4)
            {
                Assert.That(retained.Content.Themes.ContainsKey("LocalCloset"), Is.True);
                Assert.That(retained.Content.Entities.ContainsKey("LocalSupplies"), Is.True);
            }
            spec.Content!.Themes.Clear(); spec.Utilities!.Electrical.Clear();
            var node = serializer.WriteValue(retained, alwaysWrite: true);
            var copy = serializer.Read<KsProcgenProjectSpec>(node, notNullableOverride: true);
            Assert.That(KsProcgenProject.TryCapture(manager, copy, out var recaptured, out issue), Is.True, issue?.Code);
            Assert.That(recaptured!.DeclarationHash, Is.EqualTo(project.DeclarationHash));
            retained.Content.Themes.Clear();
            Assert.That(project.CopySpec().Content!.Themes.ContainsKey("KsProcgenElectricalCloset"), Is.True);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public async Task InvalidClosetDependenciesLeaveNoPartialProject(int defect)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var spec = Project(0);
            switch (defect)
            {
                case 0: spec.Areas["Area"].Request.Electrical!.Distribution!.ClosetTheme = "MissingCloset"; break;
                case 1: spec.Content!.Themes["LocalCloset"].EntityPacks[0].Pack = "MissingPack"; break;
                case 2: spec.Content!.Entities["LocalSupplies"].Entries[0].Entity = "MissingElectricalItem"; break;
            }
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(KsProcgenProject.TryCapture(manager, spec, out var project, out var issue), Is.False);
            Assert.That(project, Is.Null); Assert.That(issue, Is.Not.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private static KsProcgenProjectSpec Project(int selection)
    {
        var power = new KsProcgenElectricalSpec { SchemaVersion = 2, Distribution = new() { ClosetTheme = "LocalCloset" } };
        var request = new KsProcgenRequest { RequestId = "Area", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } } };
        var theme = new KsProcgenRoomThemeSpec { Parent = "KsProcgenSimpleOffice" };
        var spec = new KsProcgenProjectSpec
        {
            DocumentId = "Editor",
            Areas = new() { ["Area"] = new() { Request = request, Theme = "LocalMain" } },
            Content = new()
            {
                DocumentId = "Editor",
                Themes = new()
                {
                    ["LocalMain"] = theme,
                    ["LocalCloset"] = new() { Parent = "KsProcgenElectricalCloset", EntityPacks = new() { new() { Pack = "LocalSupplies" } } },
                },
                Entities = new()
                {
                    ["LocalSupplies"] = new() { Entries = new() { new() { Id = "Coil", Entity = "CableHVStack10", Role = KsProcgenEntityRole.Consumable, Movement = KsProcgenMovementClass.Clear } } },
                },
            },
            Utilities = new() { DocumentId = "Editor" },
        };
        if (selection == 0) request.Electrical = power;
        else if (selection is 1 or 2 or 3)
        {
            spec.Utilities.Electrical.Add("LocalPower", power);
            if (selection == 1) theme.ElectricalProfile = "LocalPower";
            if (selection == 3) request.ElectricalProfile = "LocalPower";
        }
        else request.ElectricalProfile = "KsProcgenClosetElectrical";
        return spec;
    }
}
