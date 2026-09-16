using Content.Client._KS14.Actions;
using NUnit.Framework;

namespace Content.Tests.Client._KS14.Actions;

[TestFixture]
public sealed class KsActionBarConfigurationJsonTest
{
    [Test]
    public void RoundTripPreservesFolderLayout()
    {
        var configuration = new KsActionBarConfiguration
        {
            Entries =
            [
                new KsActionBarConfigurationEntry
                {
                    Action = Identity("ActionStand", null, 0),
                },
                new KsActionBarConfigurationEntry
                {
                    Folder =
                    [
                        Identity("ActionToggleLight", "HandheldPDA", 0),
                        Identity("ActionOpenStorage", "Backpack", 0),
                    ],
                },
            ],
            KnownActions =
            [
                Identity("ActionStand", null, 0),
                Identity("ActionToggleLight", "HandheldPDA", 0),
                Identity("ActionOpenStorage", "Backpack", 0),
                Identity("ActionIntentionallyRemoved", null, 0),
            ],
        };

        var json = KsActionBarConfigurationJson.Serialize(configuration);

        Assert.That(json.TrimStart(), Does.StartWith("{"));
        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Version, Is.EqualTo(KsActionBarConfiguration.CurrentVersion));
            Assert.That(restored.Entries, Has.Count.EqualTo(2));
            Assert.That(restored.Entries[0].Action?.ActionPrototype, Is.EqualTo("ActionStand"));
            Assert.That(restored.Entries[0].Action?.ProviderPrototype, Is.Null);
            Assert.That(restored.Entries[1].Folder, Has.Count.EqualTo(2));
            Assert.That(restored.Entries[1].Folder?[0].ProviderPrototype, Is.EqualTo("HandheldPDA"));
            Assert.That(restored.Entries[1].Folder?[1].ActionPrototype, Is.EqualTo("ActionOpenStorage"));
            Assert.That(restored.KnownActions, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void InvalidJsonFailsWithoutThrowingFromSchemaValidation()
    {
        const string json = "{ \"version\": 1, \"entries\": [] }";

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out _), Is.False);
    }

    [Test]
    public void IntrinsicIdentityDoesNotMatchProvidedIdentity()
    {
        var saved = Identity("ActionScream", null, 0);
        var current = Identity("ActionScream", "ClothingMaskClown", 0);

        Assert.That(KsActionBarIdentity.MatchesSaved(saved, current), Is.False);
    }

    [Test]
    public void BlankProviderMatchesIntrinsicIdentity()
    {
        var saved = Identity("ActionScream", string.Empty, 0);
        var current = Identity("ActionScream", null, 0);

        Assert.That(KsActionBarIdentity.MatchesSaved(saved, current), Is.True);
    }

    [Test]
    public void ProvidedIdentityRequiresTheSameProvider()
    {
        var saved = Identity("ActionToggleLight", "CaptainPDA", 0);
        var current = Identity("ActionToggleLight", "PassengerPDA", 0);

        Assert.That(KsActionBarIdentity.MatchesSaved(saved, current), Is.False);
    }
    private static KsSavedActionIdentity Identity(string action, string provider, int occurrence)
    {
        return new KsSavedActionIdentity
        {
            ActionPrototype = action,
            ProviderPrototype = provider,
            Occurrence = occurrence,
        };
    }
}
