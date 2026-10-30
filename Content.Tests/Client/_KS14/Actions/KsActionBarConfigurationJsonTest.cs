#nullable enable
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

    /// <summary>
    ///     Well-formed JSON missing a required key is rejected by the schema check.
    /// </summary>
    [Test]
    public void JsonMissingRequiredKeysFails()
    {
        const string json = "{ \"version\": 1, \"entries\": [] }";

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out _), Is.False);
    }

    /// <summary>
    ///     The layout file sits in user data, so anything at all can turn up in it. A Try method must
    ///         answer false rather than throw.
    /// </summary>
    [TestCase("", TestName = "Empty")]
    [TestCase("not json at all", TestName = "Garbage")]
    [TestCase("{ \"version\": 2, \"entries\": [", TestName = "Truncated")]
    [TestCase("{ \"version\": 2, \"version\": 3, \"entries\": [], \"knownActions\": [] }", TestName = "DuplicateKey")]
    [TestCase("\u0000\u0001\u0002", TestName = "ControlCharacters")]
    public void MalformedInputFailsWithoutThrowing(string json)
    {
        var deserialized = true;
        Assert.DoesNotThrow(() => deserialized = KsActionBarConfigurationJson.TryDeserialize(json, out _));
        Assert.That(deserialized, Is.False, "Malformed input was accepted as a layout.");
    }

    /// <summary>
    ///     The codec reports the version it read; deciding what to do about a mismatch is the caller's.
    /// </summary>
    [Test]
    public void VersionIsReportedRatherThanEnforced()
    {
        var configuration = new KsActionBarConfiguration { Version = KsActionBarConfiguration.CurrentVersion + 7 };

        var json = KsActionBarConfigurationJson.Serialize(configuration);

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.That(restored.Version, Is.EqualTo(KsActionBarConfiguration.CurrentVersion + 7));
    }

    [Test]
    public void EmptyConfigurationRoundTrips()
    {
        var json = KsActionBarConfigurationJson.Serialize(new KsActionBarConfiguration());

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Entries, Is.Empty);
            Assert.That(restored.KnownActions, Is.Empty);
        });
    }

    /// <summary>
    ///     Entry order is slot order, so it has to survive the round trip exactly.
    /// </summary>
    [Test]
    public void EntryOrderSurvivesRoundTrip()
    {
        var configuration = new KsActionBarConfiguration
        {
            Entries =
            [
                new KsActionBarConfigurationEntry { Action = Identity("ActionFirst", null, 0) },
                new KsActionBarConfigurationEntry { Action = Identity("ActionSecond", null, 0) },
                new KsActionBarConfigurationEntry { Action = Identity("ActionThird", null, 0) },
            ],
        };

        var json = KsActionBarConfigurationJson.Serialize(configuration);

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.That(
            restored.Entries.ConvertAll(entry => entry.Action?.ActionPrototype),
            Is.EqualTo(new[] { "ActionFirst", "ActionSecond", "ActionThird" }));
    }

    /// <summary>
    ///     Occurrence is what tells two of the same item's actions apart, so it has to round trip and has
    ///         to take part in matching.
    /// </summary>
    [Test]
    public void OccurrenceDistinguishesOtherwiseIdenticalActions()
    {
        var configuration = new KsActionBarConfiguration
        {
            Entries =
            [
                new KsActionBarConfigurationEntry { Action = Identity("ActionToggleLight", "HandheldPDA", 0) },
                new KsActionBarConfigurationEntry { Action = Identity("ActionToggleLight", "HandheldPDA", 1) },
            ],
        };

        var json = KsActionBarConfigurationJson.Serialize(configuration);

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Entries[0].Action?.Occurrence, Is.EqualTo(0));
            Assert.That(restored.Entries[1].Action?.Occurrence, Is.EqualTo(1));
            Assert.That(
                KsActionBarIdentity.MatchesSaved(
                    Identity("ActionToggleLight", "HandheldPDA", 0),
                    Identity("ActionToggleLight", "HandheldPDA", 1)),
                Is.False,
                "Two actions from identical providers were matched despite differing occurrences.");
        });
    }

    /// <summary>
    ///     Strings written into the file are prototype ids in practice, but the escaping path still has
    ///         to hold if one ever contains a quote or a backslash.
    /// </summary>
    [Test]
    public void AwkwardCharactersInIdentitiesSurviveRoundTrip()
    {
        // Built from char codes so the test source itself is not an escaping puzzle.
        var awkwardAction = "Action" + (char)34 + "With" + (char)92 + "Escapes";
        var awkwardProvider = "Provider" + (char)9 + "With" + (char)10 + "Controls";

        var configuration = new KsActionBarConfiguration
        {
            Entries = [new KsActionBarConfigurationEntry { Action = Identity(awkwardAction, awkwardProvider, 0) }],
        };

        var json = KsActionBarConfigurationJson.Serialize(configuration);

        Assert.That(KsActionBarConfigurationJson.TryDeserialize(json, out var restored), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Entries[0].Action?.ActionPrototype, Is.EqualTo(awkwardAction));
            Assert.That(restored.Entries[0].Action?.ProviderPrototype, Is.EqualTo(awkwardProvider));
        });
    }

    [Test]
    public void WhitespaceProviderMatchesIntrinsicIdentity()
    {
        var saved = Identity("ActionScream", "   ", 0);
        var current = Identity("ActionScream", null, 0);

        Assert.That(KsActionBarIdentity.MatchesSaved(saved, current), Is.True);
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

    private static KsSavedActionIdentity Identity(string action, string? provider, int occurrence)
    {
        return new KsSavedActionIdentity
        {
            ActionPrototype = action,
            ProviderPrototype = provider,
            Occurrence = occurrence,
        };
    }
}
