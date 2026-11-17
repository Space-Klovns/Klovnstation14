using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Administration.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._KS14.AdminMusic;
using Content.Shared._KS14.Network;
using Content.Shared._KS14.TTS;
using Content.Shared._KS14.Voice;
using Content.Shared.Administration;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Eui;
using Content.Shared.FeedbackSystem;
using Content.Shared.Mapping;
using Content.Shared.Players;
using Content.Shared.Players.JobWhitelist;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Lidgren.Network;
using Robust.Client.Network;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Network;

/// <summary>
///     Hand-written net message readers must refuse a length they cannot trust, before allocating for it: one with
///         no data behind it (GHSA-m25p-v44q-gjqq), one past the hard limit in <see cref="KsNetMessageLimits"/>, and,
///         on the server, any message only the server sends. And they must still read everything a real sender
///         writes, because a reader that throws disconnects whoever sent the message.
/// </summary>
[TestFixture]
[TestOf(typeof(KsNetMessageLimits))]
public sealed class KsNetMessageLimitsTest : GameTest
{
    /// <summary>
    ///     How far a limit must sit above what live content really sends.
    /// </summary>
    private const int Headroom = 10;

    private const int LyingEntries = KsNetMessageLimits.MaxPrototypeListEntries - 1;

    private static NetOutgoingMessage NewBuffer()
    {
        return (NetOutgoingMessage)Activator.CreateInstance(typeof(NetOutgoingMessage), nonPublic: true)!;
    }

    /// <summary>
    ///     What was written to <paramref name="written"/>, as it would arrive.
    /// </summary>
    private static NetIncomingMessage Incoming(NetOutgoingMessage written)
    {
        var incoming = (NetIncomingMessage)Activator.CreateInstance(typeof(NetIncomingMessage), nonPublic: true)!;
        var data = new byte[Math.Max(written.LengthBytes, 1)];
        Array.Copy(written.Data, data, written.LengthBytes);
        incoming.Data = data;
        incoming.LengthBits = written.LengthBits;
        incoming.Position = 0;
        return incoming;
    }

    private static TMessage RoundTrip<TMessage>(TMessage message, IRobustSerializer serializer = null)
        where TMessage : NetMessage, new()
    {
        var buffer = NewBuffer();
        message.WriteToBuffer(buffer, serializer);

        var read = new TMessage();
        read.ReadFromBuffer(Incoming(buffer), serializer);
        return read;
    }

    private static void AssertRefused<TMessage>(NetOutgoingMessage buffer, string why, INetChannel channel = null)
        where TMessage : NetMessage, new()
    {
        var message = new TMessage { MsgChannel = channel };
        Assert.Throws<InvalidOperationException>(
            () => message.ReadFromBuffer(Incoming(buffer), null),
            $"{typeof(TMessage).Name} was read although {why}.");
    }

    /// <summary>
    ///     A count with nothing after it. Kept under the hard limits, so that only the check against the data
    ///         really in the message can refuse it.
    /// </summary>
    private static NetOutgoingMessage LyingCount()
    {
        var buffer = NewBuffer();
        buffer.WriteVariableInt32(LyingEntries);
        return buffer;
    }

    /// <summary>
    ///     A count, and that many empty strings: every byte the count promises is really there.
    /// </summary>
    private static NetOutgoingMessage EmptyStrings(int count)
    {
        var buffer = NewBuffer();
        buffer.WriteVariableInt32(count);
        for (var i = 0; i < count; i++)
            buffer.Write(string.Empty);

        return buffer;
    }

