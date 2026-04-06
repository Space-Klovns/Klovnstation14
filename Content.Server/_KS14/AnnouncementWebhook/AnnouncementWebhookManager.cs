using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using HttpListener = SpaceWizards.HttpListener.HttpListener;
using HttpListenerContext = SpaceWizards.HttpListener.HttpListenerContext;
using HttpListenerResponse = SpaceWizards.HttpListener.HttpListenerResponse;

namespace Content.Server._KS14.AnnouncementWebhook;

/// <summary>
///     Manages listening on a port for HTTP POST requests
///         to make ingame server-wide announcements.
/// </summary>
public sealed class AnnouncementWebhookManager
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private ConcurrentQueue<string> _pendingAnnouncements = new();

    private HttpListener _httpListener = null!;
    private ISawmill _sawmill = default!;

    private bool _enabled = false;
    private bool _shutdown = false;
    private string _token = "";

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("announcement_webhook");
        _httpListener = new();

        _httpListener.Prefixes.Add(_configurationManager.GetCVar(KsCCVars.AnnouncementWebhookInterface));
        _configurationManager.OnValueChanged(KsCCVars.AnnouncementWebhookToken, (x) => _token = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.AnnouncementWebhookEnabled, OnEnabledChanged, invokeImmediately: true);
    }

    public void Update()
    {
        if (_pendingAnnouncements.Count == 0)
            return;

        while (_pendingAnnouncements.TryDequeue(out var message))
            _chatManager.DispatchServerAnnouncement(message);
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (_shutdown)
            return;

        _enabled = enabled;

        if (enabled)
            _ = StartListeningAsync();
        else if (_httpListener.IsListening)
        {
            _httpListener.Stop();
            _sawmill.Info($"Stopped listening for announcements to forward");
        }
    }

    public async Task StartListeningAsync()
    {
        _httpListener.Start();
        _sawmill.Info($"Started listening for announcements to forward");

        while (_enabled)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequestAsync(context));
            }
            catch (ObjectDisposedException) when (!_enabled)
            {
                // shut down does this, so exit silently
                break;
            }
            catch (HttpListenerException ex)
            {
                _sawmill.Error($"Stopping AnnouncementWebhook listener loop due to HttpListenerException! Ex: {ex}");
                break;
            }
            catch (InvalidOperationException ex)
            {
                _sawmill.Error($"Stopping AnnouncementWebhook listener loop due to InvalidOperationException! Ex: {ex}");
                break;
            }
        }
    }

    public void Shutdown()
    {
        _shutdown = true;
        if (_httpListener.IsListening)
            _httpListener.Stop();

        _httpListener.Close();
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = 405; // Method Not Allowed
                await WriteResponseAsync(response, "You are only allowed to use POST here");
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            var data = JsonSerializer.Deserialize<RequestData>(body);
            if (data == null)
            {
                response.StatusCode = 400;
                await WriteResponseAsync(response, "Bad data sent");
                return;
            }

            if (data.Token != _token)
            {
                response.StatusCode = 401; // Unauthorized
                await WriteResponseAsync(response, "Bad API token");
                return;
            }

            _sawmill.Info($"Received announcement message successfully: `{data.Message}`");
            _pendingAnnouncements.Enqueue(data.Message);

            response.StatusCode = 200;
            await WriteResponseAsync(response, "OK");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception occurred when processing request: {ex}");

            response.StatusCode = 500;
            await WriteResponseAsync(response, $"An error occurred processing request");
        }

        response.Close();
    }

    /// <summary>
    ///     Responds with a newline.
    /// </summary>
    private static async Task WriteResponseAsync(HttpListenerResponse response, string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message + '\n');
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;

        await response.OutputStream.WriteAsync(buffer, new CancellationTokenSource(5000).Token);
    }

    private sealed record RequestData(string Token, string Message);
}
