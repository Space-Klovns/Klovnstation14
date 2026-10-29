namespace Content.Server._KS14.NPC.Firefighting;

/// <summary>
///     An NPC that fights chemical fires with its own <see cref="Content.Shared.Fluids.Components.SprayComponent"/>, and
///         keeps watch over a spill until it is out for good. A puddle that still burns hot relights a few seconds after
///         its flames go out, so the NPC remembers every tile it has seen burn and keeps the foam on it topped up until
///         the puddle under it has cooled.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsNpcFirefightingSystem))]
public sealed partial class KsNpcFirefighterComponent : Component
{
    /// <summary>
    ///     How far away, in metres, the NPC spots a new fire. It has to see it, too.
    /// </summary>
    [DataField]
    public float SearchRange = 10f;

    /// <summary>
    ///     How long the NPC keeps watching a tile after its fire went out, while the puddle under it has cooled.
    /// </summary>
    [DataField]
    public TimeSpan VigilTime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The longest the NPC keeps watching a tile after its fire went out, however hot the puddle under it still is.
    /// </summary>
    [DataField]
    public TimeSpan MaxWatchTime = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     A watched puddle at least this hot, in kelvin, may still relight, so the NPC keeps foam on it.
    /// </summary>
    [DataField]
    public float HotPuddleTemperature = 350f;

    /// <summary>
    ///     A watched puddle's foam blanket is topped up once it holds less than this fraction of its most foam.
    /// </summary>
    [DataField]
    public float RefoamFraction = 0.5f;

    /// <summary>
    ///     What fraction of its spray's reach the NPC closes to before it sprays.
    /// </summary>
    [DataField]
    public float ApproachFraction = 0.75f;

    /// <summary>
    ///     How often the NPC looks for fires while it is fighting one.
    /// </summary>
    [DataField]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);

    [DataField, AutoPausedField]
    public TimeSpan NextScanTime = TimeSpan.Zero;

    /// <summary>
    ///     The grid <see cref="WatchedTiles"/> is on. The NPC forgets every tile when it leaves it.
    /// </summary>
    [ViewVariables]
    public EntityUid? WatchedGrid;

    /// <summary>
    ///     Every tile the NPC has seen burn and still watches, with the last time it saw that tile burning.
    /// </summary>
    [ViewVariables]
    public Dictionary<Vector2i, TimeSpan> WatchedTiles = new();
}
