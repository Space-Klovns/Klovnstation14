using Content.Shared.Radio.EntitySystems;
using Content.Shared.Radio.Components;
using Content.Shared.Chat; // KS14
using Content.Shared.Radio; // KS14
using Robust.Shared.Random; // KS14

namespace Content.Server.Radio.EntitySystems;

public sealed partial class JammerSystem : SharedJammerSystem
{
    // KS14 Start
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly char[] PossibleGarbleCharacters = ['#', '*', '^', '-'];
    // KS14 End

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    // KS14 start
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

    private MsgChatMessage BuildGarbleSubstitute(RadioChannelPrototype channel, string garbledMessage)
    {
        var wrappedMessage = Loc.GetString("chat-radio-message-wrap",
            ("color", channel.Color),
            ("fontType", "Default"),
            ("fontSize", 12),
            ("verb", Loc.GetString("chat-speech-verb-default")),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", "unknown interference"),
            ("message", garbledMessage));

        return new MsgChatMessage
        {
            Message = new ChatMessage(
                ChatChannel.Radio,
                garbledMessage,
                wrappedMessage,
                NetEntity.Invalid,
                null),
        };
    }
    // KS14 end

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
                args.NewChatMessage = BuildGarbleSubstitute(args.Channel, GarbleString(oldMessage, radioJammerComponent.GarbleStrength));

                // Non-understanders never get clear-derived text; garble the scrambled clone.
                if (args.KsObfuscatedMessage is { } scrambled)
                    args.KsNewObfuscatedChatMessage = BuildGarbleSubstitute(args.Channel, GarbleString(scrambled, radioJammerComponent.GarbleStrength));

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
