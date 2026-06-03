using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Server._KS14.GridSpawner;

/// <summary>
///     Spawns some grid at this entitys position, optionally
///         a random distance away from it.
/// </summary>
[RegisterComponent]
public sealed partial class KsGridSpawnerComponent : Component, ISerializationHooks
{
    /// <summary>
    ///     Path to the grid to load.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ResPath Path;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Angle Rotation = Angle.Zero;

    /// <summary>
    ///     If not null, the X coordinate will act as the minimum
    ///         and Y coordinate as maximum, for the random distance
    ///         that the spawned grid is from the spawner.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2? SpawnRange;

    void ISerializationHooks.AfterDeserialization()
    {
        if (SpawnRange is not { } spawnRange)
            return;

        if (spawnRange.X > spawnRange.Y)
            throw new ArgumentException("SpawnRange.X (minimum range) must not be higher than SpawnRange.Y (maximum range)!");

        if (spawnRange.Y < 0f)
            throw new ArgumentException("Neither component of SpawnRange may be negative!");
    }
}
