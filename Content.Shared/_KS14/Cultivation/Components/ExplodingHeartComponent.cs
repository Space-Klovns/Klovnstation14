using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Five points of demonic qi lodged around the heart, counting the victim's steps. When the count runs out the
///         heart bursts. Standing still is safe; Acupoint Sealing or two cycles of meditation dissolve it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExplodingHeartComponent : Component
{
    /// <summary>
    ///     The alert that counts down the victim's steps.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> Alert = "KsExplodingHeart";

    [DataField, AutoNetworkedField]
    public int StepsLeft = 5;

    /// <summary>
    ///     Whoever struck the points. The victim always learns the name.
    /// </summary>
    [DataField]
    public EntityUid? Caster;

    [DataField]
    public string CasterName = string.Empty;

    [DataField]
    public int CalmCycles;

    /// <summary>
    ///     The last tile a step was counted from.
    /// </summary>
    [DataField]
    public Vector2i LastTile;
}
