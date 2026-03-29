using System.Numerics;

namespace Content.Shared._KS14.Movement;

/// <summary>
///     Raised on a mover entity to update wish-dir if necessary.
/// </summary>
[ByRefEvent]
public record struct UpdateWishDirEvent(Vector2 WishDir);
