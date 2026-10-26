using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenUtilityDraft))]
public sealed class KsProcgenUtilityDraftTests : KsProcgenGameTest
{
    [Test]
    public async Task DraftRoundtripAndRevisionUseLoadedDependenciesWithoutRegisteringPrototypes()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var gasId = "KsProcgenStandardAir";
            var powerId = "KsProcgenAutomaticElectrical";
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, gasId, out var gas, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, powerId, out var power, out issue), Is.True, issue?.Code);
            var spec = new KsProcgenUtilityDraftSpec
            {
                DocumentId = "UtilityEditor", Revision = 8,
                Atmosphere = new() { ["LocalGas"] = gas!.CopySpec() },
                Electrical = new() { ["LocalPower"] = power!.CopySpec() },
            };
            var before = SEntMan.GetEntities().ToHashSet();
            var node = serialization.WriteValue(spec, alwaysWrite: true);
            var restored = serialization.Read<KsProcgenUtilityDraftSpec>(node, notNullableOverride: true);
            var draft = Snapshot(manager, restored);
            var copy = serialization.Read<KsProcgenUtilityDraftSpec>(serialization.WriteValue(draft.CopySpec(), alwaysWrite: true), notNullableOverride: true);
            Assert.That(Snapshot(manager, copy).ContentHash, Is.EqualTo(draft.ContentHash));
            Assert.That(draft.TryRevise(copy, id => manager.HasIndex<GasPrototype>(id), id => manager.HasIndex<EntityPrototype>(id),
                out var revised, out issue), Is.True, issue?.Code);
            Assert.That(revised!.Revision, Is.EqualTo(9));
            Assert.That(revised.ContentHash, Is.EqualTo(draft.ContentHash));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, "LocalGas", null, null,
                out var selectedGas, out issue, draft: revised), Is.True, issue?.Code);
            Assert.That(selectedGas, Is.SameAs(revised.Atmosphere.Single().Profile));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, "LocalPower", null, null,
                out var selectedPower, out issue, draft: revised), Is.True, issue?.Code);
            Assert.That(selectedPower, Is.SameAs(revised.Electrical.Single().Profile));
            var localGasId = "LocalGas";
            var localPowerId = "LocalPower";
            Assert.That(manager.HasIndex<KsProcgenAtmosphereProfilePrototype>(localGasId), Is.False);
            Assert.That(manager.HasIndex<KsProcgenElectricalProfilePrototype>(localPowerId), Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task InlineThenDraftThenLoadedThenInheritedPrecedenceIsExplicit()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var gasId = "KsProcgenStandardAir";
            var powerId = "KsProcgenAutomaticElectrical";
            var powerPrototype = manager.Index<KsProcgenElectricalProfilePrototype>(powerId);
            var spec = new KsProcgenUtilityDraftSpec
            {
                DocumentId = "Overlay",
                Atmosphere = new() { [gasId] = new() },
                Electrical = new() { [powerId] = new() { DemandMargin = 0.75f } },
            };
            var draft = Snapshot(manager, spec);
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, gasId, out var loadedGas, out var issue), Is.True, issue?.Code);
            Assert.That(loadedGas!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Fill));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, gasId, null, loadedGas,
                out var selectedGas, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(selectedGas!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Preserve));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, gasId, loadedGas.CopySpec(), selectedGas,
                out var inlineGas, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(inlineGas!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Fill));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, null, null, loadedGas,
                out var inherited, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(inherited, Is.SameAs(loadedGas));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, powerId, null, null,
                out var selectedPower, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(selectedPower!.DemandMargin, Is.EqualTo(0.75f));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, powerId, new() { DemandMargin = 0.5f }, selectedPower,
                out var inlinePower, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(inlinePower!.DemandMargin, Is.EqualTo(0.5f));
            var fallbackId = "KsProcgenVacuum";
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, fallbackId, out var fallback, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(fallback!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Vacuum));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, "Missing", out var missing, out issue, draft: draft), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("UnknownAtmosphereProfile"));
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, powerId, out var unchanged, out issue), Is.True, issue?.Code);
            Assert.That(unchanged!.DemandMargin, Is.EqualTo(powerPrototype.DemandMargin));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, null, null, unchanged,
                out var inheritedPower, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(inheritedPower, Is.SameAs(unchanged));
            var loadedPowerId = "KsProcgenWiredElectrical";
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, loadedPowerId, out var loadedPower, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(loadedPower!.SupplyRequirement, Is.EqualTo(KsProcgenElectricalSupply.WiredOnly));
            Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, "Missing", out var missingPower, out issue, draft: draft), Is.False);
            Assert.That(missingPower, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("UnknownElectricalProfile"));
        });
    }

    [Test]
    public async Task ResolvedInheritedThemeRetainsNoMutablePackReferencesOrGoals()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var parentId = "KsProcgenSimpleOffice";
            var childId = "KsProcgenNitrogenOfficeTheme";
            var parent = manager.Index<KsProcgenRoomThemePrototype>(parentId);
            var lists = new[] { parent.TilePacks!, parent.WallPacks!, parent.LightingPacks!, parent.EntityPacks! };
            var saved = lists.Select(list => list.ToArray()).ToArray();
            var weights = saved.Select(list => list.Select(reference => reference.Weight).ToArray()).ToArray();
            var goals = parent.Goals!;
            var density = goals.FurnishingDensity;
            var coherent = goals.CoherentContents;
            try
            {
                Assert.That(KsProcgenThemeValidator.TryResolve(manager, childId, out var before, out var issue), Is.True, issue?.Code);
                for (var index = 0; index < lists.Length; index++)
                {
                    lists[index][0].Weight += 3f;
                    lists[index].Clear();
                }
                goals.FurnishingDensity = 0.99f;
                goals.CoherentContents = !coherent;
                var frozen = new[] { before!.TilePacks, before.WallPacks, before.LightingPacks, before.EntityPacks };
                for (var index = 0; index < frozen.Length; index++)
                {
                    Assert.That(frozen[index].Count, Is.EqualTo(saved[index].Length));
                    Assert.That(frozen[index][0].Weight, Is.EqualTo(weights[index][0]));
                    Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenResolvedPackReference>) frozen[index]).Clear());
                    Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenResolvedPackReference>) frozen[index])[0] = new("Other", 1f, 0));
                }
                Assert.That(before.Goals.FurnishingDensity, Is.EqualTo(density));
                Assert.That(before.Goals.CoherentContents, Is.EqualTo(coherent));
                Assert.That(KsProcgenThemeValidator.TryResolve(manager, childId, out _, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("IncompleteTheme"));
            }
            finally
            {
                for (var index = 0; index < lists.Length; index++)
                {
                    lists[index].Clear();
                    lists[index].AddRange(saved[index]);
                    for (var item = 0; item < saved[index].Length; item++) saved[index][item].Weight = weights[index][item];
                }
                goals.FurnishingDensity = density;
                goals.CoherentContents = coherent;
            }
        });
    }

    [Test]
    public async Task ThemeUtilityDefaultsCanResolveAgainstDocumentAssets()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenNitrogenOfficeTheme";
            var spec = new KsProcgenUtilityDraftSpec
            {
                DocumentId = "ThemePreview",
                Atmosphere = new() { ["KsProcgenNitrogenRoom"] = new() },
            };
            var draft = Snapshot(manager, spec);
            Assert.That(KsProcgenThemeValidator.TryResolve(manager, themeId, out var local, out var issue, draft: draft), Is.True, issue?.Code);
            Assert.That(local!.Atmosphere!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Preserve));
            Assert.That(KsProcgenThemeValidator.TryResolve(manager, themeId, out var loaded, out issue), Is.True, issue?.Code);
            Assert.That(loaded!.Atmosphere!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Fill));
        });
    }

    private static KsProcgenUtilityDraft Snapshot(IPrototypeManager manager, KsProcgenUtilityDraftSpec spec)
    {
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, id => manager.HasIndex<GasPrototype>(id),
            id => manager.HasIndex<EntityPrototype>(id), out var draft, out var issue), Is.True, issue?.Code);
        return draft!;
    }
}
