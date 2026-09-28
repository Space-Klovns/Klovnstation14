using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.Voice;
using Robust.Shared.Network;

namespace Content.Server._KS14.Voice;

/// <summary>
///     What a <see cref="KsVoiceUplinkConnection"/> needs from its owner. Called from websocket threads.
/// </summary>
public interface IKsVoiceUplinkHost
{
    KsVoiceProcessorSettings ProcessorSettings { get; }

    float UplinkRateFactor { get; }

    bool TryAuthenticate(string token, out NetUserId userId, out string userName);

    void OnAuthenticationFailed(IPAddress remoteAddress);

    void OnAuthenticated(KsVoiceUplinkConnection connection);

    void OnChunk(KsVoiceUplinkConnection connection, KsVoiceInboundChunk chunk);

    void OnClosed(KsVoiceUplinkConnection connection);
}

/// <summary>
///     One decoded, moderated chunk of microphone audio waiting to be relayed on the main thread.
/// </summary>
public readonly record struct KsVoiceInboundChunk(NetUserId UserId, short[] Samples, bool AbuseTriggered);

/// <summary>
///     Server side of one microphone page's websocket.
///
///     Protocol, page to server:
///     <list type="bullet">
///         <item>First message, text, within <see cref="AuthTimeout"/>: <c>{"type":"auth","token":"..."}</c>.</item>
///         <item>
///             Then binary audio: <c>[u8 version][u8 reserved][u16 sequence, LE][int16 samples, LE]</c>, carrying 1 to
///                 <see cref="KsVoiceConstants.MaxFramesPerUplinkMessage"/> whole 20 ms frames at 16 kHz mono.
///         </item>
///         <item>Optionally text keepalives: <c>{"type":"ping"}</c>.</item>
///     </list>
///     Server to page: text JSON status (<c>hello</c>, <c>state</c>, <c>closing</c>).
///
///     Anything else, including audio arriving faster than <see cref="IKsVoiceUplinkHost.UplinkRateFactor"/> times
///         real time beyond a short burst allowance, closes the socket.
/// </summary>
public sealed class KsVoiceUplinkConnection
{
    public static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(5);

    private const int MaxTextBytes = 512;
    private const float BurstSeconds = 0.5f;

    private static readonly int MaxBinaryBytes =
        KsVoiceConstants.UplinkHeaderBytes + KsVoiceConstants.MaxChunkSamples * sizeof(short);

    private readonly WebSocket _socket;
    private readonly IKsVoiceUplinkHost _host;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closeSource = new();

    private KsVoiceProcessor? _processor;
    private volatile bool _transmitting;
    private readonly TaskCompletionSource _greeted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private double _sampleAllowance;
    private long _lastAllowanceTicks;

    public KsVoiceUplinkConnection(WebSocket socket, IPAddress remoteAddress, IKsVoiceUplinkHost host)
    {
        _socket = socket;
        RemoteAddress = remoteAddress;
        _host = host;
    }

    public IPAddress RemoteAddress { get; }

    /// <summary>
    ///     The user this page speaks for. Only valid once <see cref="Authenticated"/>.
    /// </summary>
    public NetUserId UserId { get; private set; }

    public bool Authenticated { get; private set; }

    /// <summary>
    ///     Whether the main thread is currently relaying this page's audio. Only transmitted audio counts towards
    ///         automatic muting. Written by the main thread, read by this connection's thread.
    /// </summary>
    public bool Transmitting
    {
        get => _transmitting;
        set => _transmitting = value;
    }

    /// <summary>
    ///     Why the socket was closed by us, for logging and tests.
    /// </summary>
    public string? CloseReason { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closeSource.Token);
        var token = linked.Token;
        var buffer = new byte[Math.Max(MaxBinaryBytes, MaxTextBytes)];