    [Test]
    public void ACountWithNoDataBehindItIsRefused()
    {
        const string why = "it declares thousands of entries and holds none";

        Assert.Multiple(() =>
        {
            AssertRefused<MsgUpdateAdminStatus>(LyingCount(), why);
            AssertRefused<MsgJobWhitelist>(LyingCount(), why);
            AssertRefused<MsgRoleBans>(LyingCount(), why);
            AssertRefused<MsgPlayTime>(LyingCount(), why);
            AssertRefused<MsgUpdateConstructionFavorites>(LyingCount(), why);

            var feedback = NewBuffer();
            feedback.Write(false);
            feedback.WritePadBits();
            feedback.WriteVariableInt32(LyingEntries);
            AssertRefused<FeedbackPopupMessage>(feedback, why);

            var previews = NewBuffer();
            previews.Write((byte)KsTtsPreviewStatus.Ready);
            previews.Write(false);
            previews.WriteVariableInt32(LyingEntries);
            AssertRefused<KsTtsPreviewMessage>(previews, why);

            var music = NewBuffer();
            music.Write((byte)200);
            AssertRefused<KsAdminMusicDataMessage>(music, why);
        });
    }

    [Test]
    public void APayloadLengthWithNoDataBehindItIsRefused()
    {
        const string why = "it declares more payload than it holds";

        var preview = NewBuffer();
        preview.Write((byte)KsTtsPreviewStatus.Ready);
        preview.Write(false);
        preview.WriteVariableInt32(1);
        preview.Write("voice");
        preview.Write((byte)0);
        preview.WriteVariableInt32(KsTtsPreviewMessage.MaxPreviewBytes);

        var voice = NewBuffer();
        voice.Write(new NetEntity(1));
        voice.Write((ushort)0);
        voice.Write((byte)0);
        voice.Write(false);
        voice.Write((ushort)KsVoiceFrameMessage.MaxPayloadBytes);

        Assert.Multiple(() =>
        {
            AssertRefused<KsTtsPreviewMessage>(preview, why);
            AssertRefused<KsVoiceFrameMessage>(voice, why);
        });
    }

    /// <summary>
    ///     These counts are honest, so only the hard limit can refuse them.
    /// </summary>
    [Test]
    public void ACountOverTheHardLimitIsRefusedEvenWithItsDataPresent()
    {
        const string why = "it holds more entries than the hard limit";

        Assert.Multiple(() =>
        {
            AssertRefused<MsgUpdateConstructionFavorites>(EmptyStrings(KsNetMessageLimits.MaxConstructionFavorites + 1), why);
            AssertRefused<MsgJobWhitelist>(EmptyStrings(KsNetMessageLimits.MaxPrototypeListEntries + 1), why);
            AssertRefused<MsgRoleBans>(EmptyStrings(KsNetMessageLimits.MaxPrototypeListEntries + 1), why);
            AssertRefused<MsgUpdateAdminStatus>(EmptyStrings(KsNetMessageLimits.MaxAdminCommands + 1), why);

            var atLimit = new MsgUpdateConstructionFavorites();
            Assert.DoesNotThrow(
                () => atLimit.ReadFromBuffer(Incoming(EmptyStrings(KsNetMessageLimits.MaxConstructionFavorites)), null),
                "A count exactly at the hard limit was refused.");
        });
    }

