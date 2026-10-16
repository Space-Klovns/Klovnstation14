namespace Content.Shared.GameTicking.Prototypes;

public sealed partial class LobbyBackgroundPrototype
{
    /// <summary>
    ///     Whether the background can be rolled at random. When false, it is shown only while a map that
    ///     forces it is selected (<see cref="Maps.GameMapPrototype.ForcedLobbyBackground"/>).
    /// </summary>
    [DataField]
    public bool RandomlySelectable = true;
}
