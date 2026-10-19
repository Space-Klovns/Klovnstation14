using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using ListenerContext = SpaceWizards.HttpListener.HttpListenerContext;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Serves the microphone page and its websocket on the engine status host, and owns every live page connection.
///
///     Everything here is reachable from status-host threads. The main thread only talks to it through the queues
///         (<see cref="TryDequeueChunk"/>, <see cref="TryDequeueConnectionChange"/>) and the thread-safe send/kick methods.
/// </summary>
public sealed partial class KsVoiceUplinkManager : IKsVoiceUplinkHost
{
    private const string PagePathNoSlash = "/klovn/voice";
    private const string WebSocketPath = KsVoiceLinkManager.PagePath + "ws";
    private const string ResourcePrefix = "KsVoice.";

    private static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Above this many tracked addresses, stale ones are swept out whenever a new failure is recorded.
    /// </summary>
    private const int MaxTrackedAuthFailureAddresses = 256;

    /// <summary>
    ///     The engine's private <c>StatusHost.ContextImpl._context</c>. See <see cref="GetConnectionRelease"/>.
    /// </summary>
    private static readonly FieldInfo? ListenerContextField = typeof(IStatusHost).Assembly
        .GetType("Robust.Server.ServerStatus.StatusHost+ContextImpl")
        ?.GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    ///     Whether finished websockets can be released (see <see cref="GetConnectionRelease"/>). Only an engine change
    ///         can make this false, and nothing else would notice, so a test pins it.
    /// </summary>
    public static bool CanReleaseListenerConnections => ListenerContextField?.FieldType == typeof(ListenerContext);

    /// <summary>
    ///     Published path → (embedded resource name, content type).
    /// </summary>
    private const string PageResource = "index.html";

    /// <summary>
    ///     What <see cref="WithGameLanguage"/> replaces in <see cref="PageResource"/>.
    /// </summary>
    private const string PageLanguageMarkup = "<html lang=\"en\">";

    private static readonly Dictionary<string, (string Resource, string ContentType)> StaticFiles = new()
    {
        [KsVoiceLinkManager.PagePath] = (PageResource, "text/html; charset=utf-8"),
        [KsVoiceLinkManager.PagePath + "app.js"] = ("app.js", "text/javascript; charset=utf-8"),
        [KsVoiceLinkManager.PagePath + "i18n.js"] = ("i18n.js", "text/javascript; charset=utf-8"),
        [KsVoiceLinkManager.PagePath + "worklet.js"] = ("worklet.js", "text/javascript; charset=utf-8"),
        [KsVoiceLinkManager.PagePath + "style.css"] = ("style.css", "text/css; charset=utf-8"),
    };

    private static readonly Dictionary<string, string> SecurityHeaders = new()
    {
        ["Content-Security-Policy"] =
            "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
        ["Referrer-Policy"] = "no-referrer",
        ["Cache-Control"] = "no-store",
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Permissions-Policy"] = "microphone=(self)",
    };

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private ILocalizationManager _localizationManager = default!;
    [Dependency] private IServerNetManager _netManager = default!;
    [Dependency] private KsVoiceLinkManager _linkManager = default!;

    private ISawmill _sawmill = default!;

    private readonly ConcurrentDictionary<NetUserId, KsVoiceUplinkConnection> _connections = new();
    private readonly ConcurrentQueue<KsVoiceInboundChunk> _inbound = new();
    private readonly ConcurrentQueue<(NetUserId UserId, bool Connected)> _connectionChanges = new();
    private readonly Dictionary<IPAddress, Queue<DateTime>> _authFailures = [];
    private readonly Dictionary<string, byte[]> _staticFileCache = [];
    private readonly Lock _lock = new();

    private readonly CancellationTokenSource _shutdownSource = new();
    private bool _warnedNoConnectionRelease;

    // Written on the main thread by cvar callbacks, read from status-host threads.
    private volatile bool _enabled;
    private volatile bool _uplinkEnabled;
    private volatile bool _checkOrigin;
    private volatile int _authFailuresPerMinute;
    private float _uplinkRateFactor;
    private KsVoiceProcessorSettings _processorSettings;
    private KsVoiceEncoderSettings _encoderSettings;
    private string? _warnedCodec;

