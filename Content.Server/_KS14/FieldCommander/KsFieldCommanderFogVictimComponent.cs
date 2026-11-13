namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     On a defender who has been warned about the fog: when they may next be warned.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class KsFieldCommanderFogVictimComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan NextPopup;
}