        try
        {
            if (!await AuthenticateAsync(buffer, token))
                return;

            while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var (type, length) = await ReceiveMessageAsync(buffer, token);
                switch (type)
                {
                    case WebSocketMessageType.Close:
                        return;
                    case WebSocketMessageType.Binary:
                        if (HandleAudio(buffer.AsSpan(0, length)) is { } audioError)
                        {
                            await CloseAsync(audioError.Status, audioError.Reason);
                            return;
                        }

                        break;
                    case WebSocketMessageType.Text:
                        if (!IsPing(buffer.AsSpan(0, length)))
                        {
                            await CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "bad-message");
                            return;
                        }

                        break;
                    default:
                        await CloseAsync(WebSocketCloseStatus.InvalidMessageType, "bad-message");
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closed by us or by shutdown.
        }
        catch (WebSocketException)
        {
            // The page went away.
        }
        catch (MessageTooBigException)
        {
            await CloseAsync(WebSocketCloseStatus.MessageTooBig, "too-big");
        }
        finally
        {
            if (Authenticated)
                _host.OnClosed(this);

            // Also answers a close the page started, completing the handshake.
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await CloseAsync(WebSocketCloseStatus.NormalClosure, CloseReason ?? "closed");

            _socket.Dispose();
        }
    }

    /// <summary>
    ///     Sends a JSON status object to the page. Safe to call from any thread; failures are ignored.
    ///         Anything sent before the page has been greeted waits for the greeting, so "hello" is always first.
    /// </summary>
    public async Task SendStatusAsync(object status)
    {
        try
        {
            await _greeted.Task.WaitAsync(_closeSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SendNowAsync(status);
    }

    private async Task SendNowAsync(object status)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(status);

        try
        {
            await _sendLock.WaitAsync(_closeSource.Token);
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _closeSource.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The page is gone; RunAsync cleans up.
        }
    }

    /// <summary>
    ///     Tells the page why, then closes the socket. Safe to call from any thread.
    /// </summary>
    public async Task KickAsync(string reason)
    {
        await SendStatusAsync(new { type = "closing", reason });
        await CloseAsync(WebSocketCloseStatus.NormalClosure, reason);
    }

    private async Task<bool> AuthenticateAsync(byte[] buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(AuthTimeout);

        WebSocketMessageType type;
        int length;
        try
        {
            (type, length) = await ReceiveMessageAsync(buffer, timeout.Token, maxBytes: MaxTextBytes);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            _host.OnAuthenticationFailed(RemoteAddress);
            await CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth-timeout");
            return false;
        }
        catch (MessageTooBigException)
        {
            // Anything but a small auth message is a failed authentication, audio included.
            type = WebSocketMessageType.Binary;
            length = 0;
        }

        if (type != WebSocketMessageType.Text ||
            !TryParseAuth(buffer.AsSpan(0, length), out var presentedToken) ||
            !_host.TryAuthenticate(presentedToken, out var userId, out var userName))
        {
            _host.OnAuthenticationFailed(RemoteAddress);
            await CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth-failed");
            return false;
        }

        UserId = userId;
        Authenticated = true;
        _processor = new KsVoiceProcessor(_host.ProcessorSettings);
        _lastAllowanceTicks = Environment.TickCount64;
        _sampleAllowance = BurstAllowance();

        // Register first, so that of two pages authenticating together the later one always wins. Registering lets
        //      the main thread start sending "state", which SendStatusAsync holds back until "hello" has gone out;
        //      the page would otherwise overwrite that state with its greeting's default status.
        _host.OnAuthenticated(this);
        await SendNowAsync(new { type = "hello", name = userName });
        _greeted.TrySetResult();
        return true;
    }

    /// <summary>
    ///     Validates, rate-limits and moderates one audio message, handing it to the host. Returns why the socket
    ///         must close, or null if all is well.
    /// </summary>
    private (WebSocketCloseStatus Status, string Reason)? HandleAudio(ReadOnlySpan<byte> message)
    {
        var sampleBytes = message.Length - KsVoiceConstants.UplinkHeaderBytes;
        const int frameBytes = KsVoiceConstants.FrameSamples * sizeof(short);

        if (sampleBytes <= 0 ||
            sampleBytes % frameBytes != 0 ||
            message[0] != KsVoiceConstants.UplinkProtocolVersion)
        {
            return (WebSocketCloseStatus.InvalidPayloadData, "bad-audio");
        }

        var sampleCount = sampleBytes / sizeof(short);
        if (!ConsumeAllowance(sampleCount))
            return (WebSocketCloseStatus.PolicyViolation, "rate");

        var samples = new short[sampleCount];
        var sampleSpan = message[KsVoiceConstants.UplinkHeaderBytes..];
        for (var i = 0; i < sampleCount; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(sampleSpan.Slice(i * sizeof(short), sizeof(short)));

        _processor!.Settings = _host.ProcessorSettings;
        var result = _processor.Process(samples, trackAbuse: Transmitting);

        _host.OnChunk(this, new KsVoiceInboundChunk(UserId, samples, result.AbuseTriggered));
        return null;
    }

    private static bool IsPing(ReadOnlySpan<byte> message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.ToArray());
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String &&
                typeElement.GetString() == "ping")
            {
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private bool ConsumeAllowance(int sampleCount)
    {
        var now = Environment.TickCount64;
        var elapsedSeconds = (double)(now - _lastAllowanceTicks) / 1000d;
        _lastAllowanceTicks = now;

        var rate = (double)KsVoiceConstants.SampleRate * (double)Math.Max(1f, _host.UplinkRateFactor);
        _sampleAllowance = Math.Min(BurstAllowance(), _sampleAllowance + elapsedSeconds * rate);
        _sampleAllowance -= (double)sampleCount;

        return _sampleAllowance >= 0d;
    }

    private double BurstAllowance()
        => (double)KsVoiceConstants.SampleRate * (double)BurstSeconds * (double)Math.Max(1f, _host.UplinkRateFactor)
           + (double)KsVoiceConstants.MaxChunkSamples;

    private async Task<(WebSocketMessageType Type, int Length)> ReceiveMessageAsync(byte[] buffer, CancellationToken token, int? maxBytes = null)
    {
        var limit = maxBytes ?? buffer.Length;
        var length = 0;

        while (true)
        {
            if (length >= limit)
                throw new MessageTooBigException();

            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, limit - length), token);
            if (result.MessageType == WebSocketMessageType.Close)
                return (WebSocketMessageType.Close, 0);

            length += result.Count;
            if (result.EndOfMessage)
                return (result.MessageType, length);
        }
    }

    private static bool TryParseAuth(ReadOnlySpan<byte> message, out string token)
    {
        token = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                typeElement.GetString() != "auth" ||
                !root.TryGetProperty("token", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            token = tokenElement.GetString() ?? string.Empty;
            return token.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task CloseAsync(WebSocketCloseStatus status, string reason)
    {
        CloseReason ??= reason;

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                // A websocket allows one send at a time, and a close frame is a send: without the lock, a status
                //      message going out on another thread makes this throw, and the page never learns why it was
                //      closed. Bounded, so a stuck send can't hold the close up for long.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _sendLock.WaitAsync(timeout.Token);
                try
                {
                    if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        await _socket.CloseOutputAsync(status, reason, timeout.Token);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            try
            {
                _closeSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed class MessageTooBigException : Exception;
}
