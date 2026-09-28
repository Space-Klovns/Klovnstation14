using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.Voice;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     A microphone page's side of the voice websocket for tests: real websocket framing over a loopback TCP pair,
///         skipping only the HTTP upgrade (the status host's job). Hand <see cref="SocketPair.Server"/> to a
///         <see cref="Content.Server._KS14.Voice.KsVoiceUplinkConnection"/> and talk to it through
///         <see cref="SocketPair.Client"/>.
/// </summary>
internal static class KsVoiceTestSockets
{
    public static readonly TimeSpan SocketTimeout = TimeSpan.FromSeconds(10);

    public sealed class SocketPair : IAsyncDisposable
    {
        public required WebSocket Server;
        public required WebSocket Client;
        public required TcpClient ServerTcp;
        public required TcpClient ClientTcp;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Server.Dispose();
            ClientTcp.Dispose();
            ServerTcp.Dispose();
            await Task.CompletedTask;
        }
    }

    public static async Task<SocketPair> CreateSocketPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var clientTcp = new TcpClient();
            var connect = clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var serverTcp = await listener.AcceptTcpClientAsync();
            await connect;

            return new SocketPair
            {
                ServerTcp = serverTcp,
                ClientTcp = clientTcp,
                Server = WebSocket.CreateFromStream(serverTcp.GetStream(), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero),
                Client = WebSocket.CreateFromStream(clientTcp.GetStream(), isServer: false, subProtocol: null, keepAliveInterval: TimeSpan.Zero),
            };
        }
        finally
        {
            listener.Stop();
        }
    }

    public static Task SendText(WebSocket socket, string text)
        => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    public static Task SendAudio(WebSocket socket, int frames, ushort sequence = 0)
    {
        var sampleCount = frames * KsVoiceConstants.FrameSamples;
        var buffer = new byte[KsVoiceConstants.UplinkHeaderBytes + sampleCount * 2];
        buffer[0] = KsVoiceConstants.UplinkProtocolVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), sequence);
        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(KsVoiceConstants.UplinkHeaderBytes + i * 2), (short)(i % 200 * 50));

        return socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>
    ///     Reads text messages until the socket closes, returning them and the close description.
    /// </summary>
    public static async Task<(List<string> Messages, string CloseDescription)> ReadUntilClosed(WebSocket socket)
    {
        var messages = new List<string>();
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(SocketTimeout);

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return (messages, result.CloseStatusDescription);

            messages.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    public static async Task<string> ReadText(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(SocketTimeout);
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Text));
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
