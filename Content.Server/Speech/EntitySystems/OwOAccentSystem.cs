using Content.Server.Speech.Components;
<<<<<<< HEAD
using Content.Shared.Speech;
=======
using Content.Shared.Speech.EntitySystems;
>>>>>>> upstream/master
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems;

public sealed partial class OwOAccentSystem : RelayAccentSystem<OwOAccentComponent>
{
    [Dependency] private IRobustRandom _random = default!;

    private static readonly IReadOnlyList<string> Faces = new List<string>{
            " (•`ω´•)", " ;;w;;", " owo", " UwU", " >w<", " ^w^"
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<string, string> SpecialWords = new Dictionary<string, string>()
        {
            { "you", "wu" },
        };

    public string Accentuate(string message)
    {
        foreach (var (word, repl) in SpecialWords)
        {
<<<<<<< HEAD
            SubscribeLocalEvent<OwOAccentComponent, AccentGetEvent>(OnAccent);
        }

        public string Accentuate(string message)
        {
            foreach (var (word, repl) in SpecialWords)
            {
                message = message.Replace(word, repl);
            }

            return message.Replace("!", _random.Pick(Faces))
                .Replace("r", "w").Replace("R", "W")
                .Replace("l", "w").Replace("L", "W");
        }

        private void OnAccent(EntityUid uid, OwOAccentComponent component, AccentGetEvent args)
        {
            args.Message = Accentuate(args.Message);
        }
=======
            message = message.Replace(word, repl);
        }

        return message.Replace("!", _random.Pick(Faces))
            .Replace("r", "w").Replace("R", "W")
            .Replace("l", "w").Replace("L", "W");
    }

    protected override string AccentuateInternal(EntityUid uid, OwOAccentComponent comp, string message)
    {
        return Accentuate(message);
>>>>>>> upstream/master
    }
}
