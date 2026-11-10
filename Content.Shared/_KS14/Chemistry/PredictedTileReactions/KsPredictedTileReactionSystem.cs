using Robust.Shared.Network;

namespace Content.Shared._KS14.Chemistry.PredictedTileReactions;

/// <summary>
///     Where tile reactions run. The server runs every reaction a reagent lists. The client has only the reactions that
///         have a client-side class (see <see cref="KsTileReactionListSerializer"/>), and those act only inside a scope
///         that this client's own predicted vapor opens (<see cref="BeginScope"/>).
/// </summary>
/// <remarks>
///     The scope is what keeps every other caller of tile reactions on the client as it was before the client had
///         any: a mop's absorbent calls them from predicted code that is re-run on every re-predicted tick, where a
///         reaction that spawned something would spawn it again each time. A kept vapor is stepped once per real tick
///         (see <c>KsSharedPredictedSpawnSystem</c>), so what it does happens once.
/// </remarks>
public sealed partial class KsPredictedTileReactionSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;

    private int _scopeDepth;

    /// <summary>
    ///     Whether a vapor's tile reactions are running. The shared vapor code opens the scope on the server too, where
    ///         it changes nothing.
    /// </summary>
    public bool InScope => _scopeDepth > 0;

    /// <summary>
    ///     Whether a tile reaction acts here: always on the server, and on the client only inside a scope.
    /// </summary>
    public bool ShouldReact => _netManager.IsServer || InScope;

    /// <summary>
    ///     Whether what a tile reaction does here is the client's prediction of the server's, to be replaced by it.
    /// </summary>
    public bool IsPredicting => _netManager.IsClient;

    /// <summary>
    ///     Opens a scope in which the client's tile reactions act. Only the shared vapor system opens one, and only for
    ///         this client's kept copy of a vapor.
    /// </summary>
    public Scope BeginScope()
    {
        _scopeDepth++;
        return new Scope(this);
    }

    private void EndScope()
    {
        _scopeDepth--;
    }

    /// <summary>
    ///     Closes the scope when disposed. See <see cref="BeginScope"/>.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly KsPredictedTileReactionSystem _system;

        internal Scope(KsPredictedTileReactionSystem system)
        {
            _system = system;
        }

        public void Dispose()
        {
            _system.EndScope();
        }
    }
}
