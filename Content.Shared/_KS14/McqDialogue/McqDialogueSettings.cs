using Content.Shared.Whitelist;

namespace Content.Shared._KS14.McqDialogue;

/// <summary>
///     How a dialogue looks, and what it needs to stay open. Kept on the component of whatever asks the question, and
///         handed to <see cref="McqDialogueSystem.StartDialogue"/>, which applies it to the dialogue entity.
/// </summary>
[DataDefinition]
public sealed partial class McqDialogueSettings
{
    /// <summary>
    ///     Locale ID of the window's title, or null for the default.
    /// </summary>
    [DataField]
    public LocId? Title;

    /// <summary>
    ///     Locale ID of the question above the options, or null for the default.
    /// </summary>
    [DataField]
    public LocId? Prompt;

    /// <summary>
    ///     How far, in metres, the user can be from the source before the dialogue closes. Null for the UI's default,
    ///         2 m; zero or less for no limit.
    /// </summary>
    [DataField]
    public float? Range;

    /// <summary>
    ///     If set, the user has to be holding an item that passes this in their active hand when the dialogue opens,
    ///         and keep that item there, or it closes (<see cref="McqDialogueHeldItemComponent"/>).
    /// </summary>
    [DataField]
    public EntityWhitelist? HeldItemWhitelist;
}
