using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Maps;

public sealed partial class GameMapPrototype
{
    /// <summary>
    ///     The lobby background shown to everyone while this map is selected, in place of a random one.
    ///     When the selection moves to a map without one, the background is rolled again.
    /// </summary>
    [DataField]
    public ProtoId<LobbyBackgroundPrototype>? ForcedLobbyBackground;
}