    [Test]
    public void RealMessagesStillRoundTrip()
    {
        var jobs = Enumerable.Range(0, 300).Select(index => $"Job{index}").ToList();

        var whitelist = RoundTrip(new MsgJobWhitelist { Whitelist = jobs.ToHashSet() });
        var roleBans = RoundTrip(new MsgRoleBans
        {
            JobBans = jobs.Select(job => new ProtoId<JobPrototype>(job)).ToList(),
            // Empty ids: the smallest thing a real sender can write.
            AntagBans = new List<ProtoId<AntagPrototype>> { string.Empty, string.Empty },
        });
        var playTime = RoundTrip(new MsgPlayTime
        {
            Trackers = jobs.ToDictionary(job => job, _ => TimeSpan.FromHours(3)),
        });
        var favorites = RoundTrip(new MsgUpdateConstructionFavorites
        {
            Favorites = jobs.Select(job => new ProtoId<ConstructionPrototype>(job)).ToList(),
        });
        var adminStatus = RoundTrip(new MsgUpdateAdminStatus { AvailableCommands = jobs.ToArray() });

        var entries = new[]
        {
            new KsAdminMusicEntry(new ResPath("/Audio/a.ogg"), 0.5f, TimeSpan.FromSeconds(4)),
            // An empty path: the smallest entry there can be.
            new KsAdminMusicEntry(new ResPath(string.Empty), 0f, TimeSpan.Zero),
        };
        var music = RoundTrip(new KsAdminMusicDataMessage { EntryCount = (byte)entries.Length, Entries = entries });

        var previews = RoundTrip(new KsTtsPreviewMessage
        {
            Status = KsTtsPreviewStatus.Baking,
            Replace = true,
            Previews = new List<KsTtsPreview>
            {
                new(string.Empty, default, Array.Empty<byte>()),
                new("voice", default, new byte[40_000]),
            },
        });

        var voice = RoundTrip(new KsVoiceFrameMessage
        {
            Source = new NetEntity(7),
            Sequence = 9,
            StreamStart = true,
            Payload = new byte[KsVoiceFrameMessage.MaxPayloadBytes],
        });

        Assert.Multiple(() =>
        {
            Assert.That(whitelist.Whitelist, Is.EquivalentTo(jobs));
            Assert.That(roleBans.JobBans, Has.Count.EqualTo(jobs.Count));
            Assert.That(roleBans.AntagBans, Has.Count.EqualTo(2));
            Assert.That(playTime.Trackers, Has.Count.EqualTo(jobs.Count));
            Assert.That(favorites.Favorites, Has.Count.EqualTo(jobs.Count));
            Assert.That(adminStatus.AvailableCommands, Is.EqualTo(jobs));
            Assert.That(music.Entries, Is.EqualTo(entries));
            Assert.That(previews.Previews, Has.Count.EqualTo(2));
            Assert.That(previews.Previews[1].Data, Has.Length.EqualTo(40_000));
            Assert.That(voice.Payload, Has.Length.EqualTo(KsVoiceFrameMessage.MaxPayloadBytes));
        });
    }

    /// <summary>
    ///     The server reads every registered message a client sends, handler or not, so one it only ever sends is
    ///         refused before any of it is read. The client still reads the very same bytes.
    /// </summary>
    [Test]
    public void TheServerRefusesMessagesOnlyItSends()
    {
        var serverChannel = Pair.Player!.Channel;
        var clientChannel = Client.ResolveDependency<IClientNetManager>().ServerChannel;
        Assert.That(clientChannel, Is.Not.Null);

        var valid = NewBuffer();
        new MsgJobWhitelist { Whitelist = new HashSet<string> { "Captain" } }.WriteToBuffer(valid, null);

        var voice = NewBuffer();
        new KsVoiceFrameMessage { Payload = new byte[16] }.WriteToBuffer(voice, null);

        Assert.Multiple(() =>
        {
            const string why = "a server was reading it and only a server sends it";
            AssertRefused<MsgJobWhitelist>(valid, why, serverChannel);
            AssertRefused<KsVoiceFrameMessage>(voice, why, serverChannel);

            var onClient = new MsgJobWhitelist { MsgChannel = clientChannel };
            Assert.DoesNotThrow(() => onClient.ReadFromBuffer(Incoming(valid), null));
            Assert.That(onClient.Whitelist, Is.EquivalentTo(new[] { "Captain" }));

            // Clients do send this one.
            var fromClient = new MsgUpdateConstructionFavorites { MsgChannel = serverChannel };
            Assert.DoesNotThrow(() => fromClient.ReadFromBuffer(Incoming(EmptyStrings(3)), null));
        });
    }

