using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     On a status effect: whoever has it sways from side to side for as long as it lasts, as tgstation's staggered
///         mobs do. It tells everyone watching, the shover above all, that a shove against a wall will now put them
///         down. Purely visual, and drawn by the client.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsStaggerVisualsComponent : Component
{
    /// <summary>
    ///     How far the sprite swings to each side, in tiles.
    /// </summary>
    [DataField]
    public float Amplitude = 0.08f;

    /// <summary>
    ///     How long one swing to a side and back to the other takes.
    /// </summary>
    [DataField]
    public TimeSpan Period = TimeSpan.FromSeconds(0.35);
}
