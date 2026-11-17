namespace Content.Shared.RCD.Components;

public sealed partial class RCDComponent
{
    /// <summary>
    ///     Builds with no delay, and takes apart what the same user built with it with no delay either. Anything
    ///         else still takes as long to deconstruct as it normally would, so the tool can't erase a base on its own.
    /// </summary>
    [DataField]
    public bool KsInstant;
}
