namespace Content.Shared._KS14.Structures;

/// <summary>
///     Marks a structure that is drawn as a line along its own axis, with an end post only where no anchored structure
///         of the same <see cref="Key"/> carries the line on. The client's <c>KsLineSmoothSystem</c> shows the
///         <c>wire</c> layer always and the <c>postA</c> / <c>postB</c> layers at the ends that need them.
/// </summary>
[RegisterComponent]
public sealed partial class KsLineSmoothComponent : Component
{
    /// <summary>
    ///     Structures join only with others of the same key.
    /// </summary>
    [DataField(required: true)]
    public string Key = string.Empty;
}
