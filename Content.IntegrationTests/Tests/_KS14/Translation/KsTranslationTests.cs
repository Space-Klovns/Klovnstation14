using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Translation;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;

namespace Content.IntegrationTests.Tests._KS14.Translation;

/// <summary>
///     Integration tests for <see cref="KsTranslationSystem"/> using a <see cref="FakeKsTranslator"/>
///     swapped in for the real DeepL backend. Drives the per-reader translation core directly with the
///     pooled client's channel as the reader and an explicit speaker language, which is deterministic and
///     needs no live network. The client-side swap/marker rendering is covered by manual smoke testing.
/// </summary>
public sealed class KsTranslationTests : GameTest
{
    // We mutate the live server system (swap the translator), so the pooled server must not be recycled.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task DisabledByDefault()
    {
        var began = true;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            began = sys.TryBeginSession(ChatChannel.OOC, "hello", ServerSession!, out _);
        });

        Assert.That(began, Is.False, "translation must be off by default (no key, disabled)");
    }

    [Test]
    public async Task SkipsSameBaseLanguage()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "EN-GB");
        await Pair.RunTicksSync(10);

        int? id = 0;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            id = sys.TryReader("skip-msg", ctx, ServerSession!.Channel);
        });
        await Pair.RunTicksSync(5);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.Null, "an EN speaker to an EN-GB reader shares base EN and must not translate");
            Assert.That(fake.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task TranslatesCrossLanguage()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        int? id = null;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            id = sys.TryReader("cross-msg", ctx, ServerSession!.Channel);
        });
        await Pair.RunTicksSync(10);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.Not.Null, "a cross-language reader must be stamped with a message id");
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].Text, Is.EqualTo("cross-msg"));
            Assert.That(fake.Requests[0].Source, Is.EqualTo("EN"));
            Assert.That(fake.Requests[0].Target, Is.EqualTo("DE"));
        });
    }

    [Test]
    public async Task CachesRepeatedMessage()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            sys.TryReader("cache-msg", ctx, ServerSession!.Channel);
        });
        await Pair.RunTicksSync(10); // let the first call complete and populate the cache

        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            sys.TryReader("cache-msg", ctx, ServerSession!.Channel);
        });
        await Pair.RunTicksSync(10);

        Assert.That(fake.Requests, Has.Count.EqualTo(1), "an identical repeat message must be served from cache");
    }

    [Test]
    public async Task DedupCollapsesConcurrentReaders()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeKsTranslator { Gate = gate.Task };
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            sys.TryReader("dedup-msg", ctx, ServerSession!.Channel);
            sys.TryReader("dedup-msg", ctx, ServerSession!.Channel); // same (src,tgt,text): attaches to the in-flight call
        });
        await Pair.RunTicksSync(3);

        Assert.That(fake.Requests, Has.Count.EqualTo(1), "concurrent duplicate readers must collapse to one call");

        gate.SetResult();
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task CooldownBlocksRapidMessages()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        var blocked = false;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;

            // A real call for a DE reader, then apply the per-speaker cooldown.
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 5 };
            sys.TryReader("cool-msg", ctx, ServerSession!.Channel);
            sys.EndMessage(ctx);

            // The same speaker's immediate follow-up must be gated by the cooldown.
            blocked = !sys.TryBeginSession(ChatChannel.OOC, "again", ServerSession!, out _);
        });
        await Pair.RunTicksSync(5);

        Assert.That(blocked, Is.True, "a rapid follow-up from the same speaker must be blocked by the cooldown");
    }

    [Test]
    public async Task CooldownAllowsSameTextCompanion()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        var began = false;
        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;

            // A local say starts a real call and applies the per-speaker cooldown...
            var ctx = new KsTranslationContext { SpeakerBase = "EN", Speaker = ServerSession!.UserId, Length = 9, Text = "relay-msg" };
            sys.TryReader("relay-msg", ctx, ServerSession!.Channel);
            sys.EndMessage(ctx);

            // ...but the same utterance's radio copy carries identical text, so it must NOT be throttled.
            began = sys.TryBeginSession(ChatChannel.Radio, "relay-msg", ServerSession!, out _);
        });
        await Pair.RunTicksSync(5);

        Assert.That(began, Is.True, "the same-text radio copy of a say must bypass the per-speaker cooldown");
    }

    [Test]
    public async Task ContextThreadsToTranslator()
    {
        var fake = new FakeKsTranslator();
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "DE");
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            var ctx = new KsTranslationContext
            {
                SpeakerBase = "EN",
                Speaker = ServerSession!.UserId,
                Length = 12,
                Context = "setting-hint\nprior line",
            };
            sys.TryReader("ctxthread-msg", ctx, ServerSession!.Channel);
        });
        await Pair.RunTicksSync(10);

        Assert.Multiple(() =>
        {
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].Context, Is.EqualTo("setting-hint\nprior line"),
                "the per-message context must reach the translator");
        });
    }

    [Test]
    public async Task GlossaryCollectedFromPrototypes()
    {
        var fake = new FakeKsTranslator();

        await Server.WaitPost(() =>
        {
            var sys = Server.System<KsTranslationSystem>();
            sys.Translator = fake;
            sys.RebuildGlossary();
        });
        await Pair.RunTicksSync(5);

        Assert.That(fake.Glossary, Is.Not.Empty, "glossary prototypes must be collected and handed to the translator");
        Assert.That(fake.Glossary.Any(d => d.Source == "EN" && d.Target == "DE" && d.Entries.ContainsKey("honk")),
            Is.True, "the EN->DE jargon dictionary must be present");
    }
}
