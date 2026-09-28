namespace Content.IntegrationTests;

/// <inheritdoc/>
public sealed class PoolSettings : PairSettings
{
    public override bool Connected
    {
        get => _connected || InLobby;
        init => _connected = value;
    }

    private readonly bool _dummyTicker = true;
    private readonly bool _connected;

    /// <summary>
    /// Set to true if the given server should be using a dummy ticker. Ignored if <see cref="InLobby"/> is true.
    /// </summary>
    public bool DummyTicker
    {
        get => _dummyTicker && !InLobby;
        init => _dummyTicker = value;
    }

    /// <summary>
    /// If true, this enables the creation of admin logs during the test.
    /// </summary>
    public bool AdminLogsEnabled { get; init; }

    /// <summary>
    /// Set to true if the given server/client pair should be in the lobby.
    /// If the pair is not in the lobby at the end of the test, this test must be marked as dirty.
    /// </summary>
    /// <remarks>
    /// If this is enabled, the value of <see cref="DummyTicker"/> is ignored.
    /// </remarks>
    public bool InLobby { get; init; }

    /// <summary>
    /// Set this to true to skip loading the content files.
    /// Note: This setting won't work with a client.
    /// </summary>
    public bool NoLoadContent { get; init; }

    /// <summary>
    /// Set this to the path of a map to have the given server/client pair load the map.
    /// </summary>
    public string Map { get; init; } = PoolManager.TestMap;

    // KS14 start: server light tree, for tests that need the server to compute light levels
    /// <summary>
    /// Set to true to start the server with <c>lookup.enable_server_light_tree</c> on. The server only reads that
    /// at startup, so such a pair is always created fresh and never handed to another test.
    /// </summary>
    public bool ServerLightTree { get; init; }

    public override bool MustBeNew => base.MustBeNew || ServerLightTree;

    public override bool MustNotBeReused => base.MustNotBeReused || ServerLightTree;
    // KS14 end

    public override bool CanFastRecycle(PairSettings nextSettings)
    {
        if (!base.CanFastRecycle(nextSettings))
            return false;

        if (nextSettings is not PoolSettings next)
            return false;

        // Check that certain settings match.
        return DummyTicker == next.DummyTicker
               && Map == next.Map
               && InLobby == next.InLobby;
    }
}
