#nullable enable
using System.Collections;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server.Voting;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voting;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using ClientVoteManager = Content.Client.Voting.VoteManager;

namespace Content.IntegrationTests.Tests._KS14.Localization;

/// <summary>
///     A vote's text made with <see cref="KsVoteText.Deferred"/> leaves the server as a localisation id and is turned
///         into text by each client, in the client's own language. Plain text, a custom vote's, is left alone.
/// </summary>
[TestOf(typeof(KsVoteText))]
public sealed class KsVoteLocalizationTests : GameTest
{
    private const string PlainOption = "Urist's own words";

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase("en-US")]
    [TestCase("ru-RU")]
    public async Task DeferredVoteTextIsResolvedInTheClientsLanguage(string culture)
    {
        var titleId = KsFieldCommanderRuleSystem.EndlessVoteTitle;
        var serverTitle = string.Empty;
        IVoteHandle? vote = null;

        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, culture));

        await Server.WaitPost(() =>
        {
            serverTitle = Server.ResolveDependency<ILocalizationManager>().GetString(titleId);

            var options = new VoteOptions
            {
                Title = KsVoteText.Deferred(titleId),
                InitiatorText = KsVoteText.Deferred(titleId),
                Duration = System.TimeSpan.FromMinutes(5),
                Options =
                {
                    (KsVoteText.Deferred(titleId), true),
                    (PlainOption, false),
                },
            };

            vote = Server.ResolveDependency<Content.Server.Voting.Managers.IVoteManager>().CreateVote(options);
            Assert.That(KsVoteText.IsDeferred(vote.Title), Is.True, "the server should pass the id on, not format it in its own language");
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var expected = Client.ResolveDependency<ILocalizationManager>().GetString(titleId);
            if (culture != "en-US")
                Assert.That(expected, Is.Not.EqualTo(serverTitle), "the test needs the two languages to differ");

            // The client's vote manager keeps its votes to itself and its popups, so they are read off its field.
            var voteManager = (ClientVoteManager)Client.ResolveDependency<Content.Client.Voting.IVoteManager>();
            var votesField = typeof(ClientVoteManager).GetField("_votes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(votesField, Is.Not.Null, "the client vote manager no longer keeps its votes in _votes: read them some other way");
            var votes = ((IDictionary)votesField!.GetValue(voteManager)!).Values.Cast<ClientVoteManager.ActiveVote>().ToList();
            Assert.That(votes, Has.Count.EqualTo(1), "the vote should have reached the client");

            var clientVote = votes[0];
            Assert.Multiple(() =>
            {
                Assert.That(clientVote.Title, Is.EqualTo(expected), "the title, in the client's language");
                Assert.That(clientVote.Initiator, Is.EqualTo(expected), "the initiator, in the client's language");
                Assert.That(clientVote.Entries[0].Text, Is.EqualTo(expected), "an option, in the client's language");
                Assert.That(clientVote.Entries[1].Text, Is.EqualTo(PlainOption), "plain text is shown as it was written");
            });
        });

        await Server.WaitPost(() => vote!.Cancel());
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "en-US"));
    }
}
