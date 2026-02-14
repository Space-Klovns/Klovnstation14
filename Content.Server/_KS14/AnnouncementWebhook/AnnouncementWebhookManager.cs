using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.AnnouncementWebhook;

/// <summary>
///     Manages listening on a port for HTTP POST requests
///         to make ingame server-wide announcements.
/// </summary>
public sealed class AnnouncementWebhookManager
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private readonly HttpListener _httpListener = default!;

    private ISawmill _sawmill = default!;

    private bool _enabled = false;
    private int _port = 2200;
    private string _token = "";

    private CancellationTokenSource _listenTaskCancellationTokenSource = null!;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("announcement_webhook");

        _configurationManager.OnValueChanged(KsCCVars.AnnouncementWebhookEnabled, OnEnabledChanged, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.AnnouncementWebhookPort, OnPortChanged, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.AnnouncementWebhookToken, (x) => _token = x, invokeImmediately: true);

        ResetListeningTask();
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        ResetListeningTask();
    }

    private void OnPortChanged(int newPort)
    {
        _port = newPort;
        ResetListeningTask();
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        _httpListener.Start();
        _sawmill.Info($"Started listening for announcements to forward on port ");

        while (_httpListener.IsListening &&
            !cancellationToken.IsCancellationRequested &&
            _enabled)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequestAsync(context));
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

    private void ResetListeningTask()
    {
        _listenTaskCancellationTokenSource.Cancel();
        _listenTaskCancellationTokenSource.Dispose();

        _listenTaskCancellationTokenSource = new();

        _httpListener.Prefixes.Clear();
        _httpListener.Prefixes.Add($"http://*:{_port}/");
        _ = StartListeningAsync(_listenTaskCancellationTokenSource.Token);
    }

    public void Shutdown()
    {
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

            response.StatusCode = 200;
            await WriteResponseAsync(response, "OK");
        }
        catch (Exception)
        {
            response.StatusCode = 500;
            await WriteResponseAsync(response, $"An error occurred processing request");
        }

        response.Close();
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;

        await response.OutputStream.WriteAsync(buffer, new CancellationTokenSource(5000).Token);
    }

    private sealed class RequestData
    {
        public string Token = default!;
        public string Message = default!;
    }
}
