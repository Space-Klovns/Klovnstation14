using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     A TTS endpoint on loopback, speaking real HTTP, so tests go through the server's actual request and response
///         handling. Answers Ogg Vorbis by default and Ogg Opus when asked for <c>"format": "opus"</c>, like an
///         endpoint that supports both.
/// </summary>
internal sealed class KsFakeTtsEndpoint : IDisposable
{
    public readonly record struct Request(string Text, string Voice, string Format);

    private readonly HttpListener _listener = new();
    private readonly byte[] _vorbis;

    public readonly ConcurrentQueue<Request> Requests = new();

    /// <summary>
    ///     Answer every request with a 500.
    /// </summary>
    public volatile bool Failing;

    public string Url { get; }

    public KsFakeTtsEndpoint(byte[] vorbis)
    {
        _vorbis = vorbis;

        // A free port: bind one, note it, let it go.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}/tts/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Serve();
    }

    public List<Request> RequestsFor(string text)
        => Requests.Where(request => request.Text == text).ToList();

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement;
            var format = body.TryGetProperty("format", out var formatElement) ? formatElement.GetString() : null;
            Requests.Enqueue(new Request(body.GetProperty("text").GetString()!, body.GetProperty("voice").GetString()!, format));

            if (Failing)
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                continue;
            }

            var reply = format == "opus" ? KsTtsFixtures.ReferenceOggOpus : _vorbis;
            context.Response.StatusCode = 200;
            context.Response.ContentType = "audio/ogg";
            context.Response.ContentLength64 = reply.Length;
            await context.Response.OutputStream.WriteAsync(reply);
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }
}
