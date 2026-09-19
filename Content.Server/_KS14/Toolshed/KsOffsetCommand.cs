using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._KS14.Toolshed;

/// <summary>
///     Moves every piped entity by a <see cref="Vector2"/>, leaving its parent alone.
/// </summary>
/// <remarks>
///     <c>local</c> offsets in the entity's parent's frame, <c>world</c> in map space -
///     they only differ for entities parented to something rotated or moving, like a grid.
/// </remarks>
[ToolshedCommand, AdminCommand(AdminFlags.Mapping)]
public sealed partial class KsOffsetCommand : ToolshedCommand
{
    private SharedTransformSystem? _transformSystem;

    [CommandImplementation("local")]
    public EntityUid OffsetLocal(IInvocationContext context, [PipedArgument] EntityUid entityUid, Vector2 offset)
        => Offset(context, entityUid, offset, worldSpace: false);

    [CommandImplementation("local")]
    public IEnumerable<EntityUid> OffsetLocal(IInvocationContext context, [PipedArgument] IEnumerable<EntityUid> input, Vector2 offset)
        => input.Select(entityUid => Offset(context, entityUid, offset, worldSpace: false));

    [CommandImplementation("world")]
    public EntityUid OffsetWorld(IInvocationContext context, [PipedArgument] EntityUid entityUid, Vector2 offset)
        => Offset(context, entityUid, offset, worldSpace: true);

    [CommandImplementation("world")]
    public IEnumerable<EntityUid> OffsetWorld(IInvocationContext context, [PipedArgument] IEnumerable<EntityUid> input, Vector2 offset)
        => input.Select(entityUid => Offset(context, entityUid, offset, worldSpace: true));

    /// <remarks>
    ///     Anchored entities drop position writes on the floor - <see cref="TransformComponent.LocalPosition"/>'s setter
    ///     returns early - so they have to be unanchored for the move and re-anchored onto whatever tile they land on.
    ///     Landing somewhere without a tile leaves the entity unanchored where it was moved to, which is reported.
    /// </remarks>
    private EntityUid Offset(IInvocationContext context, EntityUid entityUid, Vector2 offset, bool worldSpace)
    {
        _transformSystem ??= GetSys<SharedTransformSystem>();

        var transformComponent = Transform(entityUid);
        var wasAnchored = transformComponent.Anchored;

        if (wasAnchored)
            _transformSystem.Unanchor(entityUid, transformComponent);

        if (worldSpace)
        {
            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);
            _transformSystem.SetWorldPosition((entityUid, transformComponent), worldPosition + offset);
        }
        else
        {
            _transformSystem.SetLocalPosition(entityUid, transformComponent.LocalPosition + offset, transformComponent);
        }

        if (wasAnchored && !_transformSystem.AnchorEntity((entityUid, transformComponent)))
            context.WriteLine($"Could not re-anchor {EntityManager.ToPrettyString(entityUid)}, it is now unanchored at {transformComponent.Coordinates}.");

        return entityUid;
    }
}
