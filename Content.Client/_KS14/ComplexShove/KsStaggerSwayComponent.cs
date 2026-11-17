using System.Numerics;

namespace Content.Client._KS14.ComplexShove;

/// <summary>
///     On someone staggered, while their sprite sways: what the sway is, and what to put back when it ends.
/// </summary>
[RegisterComponent, Access(typeof(KsStaggerVisualsSystem))]
public sealed partial class KsStaggerSwayComponent : Component
{
    /// <summary>
    ///     Their sprite's offset before it started swaying.
    /// </summary>
    [ViewVariables]
    public Vector2 BaseOffset;

    [ViewVariables]
    public float Amplitude;

    [ViewVariables]
    public TimeSpan Period;
}
