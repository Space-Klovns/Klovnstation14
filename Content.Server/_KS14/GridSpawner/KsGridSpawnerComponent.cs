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
    ///         and Y coordinate as maximum, for the random angle
    ///         that the spawned grid is rotated at. This is added onto <see cref="Rotation"/>
    ///         because i didn't feel like obsoleting it.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2d? RotationRangeDeg = null;

    /// <summary>
    ///     If not null, the X coordinate will act as the minimum
    ///         and Y coordinate as maximum, for the random distance
    ///         that the spawned grid is from the spawner.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2? SpawnRange;

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? SpawnedGridUid = null;

    void ISerializationHooks.AfterDeserialization()
    {
        if (SpawnRange is { } spawnRange)
        {
            if (spawnRange.X > spawnRange.Y)
                throw new ArgumentException("SpawnRange.X (minimum range) must not be higher than SpawnRange.Y (maximum range)!");

            if (spawnRange.Y < 0f)
                throw new ArgumentException("Neither component of SpawnRange may be negative!");
        }

        if (RotationRangeDeg is { } rotationRange)
        {
            if (rotationRange.X > rotationRange.Y)
                throw new ArgumentException("RotationRangeDeg.X (minimum range) must not be higher than RotationRangeDeg.Y (maximum range)!");

            if (rotationRange.Y < 0f)
                throw new ArgumentException("Neither component of RotationRangeDeg may be negative!");
        }
    }
}
