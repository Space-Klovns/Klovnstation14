namespace Content.Shared._KS14.Decals;

/// <summary>
///     Puts a random decal with <see cref="Tag"/> on the tile this entity spawns on, as a full tile fire leaves a burn
///         mark, unless the tile already has <see cref="MaxDecals"/> of them.
/// </summary>
[RegisterComponent]
public sealed partial class KsDecalOnSpawnComponent : Component
{
    /// <summary>
    ///     The tag the decal is picked from, among every decal prototype that has it.
    /// </summary>
    [DataField]
    public string Tag = KsDecalTags.Burnt;

    /// <summary>
    ///     No decal is added to a tile that already has this many decals with <see cref="Tag"/>.
    /// </summary>
    [DataField]
    public int MaxDecals = KsDecalTags.DefaultMaxDecals;
}

public static class KsDecalTags
{
    /// <summary>
    ///     The scorch marks fires leave.
    /// </summary>
    public const string Burnt = "burnt";

    /// <summary>
    ///     How many marks of one tag a tile takes before no more are added, as many as a hotspot leaves.
    /// </summary>
    public const int DefaultMaxDecals = 4;
}