    private Action<bool>? _enabledHandler;
    private Action<bool>? _uplinkEnabledHandler;
    private Action<bool>? _checkOriginHandler;
    private Action<int>? _authFailuresHandler;
    private Action<float>? _rateFactorHandler;
    private Action<float>? _processorHandler;
    private Action<string>? _codecHandler;
    private Action<int>? _opusBitrateHandler;
    private Action<int>? _opusComplexityHandler;

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("ks.voice");
        _netManager.RegisterNetMessage<KsVoiceFrameMessage>();

        _enabledHandler = value => SetAvailability(value, _uplinkEnabled);
        _uplinkEnabledHandler = value => SetAvailability(_enabled, value);
        _checkOriginHandler = value => _checkOrigin = value;
        _authFailuresHandler = value => _authFailuresPerMinute = value;
        _rateFactorHandler = value => Volatile.Write(ref _uplinkRateFactor, value);
        _processorHandler = _ => ReloadProcessorSettings();
        _codecHandler = _ => ReloadEncoderSettings();
        _opusBitrateHandler = _ => ReloadEncoderSettings();
        _opusComplexityHandler = _ => ReloadEncoderSettings();

        _configurationManager.OnValueChanged(KsCCVars.VoiceEnabled, _enabledHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.VoiceUplinkEnabled, _uplinkEnabledHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.VoiceCheckOrigin, _checkOriginHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.VoiceAuthFailuresPerMinute, _authFailuresHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.VoiceUplinkRateFactor, _rateFactorHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.VoiceLimiterCeilingDb, _processorHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceAbuseRmsDb, _processorHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceAbuseClipRatio, _processorHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceAbuseSeconds, _processorHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceCodec, _codecHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceOpusBitrate, _opusBitrateHandler);
        _configurationManager.OnValueChanged(KsCCVars.VoiceOpusComplexity, _opusComplexityHandler);
        ReloadProcessorSettings();
        ReloadEncoderSettings();

