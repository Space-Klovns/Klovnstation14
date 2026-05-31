using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._KS14.LobbyView;

public abstract class KsSharedLobbyViewSystem : EntitySystem
{
    /// <summary>
    ///     Gets the best lobby view based on priority.
    ///         Works to, if <see cref="KsLobbyViewComponent.Priority"/>
    ///         has been networked properly, always get the same
    ///         entity on both the client and server, assuming
    ///         the best client entity is unpaused (in PVS range).
    /// </summary>
    protected bool TryGetBestLobbyView([NotNullWhen(true)] out Entity<KsLobbyViewComponent, EyeComponent>? entity)
    {
        var eqe = EntityQueryEnumerator<KsLobbyViewComponent, EyeComponent>();
        (Entity<KsLobbyViewComponent, EyeComponent>? Entity, int Priority) best = (null, int.MinValue);

        while (eqe.MoveNext(out var uid, out var viewComponent, out var eyeComponent))
        {
            if (viewComponent.Priority < best.Priority ||
                Terminating(uid))
                continue;

            best = ((uid, viewComponent, eyeComponent), viewComponent.Priority);
        }

        entity = best.Entity;
        return entity != null;
    }
}