    /// <summary>
    ///     Admin windows may send far more than an ordinary player's. The looser limit goes with being an admin
    ///         right now, not with the message.
    /// </summary>
    [Test]
    public async Task OnlyAdminsGetTheLooserLimit()
    {
        var adminManager = Server.ResolveDependency<IAdminManager>();
        var session = Pair.Player!;
        var clientChannel = Client.ResolveDependency<IClientNetManager>().ServerChannel;
        const int overPlayerLimit = KsNetMessageLimits.MaxEuiMessageBytes + 1;

        void Check(INetChannel channel, int length)
        {
            KsNetMessageLimits.AssertAtMost(
                new MsgEuiMessage { MsgChannel = channel },
                length,
                KsNetMessageLimits.MaxEuiMessageBytes,
                KsNetMessageLimits.MaxAdminEuiMessageBytes);
        }

        // A whole message of that size, with all of its payload present, read the way the server reads it.
        var message = NewBuffer();
        message.Write((uint)1);
        message.WriteVariableInt32(overPlayerLimit);
        message.Write(new byte[overPlayerLimit]);

        await Server.WaitAssertion(() =>
        {
            var wasAdmin = adminManager.IsAdmin(session);
            try
            {
                adminManager.DeAdmin(session);
                Assert.Multiple(() =>
                {
                    Assert.Throws<InvalidOperationException>(() => Check(session.Channel, overPlayerLimit),
                        "A player who is not an admin was allowed the admin limit.");
                    AssertRefused<MsgEuiMessage>(message, "an ordinary player sent more than their limit", session.Channel);
                    Assert.DoesNotThrow(() => Check(session.Channel, KsNetMessageLimits.MaxEuiMessageBytes));
                });

                adminManager.ReAdmin(session);
                Assert.Multiple(() =>
                {
                    Assert.That(adminManager.IsAdmin(session), "The test player could not be made an admin.");
                    Assert.DoesNotThrow(() => Check(session.Channel, overPlayerLimit),
                        "An admin was held to an ordinary player's limit.");
                    Assert.DoesNotThrow(() => Check(session.Channel, KsNetMessageLimits.MaxAdminEuiMessageBytes));
                    Assert.Throws<InvalidOperationException>(
                        () => Check(session.Channel, KsNetMessageLimits.MaxAdminEuiMessageBytes + 1),
                        "An admin was allowed more than the admin limit.");
                });
            }
            finally
            {
                if (wasAdmin)
                    adminManager.ReAdmin(session);
                else
                    adminManager.DeAdmin(session);
            }
        });

        // Read by a client, the sender is the server.
        Assert.DoesNotThrow(() => Check(clientChannel, overPlayerLimit));
    }

    /// <summary>
    ///     A map as large as the largest we ship has to survive the trip, engine deserialisation limits included.
    /// </summary>
    [Test]
    public async Task ALargeMapStillRoundTrips()
    {
        var serializer = Server.ResolveDependency<IRobustSerializer>();
        var largestMapBytes = 0L;

        await Server.WaitPost(() =>
        {
            var resources = Server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
            foreach (var path in resources.ContentFindFiles(new ResPath("/Maps/")))
            {
                if (path.Extension != "yml")
                    continue;

                using var stream = resources.ContentFileRead(path);
                largestMapBytes = Math.Max(largestMapBytes, stream.Length);
            }
        });

        Assert.That(largestMapBytes, Is.GreaterThan(0), "Found no maps to size the test by.");
        Assert.That(largestMapBytes * 4, Is.LessThanOrEqualTo(KsNetMessageLimits.MaxMapDataUncompressedBytes),
            "The largest map is within four times the limit on a map sent to a mapper.");

        // Not all one character, so that it doesn't compress to nothing.
        var yml = string.Create((int)largestMapBytes, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = (char)('a' + i * 7 % 23);
        });

        using var context = new ZStdCompressionContext();
        var read = RoundTrip(new MappingMapDataMessage { Context = context, Yml = yml }, serializer);

