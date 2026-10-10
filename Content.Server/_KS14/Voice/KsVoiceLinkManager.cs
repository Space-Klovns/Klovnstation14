using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Content.Shared._KS14.CCVar;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Issues and resolves the personal links players open to talk.
///
///     A link is <c>{public base}{public path}#{token}</c> (by default <c>{public base}/klovn/voice/#{token}</c>), where
///         the token is 256 bits from a CSPRNG. It lives in the
///         URL fragment, which browsers never send over the network, so it stays out of the status host's request
///         log, reverse-proxy logs and <c>Referer</c> headers; the page hands it to the server as the first
///         websocket message instead. A token resolves to exactly one <see cref="NetUserId"/>, which is the only
///         identity the audio is ever attributed to. Tokens are held in memory only and die with the player's
///         connection, with a reset, or with the server.
///
///     Thread-safe: websocket threads resolve tokens while the main thread issues and revokes them.
/// </summary>
public sealed partial class KsVoiceLinkManager
{
    /// <summary>
    ///     Where the status host serves the page. Links can use another path (<see cref="KsCCVars.VoicePublicPath"/>)
    ///         when a reverse proxy maps it here.
    /// </summary>
    public const string PagePath = "/klovn/voice/";

    private const int TokenBytes = 32;
    private const int DefaultStatusPort = 1212;

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    ///     <see cref="KsCCVars.VoicePublicPath"/>, normalised, or <see cref="PagePath"/> if it isn't valid.
    /// </summary>
    private string _publicPagePath = PagePath;

    private Action<string>? _publicPathHandler;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entriesByTokenHash = [];
    private readonly Dictionary<NetUserId, string> _tokenHashesByUser = [];

