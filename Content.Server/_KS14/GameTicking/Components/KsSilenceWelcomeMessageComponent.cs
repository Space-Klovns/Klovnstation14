namespace Content.Server._KS14.GameTicking.Components;

/// <summary>
///     When added to a station, crew joining will be silent; i.e.,
///         there will be no latejoin announcement, etc..
/// </summary>
[RegisterComponent]
public sealed partial class KsSilenceWelcomeMessageComponent : Component;
