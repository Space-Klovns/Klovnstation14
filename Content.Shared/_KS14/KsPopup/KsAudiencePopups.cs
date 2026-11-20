namespace Content.Shared._KS14.KsPopup;

/// <summary>
///     The three messages of something one entity does to another: one for whoever acts, one for whoever it is done
///         to, and one for everyone else who sees it. Null shows nothing to that audience.
///     Shown with <see cref="KsPopupSystem.PopupUserTargetOthers"/>.
/// </summary>
/// <remarks>
///     As a <c>[DataField]</c> the YAML linter checks each id against the locale files. YAML replaces the whole set:
///         an audience left out of the mapping gets no message, not the field's default one.
/// </remarks>
[DataDefinition]
public partial struct KsAudiencePopups
{
    [DataField]
    public LocId? User;

    [DataField]
    public LocId? Target;

    [DataField]
    public LocId? Others;

    public KsAudiencePopups(LocId? user, LocId? target, LocId? others)
    {
        User = user;
        Target = target;
        Others = others;
    }
}