    /// <summary>
    ///     Raised on the main thread whenever a user's link stops being valid, so their page can be disconnected.
    /// </summary>
    public event Action<NetUserId>? LinkRevoked;

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("voice.link");
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        _publicPathHandler = OnPublicPathChanged;
        _configurationManager.OnValueChanged(KsCCVars.VoicePublicPath, _publicPathHandler, invokeImmediately: true);
    }

    public void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;

        if (_publicPathHandler != null)
        {
            _configurationManager.UnsubValueChanged(KsCCVars.VoicePublicPath, _publicPathHandler);
            _publicPathHandler = null;
        }
    }

    /// <summary>
    ///     Checks the setting when it's set, so a bad value is reported at startup or by the command that set it,
    ///         not the first time someone asks for a link.
    /// </summary>
    private void OnPublicPathChanged(string configured)
    {
        if (ResolvePublicPagePath(configured) is { } publicPath)
        {
            _publicPagePath = publicPath;
            return;
        }

        _publicPagePath = PagePath;
        _sawmill.Warning($"klovn.voice.public_path '{configured}' isn't a plain path; using {PagePath} instead.");
    }

    /// <summary>
    ///     Returns the user's current token, creating one if they have none or if <paramref name="reset"/> is set.
    ///         Resetting invalidates the old token, and so disconnects any page opened with it.
    /// </summary>
    public string ResolveToken(ICommonSession session, bool reset)
    {
        string token;

        lock (_lock)
        {
            if (!reset &&
                _tokenHashesByUser.TryGetValue(session.UserId, out var existingHash) &&
                _entriesByTokenHash.TryGetValue(existingHash, out var existing))
            {
                return existing.Token;
            }

            RemoveNoLock(session.UserId);

            token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
            var hash = HashToken(token);
            _entriesByTokenHash[hash] = new Entry(session.UserId, session.Name, token);
            _tokenHashesByUser[session.UserId] = hash;
        }

        if (reset)
            LinkRevoked?.Invoke(session.UserId);

        return token;
    }

    /// <summary>
    ///     Resolves a token presented by a page. Safe to call from any thread.
    /// </summary>
    public bool TryResolve(string token, out NetUserId userId, [NotNullWhen(true)] out string? userName)
    {
        userId = default;
        userName = null;

        if (token.Length is 0 or > 128)
            return false;

        var hash = HashToken(token);
        lock (_lock)
        {
            if (!_entriesByTokenHash.TryGetValue(hash, out var entry) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(entry.Token), Encoding.UTF8.GetBytes(token)))
            {
                return false;
            }

            userId = entry.UserId;
            userName = entry.UserName;
            return true;
        }
    }

    /// <summary>
    ///     Invalidates a user's link, if they have one.
    /// </summary>
    public void Revoke(NetUserId userId)
    {
        bool removed;
        lock (_lock)
        {
            removed = RemoveNoLock(userId);
        }

        if (removed)
            LinkRevoked?.Invoke(userId);
    }

    /// <summary>
    ///     Builds the full link for a token, or null if no public base URL can be worked out.
    /// </summary>
    public string? BuildUrl(string token)
    {
        var baseUrl = GetPublicBaseUrl();
        return baseUrl == null ? null : $"{baseUrl}{GetPublicPagePath()}#{token}";
    }

    /// <summary>
    ///     The page's path in links, from <see cref="KsCCVars.VoicePublicPath"/>: starting and ending with a slash.
    /// </summary>
    public string GetPublicPagePath()
    {
        return _publicPagePath;
    }

    /// <summary>
    ///     Normalises a configured page path to <c>/a/b/</c> form, or returns null if it isn't a plain path: only
    ///         unreserved URL characters in each segment, no <c>.</c> or <c>..</c> segments, and nothing that would
    ///         start a query or fragment. Empty means the default; <c>/</c> means the root of the public URL.
    /// </summary>
    public static string? ResolvePublicPagePath(string configured)
    {
        var trimmed = configured.Trim();
        if (trimmed.Length == 0)
            return PagePath;

        var segments = trimmed.Split('/', options: StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or "..")
                return null;

            foreach (var character in segment)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.' or '_' or '~'))
                    return null;
            }
        }

        return segments.Length == 0 ? "/" : $"/{string.Join('/', segments)}/";
    }

    /// <summary>
    ///     The base URL browsers reach the status host at, without a trailing slash.
    /// </summary>
    public string? GetPublicBaseUrl()
    {
        return ResolvePublicBaseUrl(
            _configurationManager.GetCVar(KsCCVars.VoicePublicUrl),
            _configurationManager.GetCVar(CVars.HubServerUrl),
            _configurationManager.GetCVar(CVars.TransferHttpEndpoint));
    }

    /// <summary>
    ///     Picks the public base URL: the explicit cvar if set, else the hub-advertised <c>ss14(s)://</c> address
    ///         mapped onto HTTP(S), else the transfer HTTP endpoint. Returns null if none of them parse.
    /// </summary>
    public static string? ResolvePublicBaseUrl(string publicUrl, string hubServerUrl, string transferHttpEndpoint)
    {
        if (TryNormaliseHttpUrl(publicUrl, out var explicitUrl))
            return explicitUrl;

        if (Uri.TryCreate(hubServerUrl.Trim(), UriKind.Absolute, out var hubUri) &&
            hubUri.Scheme is "ss14" or "ss14s")
        {
            var secure = hubUri.Scheme == "ss14s";
            var port = hubUri.IsDefaultPort || hubUri.Port < 0
                ? (secure ? 443 : DefaultStatusPort)
                : hubUri.Port;

            var builder = new UriBuilder(secure ? "https" : "http", hubUri.Host, port, hubUri.AbsolutePath);
            if (TryNormaliseHttpUrl(builder.Uri.ToString(), out var hubUrl))
                return hubUrl;
        }

        if (TryNormaliseHttpUrl(transferHttpEndpoint, out var transferUrl))
            return transferUrl;

        return null;
    }

    private static bool TryNormaliseHttpUrl(string value, [NotNullWhen(true)] out string? normalised)
    {
        normalised = null;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        normalised = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            Revoke(args.Session.UserId);
    }

    private bool RemoveNoLock(NetUserId userId)
    {
        if (!_tokenHashesByUser.Remove(userId, out var hash))
            return false;

        _entriesByTokenHash.Remove(hash);
        return true;
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record Entry(NetUserId UserId, string UserName, string Token);
}
