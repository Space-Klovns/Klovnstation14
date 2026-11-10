using System.Text;
using Content.Server.Speech.EntitySystems;
using Content.Shared._KS14.Mobs.Components;
using Content.Shared._KS14.Mobs.Systems;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Robust.Shared.Random;

namespace Content.Server._KS14.Mobs;

/// <summary>
///     Makes soft crit mobs hard to understand: they stutter like they do from bloodloss and swallow letters,
///         the latter getting worse the closer they are to hard crit.
/// </summary>
public sealed partial class KsSoftCritSpeechSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private KsSoftCritSystem _softCritSystem = default!;
    [Dependency] private StutteringSystem _stutteringSystem = default!;

    /// <summary>
    ///     Stutter settings, the same defaults bloodloss uses. Never added to an entity.
    /// </summary>
    private readonly StutteringAccentComponent _stutteringAccentComponent = new();

    [SubscribeLocalEvent]
    private void OnAccent(Entity<KsSoftCritComponent> entity, ref AccentGetEvent args)
    {
        if (!entity.Comp.Active)
            return;

        var severity = _softCritSystem.GetSoftCritSeverity(entity.AsNullable());
        var dropChance = MathHelper.Lerp(entity.Comp.MinLetterDropChance, entity.Comp.MaxLetterDropChance, severity);

        args.Message = _stutteringSystem.Accentuate(DropLetters(args.Message, dropChance), _stutteringAccentComponent);
    }

    /// <summary>
    ///     Randomly swallows letters. The first letter of every word is always kept so there's something to go on.
    /// </summary>
    private string DropLetters(string message, float chance)
    {
        var builder = new StringBuilder(message.Length);
        var wordStart = true;

        foreach (var character in message)
        {
            if (!char.IsLetter(character))
            {
                wordStart = true;
                builder.Append(character);
                continue;
            }

            if (wordStart || !_random.Prob(chance))
                builder.Append(character);

            wordStart = false;
        }

        return builder.ToString();
    }
}
