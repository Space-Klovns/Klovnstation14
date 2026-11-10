using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.TTS;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._KS14.TTS;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     A character's chosen TTS voice: validated, stored, and given to their body when they spawn. And the random
///         voice everyone else gets never being one that is kept back.
/// </summary>
[TestOf(typeof(TtsSystem))]
public sealed class KsTtsProfileTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string EliteVoice = "en_US-combatant-elite-medium";
    private const string SomeVoice = "en_GB-alan-medium";

    [Test]
    public async Task ProfileKeepsOnlySelectableVoices()
    {
        HumanoidCharacterProfile selectable = null, unselectable = null, unknown = null, random = null;

        await Server.WaitPost(() =>
        {
            var collection = Server.InstanceDependencyCollection;
            var profile = new HumanoidCharacterProfile();
            selectable = profile.WithTtsVoice(SomeVoice).Validated(ServerSession!, collection);
            unselectable = profile.WithTtsVoice(EliteVoice).Validated(ServerSession!, collection);
            unknown = profile.WithTtsVoice("NotAVoice").Validated(ServerSession!, collection);
            random = profile.WithTtsVoice(null).Validated(ServerSession!, collection);
        });

        Assert.Multiple(() =>
        {
            Assert.That(selectable!.TtsVoice, Is.EqualTo(new ProtoId<TtsVoicePrototype>(SomeVoice)));
            Assert.That(unselectable!.TtsVoice, Is.Null, "a voice kept back for particular entities can't be picked");
            Assert.That(unknown!.TtsVoice, Is.Null, "a voice that doesn't exist falls back to random");
            Assert.That(random!.TtsVoice, Is.Null);

            // Equality and cloning have to know about the field, or the editor can't tell a changed voice needs saving.
            var profile = new HumanoidCharacterProfile();
            Assert.That(profile.WithTtsVoice(SomeVoice).MemberwiseEquals(profile), Is.False);
            Assert.That(profile.WithTtsVoice(SomeVoice).Clone().TtsVoice, Is.EqualTo(new ProtoId<TtsVoicePrototype>(SomeVoice)));
        });
    }

    [Test]
    public async Task ProfileVoiceSurvivesTheDatabase()
    {
        var configuration = Server.ResolveDependency<IConfigurationManager>();
        var serialization = Server.ResolveDependency<ISerializationManager>();
        var log = Server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        builder.UseSqlite(connection);
        var db = new ServerDbSqlite(() => builder.Options, true, configuration, true, log, serialization);

        var preferences = (ServerPreferencesManager)Server.ResolveDependency<IServerPreferencesManager>();
        var user = new NetUserId(Guid.NewGuid());

        var withVoice = new HumanoidCharacterProfile().WithTtsVoice(SomeVoice);
        await db.InitPrefsAsync(user, withVoice);
        await db.SaveCharacterSlotAsync(user, new HumanoidCharacterProfile(), 1);

        var stored = await db.GetPlayerPreferencesAsync(user);
        var first = preferences.ConvertProfiles(stored!.Profiles.Single(profile => profile.Slot == 0));
        var second = preferences.ConvertProfiles(stored.Profiles.Single(profile => profile.Slot == 1));

        Assert.Multiple(() =>
        {
            Assert.That(first.TtsVoice, Is.EqualTo(new ProtoId<TtsVoicePrototype>(SomeVoice)));
            Assert.That(first.MemberwiseEquals(withVoice), Is.True);
            Assert.That(second.TtsVoice, Is.Null, "random is stored as no voice");
        });
    }

    [Test]
    public async Task SpawningGivesTheBodyTheProfileVoice()
    {
        var map = await Pair.CreateTestMap();
        EntityUid chosenUid = default, randomUid = default;

        await Server.WaitPost(() =>
        {
            chosenUid = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            randomUid = SEntMan.SpawnEntity("MobHuman", map.MapCoords);

            var profile = new HumanoidCharacterProfile();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new PlayerSpawnCompleteEvent(chosenUid, ServerSession!,
                jobId: null, lateJoin: false, silent: true, joinOrder: 0, map.Grid.Owner, profile.WithTtsVoice(SomeVoice)));
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new PlayerSpawnCompleteEvent(randomUid, ServerSession!,
                jobId: null, lateJoin: false, silent: true, joinOrder: 0, map.Grid.Owner, profile));
        });

        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.GetComponentOrNull<TtsVoiceComponent>(chosenUid)?.Id,
                Is.EqualTo(new ProtoId<TtsVoicePrototype>(SomeVoice)));
            Assert.That(SEntMan.GetComponentOrNull<TtsVoiceComponent>(randomUid)?.Id, Is.Null,
                "random is left to be settled the first time they speak");
        });
    }

    [Test]
    public async Task RandomVoicesAreAlwaysSelectable()
    {
        var map = await Pair.CreateTestMap();
        var chosen = new List<ProtoId<TtsVoicePrototype>?>();

        await Server.WaitPost(() =>
        {
            // One in 33 per pick if unselectable voices were in the pool: 400 picks miss it about once in 700,000 runs.
            for (var i = 0; i < 400; i++)
            {
                var speakerUid = SEntMan.SpawnEntity(null, map.MapCoords);

                // Also one left holding a voice that no longer exists, as after a prototype reload.
                if (i == 0)
                    SEntMan.AddComponent<TtsVoiceComponent>(speakerUid).Id = "RemovedVoice";

                SEntMan.EventBus.RaiseEvent(EventSource.Local, new EntitySpokeEvent(speakerUid, "hello", null, null));
                chosen.Add(SEntMan.GetComponent<TtsVoiceComponent>(speakerUid).Id);
            }
        });

        var selectable = Server.ProtoMan.EnumeratePrototypes<TtsVoicePrototype>()
            .Where(voice => voice.Selectable)
            .Select(voice => (ProtoId<TtsVoicePrototype>?)voice.ID)
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.All.Not.Null);
            Assert.That(chosen, Is.All.Matches<ProtoId<TtsVoicePrototype>?>(voice => selectable.Contains(voice)),
                "every random voice is a selectable one");
            Assert.That(chosen[0], Is.Not.EqualTo(new ProtoId<TtsVoicePrototype>("RemovedVoice")),
                "a removed voice is replaced");
        });
    }
}
