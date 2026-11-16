using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Systems;
using NUnit.Framework;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Locale keys the cultivation code builds at runtime ($"cultivation-realm-{realm}" and the like). A missing one
///         compiles and passes everything else, and shows up in game as a raw key.
/// </summary>
[TestFixture]
public sealed class CultivationLocaleTest : GameTest
{
    [Test]
    public async Task TestInterpolatedKeysExist()
    {
        await Server.WaitAssertion(() =>
        {
            var localization = Server.ResolveDependency<ILocalizationManager>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();

            using (Assert.EnterMultipleScope())
            {
                foreach (var realm in Enum.GetValues<CultivationRealm>())
                    Assert.That(localization.HasString($"cultivation-realm-{realm}"), Is.True, $"No name for the {realm} realm.");

                foreach (var group in SharedBodyCultivationSystem.AllGroups)
                    Assert.That(localization.HasString($"body-group-{group}"), Is.True, $"No name for the {group}.");

                for (var stage = 0; stage <= BodyCultivatorComponent.MaxStage; stage++)
                    Assert.That(localization.HasString($"body-stage-{stage}"), Is.True, $"No name for body stage {stage}.");

                for (var stage = 1; stage <= BodyCultivatorComponent.MaxStage; stage++)
                    Assert.That(localization.HasString($"body-stage-{stage}-benefits"), Is.True, $"No benefits text for body stage {stage}.");

                foreach (var stance in Enum.GetValues<BodyStance>())
                    Assert.That(localization.HasString($"body-stance-{stance}"), Is.True, $"No text for the {stance} stance.");

                foreach (var result in Enum.GetValues<MartialArtLearnResult>())
                {
                    if (result != MartialArtLearnResult.Learned)
                        Assert.That(localization.HasString($"martial-art-learn-{result}"), Is.True, $"No refusal text for {result}.");
                }

                foreach (var power in prototypes.EnumeratePrototypes<BodyTemperingPowerPrototype>())
                {
                    Assert.That(localization.HasString(power.Name), Is.True, $"{power.ID} has no name.");
                    Assert.That(localization.HasString(power.Description), Is.True, $"{power.ID} has no description.");
                }
            }
        });
    }
}
