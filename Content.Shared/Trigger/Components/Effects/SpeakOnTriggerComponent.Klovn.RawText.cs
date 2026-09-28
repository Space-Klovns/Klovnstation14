namespace Content.Shared.Trigger.Components.Effects;

public sealed partial class SpeakOnTriggerComponent
{
    /// <summary>
    ///     The text to speak, but not locale. This has priority over Text.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? NonLocText;

    [DataField, AutoNetworkedField]
    public bool HideChat = true;
}
