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
    [Dependency] private KsSoftCritSystem _softCrit = default!;
    [Dependency] private StutteringSystem _stuttering = default!;

    /// <summary>
    ///     Stutter settings, the same defaults bloodloss uses. Never added to an entity.
    /// </summary>
    private readonly StutteringAccentComponent _stutter = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsSoftCritComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(Entity<KsSoftCritComponent> ent, ref AccentGetEvent args)
    {
        if (!ent.Comp.Active)
            return;

        var severity = _softCrit.GetSoftCritSeverity(ent);
        var dropChance = MathHelper.Lerp(ent.Comp.MinLetterDropChance, ent.Comp.MaxLetterDropChance, severity);

        args.Message = _stuttering.Accentuate(DropLetters(args.Message, dropChance), _stutter);
    }

    /// <summary>
    ///     Randomly swallows letters. The first letter of every word is always kept so there's something to go on.
    /// </summary>
    private string DropLetters(string message, float chance)
    {
        var sb = new StringBuilder(message.Length);
        var wordStart = true;

        foreach (var character in message)
        {
            if (!char.IsLetter(character))
            {
                wordStart = true;
                sb.Append(character);
                continue;
            }

            if (wordStart || !_random.Prob(chance))
                sb.Append(character);

            wordStart = false;
        }

        return sb.ToString();
    }
}