        Assert.That(read.Yml, Has.Length.EqualTo(yml.Length));
        Assert.That(read.Yml.GetHashCode(), Is.EqualTo(yml.GetHashCode()));
    }

    /// <summary>
    ///     The declared uncompressed length is the sender's word: data that decompresses to more than it is refused
    ///         while decompressing, not after.
    /// </summary>
    [Test]
    public void MapDataLongerThanDeclaredIsRefused()
    {
        var serializer = Server.ResolveDependency<IRobustSerializer>();
        using var context = new ZStdCompressionContext();

        var honest = NewBuffer();
        new MappingMapDataMessage { Context = context, Yml = new string('a', 4_000_000) }.WriteToBuffer(honest, serializer);
        var honestIncoming = Incoming(honest);
        var declaredLength = honestIncoming.ReadVariableInt32();
        var compressedLength = honestIncoming.ReadVariableInt32();
        var compressed = honestIncoming.ReadBytes(compressedLength);

        NetOutgoingMessage Declaring(int uncompressedLength)
        {
            var buffer = NewBuffer();
            buffer.WriteVariableInt32(uncompressedLength);
            buffer.WriteVariableInt32(compressed.Length);
            buffer.Write(compressed);
            return buffer;
        }

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => new MappingMapDataMessage().ReadFromBuffer(Incoming(Declaring(declaredLength / 2)), serializer),
                "Map data that decompresses to twice its declared length was read.");
            Assert.Throws<InvalidOperationException>(
                () => new MappingMapDataMessage().ReadFromBuffer(
                    Incoming(Declaring(KsNetMessageLimits.MaxMapDataUncompressedBytes + 1)), serializer),
                "Map data declaring more than the hard limit was read.");

            var control = new MappingMapDataMessage();
            Assert.DoesNotThrow(() => control.ReadFromBuffer(Incoming(Declaring(declaredLength)), serializer));
            Assert.That(control.Yml, Has.Length.EqualTo(4_000_000));
        });
    }

    /// <summary>
    ///     A limit that live content comes near would start disconnecting players as content grows. Every number
    ///         here is read from the running game.
    /// </summary>
    [Test]
    public async Task LimitsSitFarAboveLiveContent()
    {
        var serializer = Server.ResolveDependency<IRobustSerializer>();
        var preferencesManager = Server.ResolveDependency<IServerPreferencesManager>();
        var consoleHost = Server.ResolveDependency<IConsoleHost>();
        var userId = Pair.Player!.UserId;

        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ProtoMan;
            var preferences = preferencesManager.GetPreferences(userId);

            using var preferencesStream = new MemoryStream();
            serializer.SerializeDirect(preferencesStream, preferences);

            using var profileStream = new MemoryStream();
            serializer.Serialize(profileStream, HumanoidCharacterProfile.Random());

            Assert.Multiple(() =>
            {
                AssertHeadroom(prototypes.Count<JobPrototype>(), KsNetMessageLimits.MaxPrototypeListEntries, "jobs");
                AssertHeadroom(prototypes.Count<AntagPrototype>(), KsNetMessageLimits.MaxPrototypeListEntries, "antagonists");
                AssertHeadroom(prototypes.Count<PlayTimeTrackerPrototype>(), KsNetMessageLimits.MaxPrototypeListEntries, "playtime trackers");
                AssertHeadroom(prototypes.Count<FeedbackPopupPrototype>(), KsNetMessageLimits.MaxPrototypeListEntries, "feedback popups");
                AssertHeadroom(prototypes.Count<ConstructionPrototype>(), KsNetMessageLimits.MaxConstructionFavorites, "construction recipes");
                AssertHeadroom(prototypes.Count<TtsVoicePrototype>(), KsNetMessageLimits.MaxTtsPreviews, "TTS voices");
                AssertHeadroom(consoleHost.AvailableCommands.Count, KsNetMessageLimits.MaxAdminCommands, "console commands");
                AssertHeadroom(preferencesStream.Length, KsNetMessageLimits.MaxPreferencesBytes, "bytes of preferences");
                AssertHeadroom(profileStream.Length, KsNetMessageLimits.MaxCharacterProfileBytes, "bytes of character profile");
            });
        });
    }

    private static void AssertHeadroom(long live, int limit, string what)
    {
        Assert.That(live, Is.GreaterThan(0), $"Found no {what} to compare the limit against.");
        Assert.That(live * Headroom, Is.LessThanOrEqualTo(limit),
            $"There are {live} {what}, within {Headroom} times the limit of {limit}. Raise the limit.");
    }
}
