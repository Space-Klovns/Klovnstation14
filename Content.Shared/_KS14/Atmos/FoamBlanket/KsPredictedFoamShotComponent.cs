using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Atmos.FoamBlanket;

/// <summary>
///     Marks a gun shot whose client-predicted copy lays foam blankets along its path for the shooter, ahead of the
///         server's vapor laying the real ones. Does nothing on the server, and on the client does nothing for a shot
///         that is not the shooter's own kept copy (a user-mode predicted spawn, see
///         <c>KsSharedPredictedSpawnSystem</c>).
/// </summary>
/// <remarks>
///     The shot's own <c>Vapor</c> is what the server lays blankets with, and has no solution on the client, so this
///         repeats what it would do: offer each new tile a fraction of the foam, at least a minimum, and keep what
///         the blanket did not take for the next tile.
/// </remarks>
[RegisterComponent]
public sealed partial class KsPredictedFoamShotComponent : Component
{
    /// <summary>
    ///     The blanket laid on each tile.
    /// </summary>
    [DataField]
    public EntProtoId BlanketPrototype = "KsFoamBlanket";

    /// <summary>
    ///     The reagent of the gun's solution that becomes foam.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> FoamReagent = "FirefightingFoam";

    /// <summary>
    ///     Fraction of the foam still carried that each new tile is offered. Must match the shot's
    ///         <c>Vapor.transferAmountPercentage</c>.
    /// </summary>
    [DataField]
    public float TransferFraction = 0.5f;

    /// <summary>
    ///     Least units of foam a tile is offered. Must match <c>VaporComponent.MinimumTransferAmount</c>'s default.
    /// </summary>
    [DataField]
    public float MinimumTransfer = 0.05f;

    /// <summary>
    ///     Units of foam the shot still carries. Runtime state of the predicted copy.
    /// </summary>
    [ViewVariables]
    public float Foam;

    /// <summary>
    ///     Whether <see cref="Foam"/> has been worked out from the firing gun yet.
    /// </summary>
    [ViewVariables]
    public KsPredictedFoamShotState State;

    /// <summary>
    ///     The last tile the shot was offered to, so that it reacts once per tile.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastGridUid;

    /// <inheritdoc cref="LastGridUid"/>
    [ViewVariables]
    public Vector2i LastTile;
}

public enum KsPredictedFoamShotState : byte
{
    /// <summary>
    ///     The firing gun is not known yet; it is set just after the shot spawns.
    /// </summary>
    Waiting,

    /// <summary>
    ///     Carrying foam, laying blankets.
    /// </summary>
    Laying,

    /// <summary>
    ///     Nothing more to lay: the gun has no foam, or none is left.
    /// </summary>
    Done,
}
