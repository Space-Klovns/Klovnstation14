namespace Content.Shared.Materials.OreSilo;

public sealed partial class OreSiloBuiState
{
    /// <summary>
    ///     The listed machines linked to the silo. The client can't tell this from the machines themselves: one out of its
    ///         sight sends it nothing, and one in sight can have its state applied after this one.
    /// </summary>
    public HashSet<NetEntity> KsLinked = new();
}
