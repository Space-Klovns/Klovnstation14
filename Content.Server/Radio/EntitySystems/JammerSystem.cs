using Content.Shared.Radio.EntitySystems;
using Content.Shared.Radio.Components;
using Content.Shared.Chat; // KS14
using Robust.Shared.Random; // KS14

namespace Content.Server.Radio.EntitySystems;

public sealed class JammerSystem : SharedJammerSystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!; // KS14
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly char[] PossibleGarbleCharacters = ['#', '*', '^', '-'];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    // KS14
    private string GarbleString(string message, float chance)
    {
        var characters = message.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (!_robustRandom.Prob(chance))
                continue;

            characters[i] = PossibleGarbleCharacters[_robustRandom.Next(PossibleGarbleCharacters.Length)];
        }

        return new string(characters);
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent args)
    {
        if (ShouldCancel(args.RadioSource, args.Channel.Frequency, out var radioJammerComponent /* KS14 */))
        {
            if (radioJammerComponent!.OnlyGarbleReceivedMessages) // KS14
                return;

            args.Cancelled = true;
        }
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (ShouldCancel(args.RadioReceiver, args.Channel.Frequency, out var radioJammerComponent /* KS14 */))
        {
            // KS14 start
            if (radioJammerComponent is { } &&
                radioJammerComponent.OnlyGarbleReceivedMessages)
            {
                var oldMessage = args.OriginalChatMessage.Message.Message;
                var garbledMessage = GarbleString(oldMessage, radioJammerComponent.GarbleStrength);

                var newWrappedMessage = Loc.GetString("chat-radio-message-wrap",
                    ("color", args.Channel.Color),
                    ("fontType", "Default"),
                    ("fontSize", 12),
                    ("verb", Loc.GetString("chat-speech-verb-default")),
                    ("channel", $"\\[{args.Channel.LocalizedName}\\]"),
                    ("name", "unknown interference"),
                    ("message", garbledMessage));

                var newChat = new ChatMessage(
                    ChatChannel.Radio,
                    garbledMessage,
                    newWrappedMessage,
                    NetEntity.Invalid,
                    null);

                args.NewChatMessage = new MsgChatMessage { Message = newChat };

                return;
            }
            // KS14 end

            args.Cancelled = true;
        }
    }

    private bool ShouldCancel(EntityUid sourceUid, int frequency, out RadioJammerComponent? radioJammerComponent /* KS14 */)
    {
        var source = Transform(sourceUid).Coordinates;
        var query = EntityQueryEnumerator<ActiveRadioJammerComponent, RadioJammerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var jam, out var transform))
        {
            // Check if this jammer excludes the frequency
            if (jam.FrequenciesExcluded.Contains(frequency))
                continue;

            if (_transform.InRange(source, transform.Coordinates, GetCurrentRange((uid, jam))))
            {
                radioJammerComponent = jam; // KS14
                return true;
            }
        }

        radioJammerComponent = null; // KS14
        return false;
    }
}
