#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Language;
using Content.Server.Animals.Components;
using Content.Server.Examine;
using Content.Server.Radio;
using Content.Server.Vocalization.Systems;
using Content.Shared._KS14.Language;
using Content.Shared._KS14.Language.Components;
using Content.Shared.Animals.Components;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Components.Triggers;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Language;

/// <summary>
///     Drives the <see cref="KsLanguageSystem"/> perception kernel directly: obfuscation,
///     knowledge resolution, comprehension and delivery variant picks.
/// </summary>
public sealed class KsLanguageTests : GameTest
{
    // We spawn and mutate server entities and re-attach the pooled player.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly ProtoId<KsLanguagePrototype> Common = "KsLangCommon";
    private static readonly ProtoId<RadioChannelPrototype> CommonChannel = "Common";
    private static readonly ProtoId<KsLanguagePrototype> Vox = "KsLangVox";
    private static readonly ProtoId<KsLanguagePrototype> Dwarvish = "KsLangDwarvish";
    private static readonly ProtoId<KsLanguagePrototype> Klovnish = "KsLangKlovnish";

    [Test]
    public async Task SyllableObfuscationDeterministicAndShapePreserving()
    {
        var method = new KsSyllableObfuscation
        {
            Syllables = new() { "ka", "ri", "zek", "tal" },
            MinSyllables = 1,
            MaxSyllables = 3,
        };

        var first = method.Obfuscate("Help me, the Bomb is armed!", 7, 13);
        var second = method.Obfuscate("Help me, the Bomb is armed!", 7, 13);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first), "same message, round and language must scramble identically");
            Assert.That(first, Does.Contain(", "), "punctuation and whitespace must survive verbatim");
            Assert.That(first, Does.EndWith("!"));
            Assert.That(char.IsUpper(first[0]), "a capitalized word must keep a leading capital");
            Assert.That(first, Does.Not.Contain("Help"), "no clear word may survive");
            Assert.That(first, Does.Not.Contain("Bomb"));
        });

        var wordA = method.Obfuscate("bomb", 7, 13);
        var wordB = method.Obfuscate("bomb bomb", 7, 13);
        Assert.That(wordB, Is.EqualTo(wordA + " " + wordA), "word scramble must be position-independent");
    }

    [Test]
    public async Task SyllableObfuscationVariesByRoundAndLanguage()
    {
        var method = new KsSyllableObfuscation
        {
            Syllables = new() { "ka", "ri", "zek", "tal", "mor", "dun", "vex", "sol" },
            MinSyllables = 2,
            MaxSyllables = 4,
        };

        var baseline = method.Obfuscate("the quick brown fox jumps over the lazy dog", 1, 13);
        var nextRound = method.Obfuscate("the quick brown fox jumps over the lazy dog", 2, 13);
        var otherLanguage = method.Obfuscate("the quick brown fox jumps over the lazy dog", 1, 14);

        Assert.Multiple(() =>
        {
            Assert.That(nextRound, Is.Not.EqualTo(baseline), "a new round must re-roll the scramble");
            Assert.That(otherLanguage, Is.Not.EqualTo(baseline), "different languages must scramble the same word differently");
        });
    }

    [Test]
    public async Task ElasticLexemeObfuscationMatchesWordLengths()
    {
        var method = new KsElasticLexemeObfuscation
        {
            Lexemes = new() { new KsElasticLexeme { Prefix = "h", Stretch = "o", Suffix = "nk" } },
        };

        // A single lexeme makes the transform purely structural, so exact-string asserts hold.
        var scrambled = method.Obfuscate("Meet me in maintenance near the bar at nine, the captain suspects nothing.", 7, 13);
        Assert.That(scrambled, Is.EqualTo("Honk ho ho hoooooooonk honk hon hon ho honk, hon hoooonk hooooonk hoooonk."));

        Assert.Multiple(() =>
        {
            Assert.That(method.Obfuscate("HELP ME", 7, 13), Is.EqualTo("HONK HO"), "all-caps shouting must survive");
            Assert.That(method.Obfuscate("bomb bomb", 7, 13), Is.EqualTo(method.Obfuscate("bomb", 7, 13) + " " + method.Obfuscate("bomb", 7, 13)),
                "word scramble must be position-independent");
        });
    }

    [Test]
    public async Task ElasticLexemeObfuscationClampsStretchAndRerollsLexeme()
    {
        var method = new KsElasticLexemeObfuscation
        {
            Lexemes = new()
            {
                new KsElasticLexeme { Prefix = "h", Stretch = "o", Suffix = "nk" },
                new KsElasticLexeme { Prefix = "bw", Stretch = "a", Suffix = "p" },
            },
            MaxStretch = 4,
        };

        var longWord = method.Obfuscate("Kolossalifragilistisch", 7, 13);
        Assert.That(longWord, Has.Length.EqualTo(7), "stretch must clamp at MaxStretch plus prefix and suffix");

        var single = method.Obfuscate("maintenance", 7, 13);
        Assert.That(method.Obfuscate("maintenance maintenance", 7, 13), Is.EqualTo(single + " " + single),
            "the lexeme pick must be word-stable, not positional");

        // The lexeme pick is keyed on (word, round, language); across many rounds it must vary.
        var outputs = new HashSet<string>();
        for (var round = 0; round < 32; round++)
            outputs.Add(method.Obfuscate("maintenance", round, 13));

        Assert.That(outputs, Has.Count.GreaterThan(1), "a new round must be able to re-roll the lexeme pick");

        var zeroCap = new KsElasticLexemeObfuscation
        {
            Lexemes = new() { new KsElasticLexeme { Stretch = "o" } },
            MaxStretch = 0,
        };
        Assert.That(zeroCap.Obfuscate("bomb", 7, 13), Is.EqualTo("o"),
            "the cap must never cut below one full stretch segment");
    }

    [Test]
    public async Task WordObfuscationScramblesLookalikeAlphabets()
    {
        var method = new KsSyllableObfuscation
        {
            Syllables = new() { "ka", "ri" },
        };

        Assert.Multiple(() =>
        {
            Assert.That(method.Obfuscate("the 𝐛𝐨𝐦𝐛 is armed", 7, 13), Does.Not.Contain("𝐛𝐨𝐦𝐛"),
                "mathematical-alphabet lookalikes must never pass through clear");
            Assert.That(method.Obfuscate("ⓑⓞⓜⓑ", 7, 13), Does.Not.Contain("ⓑⓞⓜⓑ"),
                "circled-letter lookalikes must never pass through clear");
            Assert.That(method.Obfuscate("hello, world!", 7, 13), Does.Contain(", "),
                "real punctuation must still survive verbatim");
            Assert.That(method.Obfuscate("hello, world!", 7, 13), Does.EndWith("!"));
        });
    }

    [Test]
    public async Task WordObfuscationFailsClosedOnMisauthoredBanks()
    {
        string? emptyBank = null;
        string? emptyStretch = null;
        string? emptySyllable = null;
        await Server.WaitPost(() =>
        {
            emptyBank = new KsElasticLexemeObfuscation().Obfuscate("the bomb is armed", 7, 13);
            emptyStretch = new KsElasticLexemeObfuscation
            {
                Lexemes = new() { new KsElasticLexeme { Prefix = "honk" } },
            }.Obfuscate("the bomb is armed", 7, 13);
            emptySyllable = new KsSyllableObfuscation
            {
                Syllables = new() { "" },
            }.Obfuscate("the bomb is armed", 7, 13);
        });

        // Fail closed = the incomprehensible line: no clear text, never a silently erased message.
        Assert.Multiple(() =>
        {
            Assert.That(emptyBank, Does.Not.Contain("bomb"), "an empty bank must never leak the clear message");
            Assert.That(emptyBank, Is.Not.Empty);
            Assert.That(emptyStretch, Does.Not.Contain("bomb"), "a stretchless lexeme must never leak the clear message");
            Assert.That(emptyStretch, Is.Not.Empty);
            Assert.That(emptySyllable, Does.Not.Contain("bomb"), "an empty-syllable bank must never leak the clear message");
            Assert.That(emptySyllable, Is.Not.Empty);
        });
    }

    [Test]
    public async Task UntaggedSpeakerTakesVanillaPath()
    {
        var map = await Pair.CreateTestMap();

        var started = true;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();
            var plain = SEntMan.SpawnEntity(null, map.GridCoords);
            started = sys.TryStartUtterance(plain, "hello", out _);
            SEntMan.DeleteEntity(plain);
        });

        Assert.That(started, Is.False, "an entity with no language components must never gate its speech");
    }

    [Test]
    public async Task ForcedLanguageStartsUtterance()
    {
        var map = await Pair.CreateTestMap();

        KsUtteranceContext? ctx = null;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();
            var plain = SEntMan.SpawnEntity(null, map.GridCoords);
            sys.TryStartUtterance(plain, "hello", out ctx, Vox);
            SEntMan.DeleteEntity(plain);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ctx, Is.Not.Null, "a forced language must gate even an untagged speaker (radio relays rely on this)");
            Assert.That(ctx!.LanguageId, Is.EqualTo(Vox));
            Assert.That(ctx.Obfuscated, Is.Not.EqualTo("hello"));
        });
    }

    [Test]
    public async Task SpeciesKnowledgeResolvesOnSpawn()
    {
        var map = await Pair.CreateTestMap();

        KsLanguageSpeakerComponent? speaker = null;
        await Server.WaitPost(() =>
        {
            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            SEntMan.TryGetComponent(dwarf, out speaker);
        });

        Assert.That(speaker, Is.Not.Null, "species knowledge must produce a resolved speaker cache on spawn");
        Assert.Multiple(() =>
        {
            Assert.That(speaker!.Spoken, Does.Contain(Common));
            Assert.That(speaker.Spoken, Does.Contain(Dwarvish));
            Assert.That(speaker.CurrentLanguage, Is.EqualTo(Common), "lowest sort order must be the spawn default");
        });
    }

    [Test]
    public async Task UnderstandsMatrix()
    {
        var map = await Pair.CreateTestMap();

        bool dwarfHearsDwarvish = false, dwarfHearsVox = true, plainHearsVox = true, omniglotHearsVox = false;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();
            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            var plain = SEntMan.SpawnEntity(null, map.GridCoords);
            var omniglot = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.AddComponent<KsOmniglotComponent>(omniglot);

            sys.TryStartUtterance(dwarf, "rock and stone", out var dwarvishCtx, Dwarvish);
            sys.TryStartUtterance(dwarf, "rock and stone", out var voxCtx, Vox);

            dwarfHearsDwarvish = sys.Understands(dwarf, dwarvishCtx!);
            dwarfHearsVox = sys.Understands(dwarf, voxCtx!);
            plainHearsVox = sys.Understands(plain, voxCtx!);
            omniglotHearsVox = sys.Understands(omniglot, voxCtx!);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dwarfHearsDwarvish, Is.True, "a dwarf understands dwarvish");
            Assert.That(dwarfHearsVox, Is.False, "a dwarf does not understand vox");
            Assert.That(plainHearsVox, Is.False, "an untagged listener knows only the default language");
            Assert.That(omniglotHearsVox, Is.True, "an omniglot understands everything");
        });
    }

    [Test]
    public async Task IntrinsicGrantAppliesAndRequiresGates()
    {
        var map = await Pair.CreateTestMap();

        List<string> granted = new();
        List<string> gated = new();
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();

            var clown = SEntMan.SpawnEntity(null, map.GridCoords);
            var grant = SEntMan.AddComponent<KsLanguageGrantComponent>(clown);
            grant.Speaks.Add(Klovnish);
            grant.Understands.Add(Klovnish);
            sys.InvalidateLanguages(clown);
            foreach (var lang in SEntMan.GetComponent<KsLanguageSpeakerComponent>(clown).Spoken)
                granted.Add(lang.Id);

            // A grant requiring a language the holder doesn't intrinsically know must not apply.
            var pretender = SEntMan.SpawnEntity(null, map.GridCoords);
            var gatedGrant = SEntMan.AddComponent<KsLanguageGrantComponent>(pretender);
            gatedGrant.Speaks.Add(Klovnish);
            gatedGrant.Requires.Add(Vox);
            sys.InvalidateLanguages(pretender);
            foreach (var lang in SEntMan.GetComponent<KsLanguageSpeakerComponent>(pretender).Spoken)
                gated.Add(lang.Id);
        });

        Assert.Multiple(() =>
        {
            Assert.That(granted, Does.Contain(Klovnish.Id), "an intrinsic grant must add its languages");
            Assert.That(granted, Does.Contain(Common.Id), "the implicit default must survive a grant");
            Assert.That(gated, Does.Not.Contain(Klovnish.Id), "an unmet requirement must gate the grant");
        });
    }

    [Test]
    public async Task SetCurrentLanguageValidates()
    {
        var map = await Pair.CreateTestMap();

        KsLanguageSpeakerComponent? speaker = null;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();
            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            speaker = SEntMan.GetComponent<KsLanguageSpeakerComponent>(dwarf);

            sys.SetCurrentLanguage(dwarf, Vox); // not spoken, must be rejected
            Assert.That(speaker.CurrentLanguage, Is.EqualTo(Common));

            sys.SetCurrentLanguage(dwarf, Dwarvish);
        });

        Assert.That(speaker!.CurrentLanguage, Is.EqualTo(Dwarvish));
    }

    [Test]
    public async Task ApplyListenerPicksRadioVariant()
    {
        var map = await Pair.CreateTestMap();

        MsgChatMessage? shared = null;
        MsgChatMessage? toDwarfVox = null;
        MsgChatMessage? toDwarfDwarvish = null;
        MsgChatMessage? toDwarfJammed = null;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsLanguageSystem>();

            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, dwarf);

            var voxSpeaker = SEntMan.SpawnEntity("MobVox", map.GridCoords);
            sys.TryStartUtterance(voxSpeaker, "kikeree", out var voxCtx, Vox);
            sys.TryStartUtterance(voxSpeaker, "kikeree", out var dwarvishCtx, Dwarvish);

            shared = new MsgChatMessage
            {
                Message = new ChatMessage(ChatChannel.Radio, "kikeree", "wrapped kikeree", NetEntity.Invalid, null),
            };

            // SendRadioMessage builds this clone per broadcast; mirror that here.
            var voxClone = new MsgChatMessage
            {
                Message = new ChatMessage(ChatChannel.Radio, voxCtx!.Obfuscated, "wrapped scrambled", NetEntity.Invalid, null),
            };
            var dwarvishClone = new MsgChatMessage
            {
                Message = new ChatMessage(ChatChannel.Radio, dwarvishCtx!.Obfuscated, "wrapped scrambled", NetEntity.Invalid, null),
            };

            toDwarfVox = sys.ApplyListener(shared, voxCtx, voxClone, null, ServerSession!);
            toDwarfDwarvish = sys.ApplyListener(shared, dwarvishCtx, dwarvishClone, null, ServerSession!);
            // A delivery without a scrambled clone (default-language utterance) falls through.
            toDwarfJammed = sys.ApplyListener(shared, voxCtx, null, null, ServerSession!);
        });

        Assert.Multiple(() =>
        {
            Assert.That(toDwarfVox, Is.Not.SameAs(shared), "a non-understander must get the scrambled clone");
            Assert.That(toDwarfVox!.Message.Message, Is.Not.EqualTo("kikeree"));
            Assert.That(toDwarfDwarvish, Is.SameAs(shared), "an understander must get the shared clear message");
            Assert.That(toDwarfJammed, Is.SameAs(shared), "a delivery without a scrambled clone must pass through untouched");
        });
    }

    [Test]
    public async Task VoiceTriggerRecordsExoticSpeechAsScrambledSound()
    {
        var map = await Pair.CreateTestMap();

        TriggerOnVoiceComponent? voice = null;
        string? scrambled = null;
        string? voxView = null;
        string? dwarfView = null;
        await Server.WaitPost(() =>
        {
            var language = Server.System<KsLanguageSystem>();
            var triggers = Server.System<TriggerSystem>();
            var examine = Server.System<ExamineSystem>();

            var speaker = SEntMan.SpawnEntity(null, map.GridCoords);
            var trigger = SEntMan.SpawnEntity(null, map.GridCoords);
            voice = SEntMan.AddComponent<TriggerOnVoiceComponent>(trigger);
            triggers.StartRecording((trigger, voice), null);

            language.TryStartUtterance(speaker, "the bomb is in maintenance", out var ctx, Vox);
            scrambled = ctx!.Obfuscated;
            SEntMan.EventBus.RaiseLocalEvent(trigger, new ListenEvent("the bomb is in maintenance", speaker, ctx));

            // Examine: everyone gets the language, only understanders the clear reading.
            var vox = SEntMan.SpawnEntity("MobVox", map.GridCoords);
            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            voxView = examine.GetExamineText(trigger, vox).ToMarkup();
            dwarfView = examine.GetExamineText(trigger, dwarf).ToMarkup();
        });

        Assert.Multiple(() =>
        {
            Assert.That(voice!.KeyPhrase, Is.EqualTo(scrambled), "the stored phrase must be the scrambled sound the device heard");
            Assert.That(voice.KsKeyPhraseLanguage, Is.EqualTo((ProtoId<KsLanguagePrototype>?) Vox));
            Assert.That(voice.KsKeyPhraseClear, Is.EqualTo("the bomb is in maintenance"), "the clear text must survive server-side for understander examine");
            Assert.That(voxView, Does.Contain("the bomb is in maintenance"), "an examiner who understands the language must get the clear reading");
            Assert.That(dwarfView, Does.Not.Contain("the bomb is in maintenance"), "an examiner who does not understand must never see the clear text");
            Assert.That(dwarfView, Does.Contain("Vox-pirin"), "everyone learns which language the phrase was recorded in");
        });
    }

    [Test]
    public async Task VoiceTriggerMatchesOnlyRecordedLanguage()
    {
        var map = await Pair.CreateTestMap();

        EntityUid trigger = default;
        await Server.WaitPost(() =>
        {
            var language = Server.System<KsLanguageSystem>();
            var triggers = Server.System<TriggerSystem>();

            var speaker = SEntMan.SpawnEntity(null, map.GridCoords);
            trigger = SEntMan.SpawnEntity(null, map.GridCoords);
            var voice = SEntMan.AddComponent<TriggerOnVoiceComponent>(trigger);
            SEntMan.AddComponent<DeleteOnTriggerComponent>(trigger);
            triggers.StartRecording((trigger, voice), null);

            language.TryStartUtterance(speaker, "open sesame", out var voxCtx, Vox);
            SEntMan.EventBus.RaiseLocalEvent(trigger, new ListenEvent("open sesame", speaker, voxCtx));

            // Same words in another language are different sounds and must not fire.
            language.TryStartUtterance(speaker, "open sesame", out var klovnishCtx, Klovnish);
            SEntMan.EventBus.RaiseLocalEvent(trigger, new ListenEvent("open sesame", speaker, klovnishCtx));
            SEntMan.EventBus.RaiseLocalEvent(trigger, new ListenEvent("open sesame", speaker));
        });

        await Pair.RunTicksSync(3);
        Assert.That(SEntMan.Deleted(trigger), Is.False, "the wrong language must not fire the trigger");

        await Server.WaitPost(() =>
        {
            var language = Server.System<KsLanguageSystem>();
            var speaker = SEntMan.SpawnEntity(null, map.GridCoords);
            language.TryStartUtterance(speaker, "I said open sesame loudly", out var voxCtx, Vox);
            SEntMan.EventBus.RaiseLocalEvent(trigger, new ListenEvent("I said open sesame loudly", speaker, voxCtx));
        });

        await Pair.RunTicksSync(3);
        Assert.That(SEntMan.Deleted(trigger), Is.True, "the phrase spoken in the recorded language must fire the trigger");
    }

    [Test]
    public async Task ParrotLearnsAndReplaysLanguage()
    {
        var map = await Pair.CreateTestMap();

        SpeechMemory memory = default;
        TryVocalizeEvent vocalize = default;
        await Server.WaitPost(() =>
        {
            var language = Server.System<KsLanguageSystem>();

            var speaker = SEntMan.SpawnEntity(null, map.GridCoords);
            var parrot = SEntMan.SpawnEntity(null, map.GridCoords);
            var parrotMemory = SEntMan.AddComponent<ParrotMemoryComponent>(parrot);
            SEntMan.AddComponent<ParrotListenerComponent>(parrot);
            parrotMemory.LearnChance = 1f; // deterministic learning

            language.TryStartUtterance(speaker, "the bomb is armed", out var ctx, Vox);
            SEntMan.EventBus.RaiseLocalEvent(parrot, new ListenEvent("the bomb is armed", speaker, ctx));
            memory = parrotMemory.SpeechMemories[0];

            vocalize = new TryVocalizeEvent();
            SEntMan.EventBus.RaiseLocalEvent(parrot, ref vocalize);
        });

        Assert.Multiple(() =>
        {
            Assert.That(memory.Message, Is.EqualTo("the bomb is armed"), "the parrot memorizes the clear phrase server-side");
            Assert.That(memory.KsLanguage, Is.EqualTo((ProtoId<KsLanguagePrototype>?) Vox), "the memory must carry the language it was heard in");
            Assert.That(vocalize.Handled, Is.True);
            Assert.That(vocalize.KsLanguage, Is.EqualTo((ProtoId<KsLanguagePrototype>?) Vox), "replay must speak the recorded language so perception gating applies");
        });
    }

    [Test]
    public async Task JammerGarblesScrambledCloneForNonUnderstanders()
    {
        var map = await Pair.CreateTestMap();

        var attemptEv = default(RadioReceiveAttemptEvent);
        var noCloneEv = default(RadioReceiveAttemptEvent);
        await Server.WaitPost(() =>
        {
            var channel = SProtoMan.Index(CommonChannel);

            var jammer = SEntMan.SpawnEntity(null, map.GridCoords);
            var jam = SEntMan.AddComponent<RadioJammerComponent>(jammer);
            jam.OnlyGarbleReceivedMessages = true;
            jam.GarbleStrength = 1f; // deterministic: every character garbles
            jam.SelectedPowerLevel = 0;
            jam.Settings = new[]
            {
                new RadioJammerComponent.RadioJamSetting { Wattage = 1f, Range = 10f, Message = "x", Name = "x" },
            };
            SEntMan.AddComponent<ActiveRadioJammerComponent>(jammer);

            var source = SEntMan.SpawnEntity(null, map.GridCoords);
            var receiver = SEntMan.SpawnEntity(null, map.GridCoords);
            var chatMsg = new MsgChatMessage
            {
                Message = new ChatMessage(ChatChannel.Radio, "the bomb is armed", "wrapped", NetEntity.Invalid, null),
            };

            attemptEv = new RadioReceiveAttemptEvent(channel, source, receiver, chatMsg, "honk hoonk hoonk!");
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref attemptEv);

            noCloneEv = new RadioReceiveAttemptEvent(channel, source, receiver, chatMsg);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref noCloneEv);
        });

        Assert.Multiple(() =>
        {
            Assert.That(attemptEv.NewChatMessage, Is.Not.Null, "a garble jammer must substitute the broadcast");
            Assert.That(attemptEv.KsNewObfuscatedChatMessage, Is.Not.Null,
                "an exotic broadcast must get a garbled scrambled clone, or non-understanders would receive clear-derived text");
            Assert.That(attemptEv.KsNewObfuscatedChatMessage!.Message.Message, Has.Length.EqualTo("honk hoonk hoonk!".Length),
                "the non-understander substitute must garble the scrambled text, not the clear message");
            Assert.That(noCloneEv.NewChatMessage, Is.Not.Null);
            Assert.That(noCloneEv.KsNewObfuscatedChatMessage, Is.Null, "a default-language broadcast needs no scrambled substitute");
        });
    }

    [Test]
    public async Task LanguageTraitSpecialsCompose()
    {
        var map = await Pair.CreateTestMap();

        KsLanguageSpeakerComponent? dwarfSpeaker = null;
        KsLanguageSpeakerComponent? plainSpeaker = null;
        await Server.WaitPost(() =>
        {
            // Both traits must survive; stacked AddComponentSpecial registries would replace one.
            var dwarf = SEntMan.SpawnEntity("MobDwarf", map.GridCoords);
            new KsLanguageAddSpecial { Speaks = new() { Vox } }.AfterEquip(dwarf);
            new KsLanguageAddSpecial { Speaks = new() { Klovnish } }.AfterEquip(dwarf);
            SEntMan.TryGetComponent(dwarf, out dwarfSpeaker);

            // First trait knowledge must not drop the implicit station default.
            var plain = SEntMan.SpawnEntity(null, map.GridCoords);
            new KsLanguageAddSpecial { Speaks = new() { Klovnish } }.AfterEquip(plain);
            SEntMan.TryGetComponent(plain, out plainSpeaker);
        });

        Assert.That(dwarfSpeaker, Is.Not.Null);
        Assert.That(plainSpeaker, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dwarfSpeaker!.Spoken, Does.Contain(Common));
            Assert.That(dwarfSpeaker.Spoken, Does.Contain(Dwarvish), "species languages must survive trait application");
            Assert.That(dwarfSpeaker.Spoken, Does.Contain(Vox), "the first language trait must apply");
            Assert.That(dwarfSpeaker.Spoken, Does.Contain(Klovnish), "the second language trait must stack with the first");
            Assert.That(dwarfSpeaker.CurrentLanguage, Is.EqualTo(Common));
            Assert.That(plainSpeaker!.Spoken, Does.Contain(Common), "first trait knowledge must not drop the implicit default");
            Assert.That(plainSpeaker.Spoken, Does.Contain(Klovnish));
        });
    }

    [Test]
    public async Task ChatWrapsCarryFontAsStringParameter()
    {
        var loc = Server.ResolveDependency<ILocalizationManager>();

        var sayWrap = loc.GetString("chat-manager-entity-say-wrap-message",
            ("entityName", "Urist"),
            ("verb", "says"),
            ("fontType", "DefaultItalic"),
            ("fontSize", 12),
            ("message", "honk"));
        var radioWrap = loc.GetString("chat-radio-message-wrap",
            ("color", Color.White),
            ("channel", "Common"),
            ("name", "Urist"),
            ("verb", "says"),
            ("fontType", "DefaultItalic"),
            ("fontSize", 12),
            ("message", "honk"));

        Assert.Multiple(() =>
        {
            // Unquoted font ids parse as color parameters; FontTag silently falls back to Default.
            Assert.That(GetFontParameter(sayWrap), Is.EqualTo("DefaultItalic"),
                "the say wrap must quote fontType so it survives as a string parameter");
            Assert.That(GetFontParameter(radioWrap), Is.EqualTo("DefaultItalic"),
                "the radio wrap must quote fontType so it survives as a string parameter");
        });
    }

    private static string? GetFontParameter(string markup)
    {
        Assert.That(FormattedMessage.TryFromMarkup(markup, out var message), $"wrap must stay parseable: {markup}");

        foreach (var node in message!.Nodes)
        {
            if (node is { Name: "font", Closing: false })
                return node.Value.StringValue;
        }

        return null;
    }
}
