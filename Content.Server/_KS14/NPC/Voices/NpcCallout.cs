namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     Something an NPC may call out that only some voices say at all: each voice set opts in by naming a line set for it
///         (<see cref="NpcVoiceSetPrototype.Callouts"/>), and an NPC whose voice set names none says nothing. Spoken from a
///         system with <see cref="NpcVoiceSystem.TrySpeakCallout"/>, or from HTN with <c>VoiceCalloutSpeakOperatorSpeech</c>.
/// </summary>
public enum NpcCallout : byte
{
    /// <summary>
    ///     It throws a grenade that blows up.
    /// </summary>
    ThrowFrag,

    /// <summary>
    ///     It throws anything else: a flashbang, smoke.
    /// </summary>
    ThrowOther,

    /// <summary>
    ///     A squadmate it saw go down, with no callsign to name it by.
    /// </summary>
    ManDown,

    /// <summary>
    ///     It opens up on where a hostile went to keep its head down.
    /// </summary>
    Suppressing,

    /// <summary>
    ///     It sends the squad forward.
    /// </summary>
    Push,

    /// <summary>
    ///     It sends squadmates round the hostile's side.
    /// </summary>
    Flank,
}
