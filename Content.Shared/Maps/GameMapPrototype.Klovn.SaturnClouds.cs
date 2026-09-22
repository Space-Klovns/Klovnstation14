// KS14: added in this fork
using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Shared.Maps;

public sealed partial class GameMapPrototype
{
    /// <summary>
    ///     A complete game-map prototype for this station's optional Saturn-cloud version.
    ///     The referenced prototype supplies its own map path and station/job configuration.
    /// </summary>
    [DataField]
    public ProtoId<GameMapPrototype>? KsSaturnCloudMap { get; private set; }

    /// <summary>
    ///     The orbital game-map prototype paired with a Saturn-cloud map.
    ///     A cloud variation is only eligible when both references resolve.
    /// </summary>
    [DataField]
    public ProtoId<GameMapPrototype>? KsSaturnOrbitalMap { get; private set; }

    /// <summary>
    ///     Dungeon used for each asteroid generated around the orbital station.
    /// </summary>
    [DataField]
    public ProtoId<DungeonConfigPrototype>? KsSaturnAsteroidDungeon { get; private set; }

    [DataField]
    public int KsSaturnAsteroidCount { get; private set; }

    [DataField]
    public float KsSaturnAsteroidMinimumDistance { get; private set; }

    [DataField]
    public float KsSaturnAsteroidMaximumDistance { get; private set; }

    /// <summary>
    ///     Runtime-only markers used after the selected pair has been cloned for loading.
    /// </summary>
    public bool KsIsSaturnCloudVariant { get; private set; }
    public bool KsIsSaturnOrbitalVariant { get; private set; }

    public GameMapPrototype KsCreateSaturnRuntimeVariant(bool orbital)
    {
#pragma warning disable RA0039
        return new GameMapPrototype
        {
            ID = ID,
            MapName = MapName,
            MapPath = MapPath,
            MaxRandomOffset = MaxRandomOffset,
            IsGrid = IsGrid,
            RandomRotation = RandomRotation,
            Fallback = Fallback,
            MinPlayers = MinPlayers,
            MaxPlayers = MaxPlayers,
            _conditions = _conditions,
            _stations = _stations,
            KsSaturnCloudMap = KsSaturnCloudMap,
            KsSaturnOrbitalMap = KsSaturnOrbitalMap,
            KsSaturnAsteroidDungeon = KsSaturnAsteroidDungeon,
            KsSaturnAsteroidCount = KsSaturnAsteroidCount,
            KsSaturnAsteroidMinimumDistance = KsSaturnAsteroidMinimumDistance,
            KsSaturnAsteroidMaximumDistance = KsSaturnAsteroidMaximumDistance,
            KsIsSaturnCloudVariant = !orbital,
            KsIsSaturnOrbitalVariant = orbital,
        };
#pragma warning restore RA0039
    }
}