        _linkManager.LinkRevoked += OnLinkRevoked;
        _statusHost.AddHandler(HandleRequestAsync);
    }

    public void Shutdown()
    {
        _linkManager.LinkRevoked -= OnLinkRevoked;

        if (_enabledHandler != null)
        {
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceEnabled, _enabledHandler);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceUplinkEnabled, _uplinkEnabledHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceCheckOrigin, _checkOriginHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceAuthFailuresPerMinute, _authFailuresHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceUplinkRateFactor, _rateFactorHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceLimiterCeilingDb, _processorHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceAbuseRmsDb, _processorHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceAbuseClipRatio, _processorHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceAbuseSeconds, _processorHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceCodec, _codecHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceOpusBitrate, _opusBitrateHandler!);
            _configurationManager.UnsubValueChanged(KsCCVars.VoiceOpusComplexity, _opusComplexityHandler!);
            _enabledHandler = null;
        }

        _enabled = false;
        _shutdownSource.Cancel();
    }

    #region Main-thread API

    public bool TryDequeueChunk(out KsVoiceInboundChunk chunk)
        => _inbound.TryDequeue(out chunk);

    public bool TryDequeueConnectionChange(out NetUserId userId, out bool connected)
    {
        var dequeued = _connectionChanges.TryDequeue(out var change);
        userId = change.UserId;
        connected = change.Connected;
        return dequeued;
    }

    public bool IsConnected(NetUserId userId)
        => _connections.ContainsKey(userId);

    /// <summary>
    ///     Sends a JSON status object to the user's page, if one is connected.
    /// </summary>
    public void SendStatus(NetUserId userId, object status)
    {
        if (_connections.TryGetValue(userId, out var connection))
            _ = connection.SendStatusAsync(status);
    }

    /// <summary>
    ///     Tells the user's page connection whether its audio is being relayed, which decides whether it counts
    ///         towards automatic muting.
    /// </summary>
    public void SetTransmitting(NetUserId userId, bool transmitting)
    {
        if (_connections.TryGetValue(userId, out var connection))
            connection.Transmitting = transmitting;
    }

    public void Kick(NetUserId userId, string reason)
    {
        if (_connections.TryGetValue(userId, out var connection))
            _ = connection.KickAsync(reason);
    }

    public void KickAll(string reason)
    {
        foreach (var connection in _connections.Values)
            _ = connection.KickAsync(reason);
    }

    #endregion

    #region IKsVoiceUplinkHost

    public KsVoiceProcessorSettings ProcessorSettings
    {
        get
        {
            lock (_lock)
            {
                return _processorSettings;
            }
        }
    }

    public KsVoiceEncoderSettings EncoderSettings
    {
        get
        {
            lock (_lock)
            {
                return _encoderSettings;
            }
        }
    }

    public float UplinkRateFactor => Volatile.Read(ref _uplinkRateFactor);

    public bool TryAuthenticate(string token, out NetUserId userId, out string userName)
    {
        userName = string.Empty;
        userId = default;

        if (!_enabled || !_uplinkEnabled)
            return false;

        if (!_linkManager.TryResolve(token, out userId, out var resolvedName))
            return false;

        userName = resolvedName;
        return true;
    }

    public void OnAuthenticationFailed(IPAddress remoteAddress)
    {
        lock (_lock)
        {
            if (!_authFailures.TryGetValue(remoteAddress, out var failures))
                _authFailures[remoteAddress] = failures = new Queue<DateTime>();

            failures.Enqueue(DateTime.UtcNow);

            // Addresses that never come back are otherwise only pruned when they reconnect.
            if (_authFailures.Count > MaxTrackedAuthFailureAddresses)
                PruneAuthFailuresNoLock();
        }

        _sawmill.Info($"Voice page authentication failed from {remoteAddress}.");
    }

    public void OnAuthenticated(KsVoiceUplinkConnection connection)
    {
        // One atomic swap, so that of two pages authenticating at once, whichever is displaced is always told. Checking
        //      and then writing would let both see an empty slot, and leave the loser open but unable to send audio.
        KsVoiceUplinkConnection? displacedConnection = null;
        _connections.AddOrUpdate(connection.UserId,
            connection,
            (_, existingConnection) =>
            {
                displacedConnection = existingConnection;
                return connection;
            });

        if (displacedConnection != null && displacedConnection != connection)
            _ = displacedConnection.KickAsync("replaced");

        _connectionChanges.Enqueue((connection.UserId, true));
    }

    public void OnChunk(KsVoiceUplinkConnection connection, KsVoiceInboundChunk chunk)
    {
        // A page that was replaced or revoked may still have a message in flight; only the registered one speaks.
        if (!_enabled ||
            !_connections.TryGetValue(connection.UserId, out var current) ||
            current != connection)
        {
            return;
        }

        _inbound.Enqueue(chunk);
    }

    public void OnClosed(KsVoiceUplinkConnection connection)
    {
        if (_connections.TryRemove(new KeyValuePair<NetUserId, KsVoiceUplinkConnection>(connection.UserId, connection)))
            _connectionChanges.Enqueue((connection.UserId, false));
    }

    #endregion

    private async Task<bool> HandleRequestAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (!path.StartsWith(PagePathNoSlash, StringComparison.Ordinal))
            return false;

        // Unavailable voice looks exactly like no voice at all.
        if (!_enabled || !_uplinkEnabled)
            return false;

        if (path == WebSocketPath)
            return await HandleWebSocketAsync(context);

        if (!context.IsGetLike)
            return false;

        if (path == PagePathNoSlash)
        {
            // Relative, so it survives reverse proxies that mount us under a prefix. Browsers keep the fragment. Always
            //      "voice/", whatever klovn.voice.public_path says: a proxy publishing the page at another path answers
            //      its own slashless form itself (nginx does for "location /talk/"), so only requests made straight to
            //      the status host get here, and for those "voice/" is right.
            context.ResponseHeaders["Location"] = "voice/";
            await context.RespondAsync("Moved", code: HttpStatusCode.MovedPermanently);
            return true;
        }

        if (!TryGetStaticFile(path, out var data, out var contentType))
            return false;

        foreach (var (header, value) in SecurityHeaders)
            context.ResponseHeaders[header] = value;

        await context.RespondAsync(data, code: HttpStatusCode.OK, contentType: contentType);
        return true;
    }

    /// <summary>
    ///     A file of the page, exactly as served: <paramref name="path"/> is the request path, e.g.
    ///         <see cref="KsVoiceLinkManager.PagePath"/>.
    /// </summary>
    public bool TryGetStaticFile(string path, [NotNullWhen(true)] out byte[]? data, out string contentType)
    {
        data = null;
        contentType = "";

        if (!StaticFiles.TryGetValue(path, out var file) ||
            LoadStaticFile(file.Resource) is not { } loaded)
        {
            return false;
        }

        data = file.Resource == PageResource ? WithGameLanguage(loaded) : loaded;
        contentType = file.ContentType;
        return true;
    }

    private async Task<bool> HandleWebSocketAsync(IStatusHandlerContext context)
    {
        if (!context.IsWebSocketRequest)
        {
            await context.RespondErrorAsync(HttpStatusCode.BadRequest);
            return true;
        }

        var remoteAddress = GetClientAddress(context);
        if (IsAuthThrottled(remoteAddress))
        {
            await context.RespondErrorAsync(HttpStatusCode.TooManyRequests);
            return true;
        }

        if (_checkOrigin && !IsOriginAllowed(context))
        {
            _sawmill.Info($"Voice websocket from {remoteAddress} refused: origin not allowed.");
            await context.RespondErrorAsync(HttpStatusCode.Forbidden);
            return true;
        }

        var releaseConnection = GetConnectionRelease(context);
        var socket = await context.AcceptWebSocketAsync();
        var connection = new KsVoiceUplinkConnection(socket, remoteAddress, this);
        var shutdownToken = _shutdownSource.Token;

        // Hand the socket's lifetime to the thread pool and return straight away: the status host counts a request
        //      against `status.max_connections` until its handler returns, so holding it here would let a handful of
        //      talkers starve the whole status host.
        _ = Task.Run(async () =>
        {
            try
            {
                await connection.RunAsync(shutdownToken);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Voice websocket from {remoteAddress} failed: {e}");
            }
            finally
            {
                releaseConnection?.Invoke();
            }
        }, shutdownToken);

        return true;
    }

    /// <summary>
    ///     Returns an action that closes the TCP connection under a status-host websocket.
    ///
    ///     The listener hands websockets a stream that doesn't own its socket, so disposing the websocket after the close
    ///         handshake leaves the connection in CLOSE_WAIT for the life of the server, and the page's browser waits
    ///         for a TCP close that never comes. Only the listener's response can release it, and the engine keeps that
    ///         behind <see cref="IStatusHandlerContext"/>. Server content isn't sandboxed, so reach it by reflection; if
    ///         the engine ever renames the field, voice keeps working and we just stop releasing connections early.
    /// </summary>
    private Action? GetConnectionRelease(IStatusHandlerContext context)
    {
        if (ListenerContextField?.GetValue(context) is ListenerContext listenerContext)
        {
            return () =>
            {
                try
                {
                    listenerContext.Response.Abort();
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
                {
                }
            };
        }

        if (!_warnedNoConnectionRelease)
        {
            _warnedNoConnectionRelease = true;
            _sawmill.Warning("Can't reach the status host's listener context; finished voice websockets will linger until the client drops them.");
        }

        return null;
    }

    private bool IsOriginAllowed(IStatusHandlerContext context)
    {
        if (!context.RequestHeaders.TryGetValue("Origin", out var originValues) ||
            originValues.Count != 1 ||
            !Uri.TryCreate(originValues[0], UriKind.Absolute, out var origin))
        {
            return false;
        }

        if (_linkManager.GetPublicBaseUrl() is { } baseUrl &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            Uri.Compare(origin, baseUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }

        // Same-origin with whatever host the request was addressed to, e.g. local testing on http://localhost:1212.
        return context.RequestHeaders.TryGetValue("Host", out var hostValues) &&
               hostValues.Count == 1 &&
               string.Equals(origin.Authority, hostValues[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The address to rate-limit failed authentications by. Behind a local reverse proxy every request comes from
    ///         loopback, so only then is the proxy-appended (last) <c>X-Forwarded-For</c> entry trusted.
    /// </summary>
    private static IPAddress GetClientAddress(IStatusHandlerContext context)
    {
        var remote = context.RemoteEndPoint.Address;
        if (!IPAddress.IsLoopback(remote) ||
            !context.RequestHeaders.TryGetValue("X-Forwarded-For", out var forwarded) ||
            forwarded.Count == 0)
        {
            return remote;
        }

        var last = forwarded[forwarded.Count - 1]?.Split(',').LastOrDefault()?.Trim();
        return last != null && IPAddress.TryParse(last, out var parsed) ? parsed : remote;
    }

    private void PruneAuthFailuresNoLock()
    {
        var cutoff = DateTime.UtcNow - AuthFailureWindow;
        foreach (var (address, failures) in _authFailures)
        {
            while (failures.TryPeek(out var time) && time < cutoff)
                failures.Dequeue();

            if (failures.Count == 0)
                _authFailures.Remove(address);
        }
    }

    private bool IsAuthThrottled(IPAddress remoteAddress)
    {
        lock (_lock)
        {
            if (!_authFailures.TryGetValue(remoteAddress, out var failures))
                return false;

            var cutoff = DateTime.UtcNow - AuthFailureWindow;
            while (failures.TryPeek(out var time) && time < cutoff)
                failures.Dequeue();

            if (failures.Count == 0)
            {
                _authFailures.Remove(remoteAddress);
                return false;
            }

            return failures.Count >= _authFailuresPerMinute;
        }
    }

    /// <summary>
    ///     The page, with its <c>&lt;html lang&gt;</c> set to the language the game itself runs in, which the page then
    ///         shows itself in unless the player has picked another. Done per request rather than cached: the culture
    ///         is only loaded once content has initialised, and the status host may already be answering by then.
    /// </summary>
    private byte[] WithGameLanguage(byte[] page)
    {
        var culture = GameCultureName();
        var html = Encoding.UTF8.GetString(page);
        if (!html.Contains(PageLanguageMarkup, StringComparison.Ordinal))
        {
            _sawmill.Error($"The voice page has no '{PageLanguageMarkup}' to put the game's language in.");
            return page;
        }

        return Encoding.UTF8.GetBytes(html.Replace(PageLanguageMarkup, $"<html lang=\"{culture}\">", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The culture the game's localization runs in (<c>ContentLocalizationManager</c> loads it), e.g. "en-US". Not
    ///         <c>loc.culture_name</c>: content loads a fixed culture and never reads that cvar.
    /// </summary>
    public string GameCultureName()
    {
        var name = _localizationManager.DefaultCulture?.Name;

        // Only a language tag's characters, since it goes into markup.
        return !string.IsNullOrEmpty(name) && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? name
            : "en";
    }

    private byte[]? LoadStaticFile(string resource)
    {
        lock (_lock)
        {
            if (_staticFileCache.TryGetValue(resource, out var cached))
                return cached;

            using var stream = typeof(KsVoiceUplinkManager).Assembly.GetManifestResourceStream(ResourcePrefix + resource);
            if (stream == null)
            {
                _sawmill.Error($"Missing embedded voice page resource {resource}.");
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return _staticFileCache[resource] = memory.ToArray();
        }
    }

    private void SetAvailability(bool enabled, bool uplinkEnabled)
    {
        _enabled = enabled;
        _uplinkEnabled = uplinkEnabled;

        if (!enabled || !uplinkEnabled)
            KickAll("disabled");
    }

    private void ReloadProcessorSettings()
    {
        var settings = new KsVoiceProcessorSettings(
            _configurationManager.GetCVar(KsCCVars.VoiceLimiterCeilingDb),
            _configurationManager.GetCVar(KsCCVars.VoiceAbuseRmsDb),
            _configurationManager.GetCVar(KsCCVars.VoiceAbuseClipRatio),
            _configurationManager.GetCVar(KsCCVars.VoiceAbuseSeconds));

        lock (_lock)
        {
            _processorSettings = settings;
        }
    }

    private void ReloadEncoderSettings()
    {
        var codecName = _configurationManager.GetCVar(KsCCVars.VoiceCodec);
        var codec = ParseCodec(codecName);
        if (codec == null && _warnedCodec != codecName)
        {
            _warnedCodec = codecName;
            _sawmill.Warning($"klovn.voice.codec '{codecName}' isn't adpcm or opus; using adpcm.");
        }

        var settings = new KsVoiceEncoderSettings(
            codec ?? KsVoiceCodec.Adpcm,
            _configurationManager.GetCVar(KsCCVars.VoiceOpusBitrate),
            _configurationManager.GetCVar(KsCCVars.VoiceOpusComplexity));
        lock (_lock)
        {
            _encoderSettings = settings;
        }
    }

    /// <summary>
    ///     The codec a <c>klovn.voice.codec</c> value names, or null if it names none.
    /// </summary>
    public static KsVoiceCodec? ParseCodec(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "adpcm" => KsVoiceCodec.Adpcm,
            "opus" => KsVoiceCodec.Opus,
            _ => null,
        };
    }

    private void OnLinkRevoked(NetUserId userId)
    {
        Kick(userId, "link-reset");
    }
}
