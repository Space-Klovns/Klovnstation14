using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Llm;

// The llama-server child process: launch, readiness, crash handling, restart and teardown. Like the rest of the
//      manager, every field here outside _processLock is main-thread only.
public sealed partial class KsLlmManager
{
    private const int OutputTailLength = 20;

    /// <summary>
    ///     The longest wait between attempts to reach a remote backend, which is retried forever.
    /// </summary>
    private const double MaxRemoteRetryDelaySeconds = 60;

    /// <summary>
    ///     Remembers the child's PID across a hard crash of this process, so the next start can reap it.
    /// </summary>
    private static readonly ResPath PidFilePath = new("/llm/llama-server.pid");

    private ISawmill _serverSawmill = default!;

    /// <summary>
    ///     Bumped whenever the backend is stopped or started. Events carry the generation they were started
    ///         under, and anything from an older one is ignored - a killed process's exit, a superseded health
    ///         check - so a late result can never be mistaken for the current backend's.
    /// </summary>
    private int _generation;

    private CancellationTokenSource _backendCancellationTokenSource = new();

    // The one piece of state shared with background threads: the shutdown paths must be able to reach the child
    // from any thread, including one the process is dying on.
    private readonly object _processLock = new();
    private Process? _process;
    private KsLlmJobObject? _jobObject;

    // Guarded by itself.
    private readonly Queue<string> _outputTail = new();

    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;

    /// <summary>
    ///     Whether the current backend is <see cref="KsCCVars.LlmEndpoint"/> rather than a child process. A remote
    ///         backend being unreachable is expected - somebody's PC is switched off - so it is retried forever
    ///         and quietly, where a local process gets a few loud attempts.
    /// </summary>
    private bool _remoteEndpoint;
    private int _contextSize;
    private int _restartAttempt;
    private int _maxRestartAttempts;
    private TimeSpan _restartAt;

    private PosixSignalRegistration? _sigtermRegistration;

    public int ContextSize => _contextSize;

    public string BackendUrl => _baseUrl;

    /// <summary>
    ///     Stops the child for good. Called from the entry point as the server shuts down.
    /// </summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        _generation++;

        _lifetimeCancellationTokenSource.Cancel();
        _backendCancellationTokenSource.Cancel();

        // The server is exiting; blocking briefly here keeps the child from outliving it.
        KillCurrentProcess(wait: true);
        TryDeletePidFile();

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _sigtermRegistration?.Dispose();
        _sigtermRegistration = null;

        _prototypeManager.PrototypesReloaded -= OnPrototypesReloaded;
        State = KsLlmState.Disabled;
    }

    /// <summary>
    ///     Second line of defence for exits that skip <see cref="Shutdown"/>. A hard crash of this process skips
    ///         these too; on Windows the job object covers that, elsewhere the PID file reap on next start does.
    /// </summary>
    private void RegisterExitHandlers()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try
        {
            // Not cancelling the signal: the default handling (and the engine's own) still proceeds.
            _sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => KillCurrentProcess(wait: false));
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        KillCurrentProcess(wait: false);
    }

    #region Start and stop

    private void StartBackend()
    {
        StopBackend();

        var generation = _generation;
        State = KsLlmState.Starting;

        var launchConfig = new LaunchConfig(
            Endpoint: _configurationManager.GetCVar(KsCCVars.LlmEndpoint).Trim().TrimEnd('/'),
            ServerPath: _configurationManager.GetCVar(KsCCVars.LlmServerPath),
            ModelPath: _configurationManager.GetCVar(KsCCVars.LlmModelPath),
            GpuLayers: _configurationManager.GetCVar(KsCCVars.LlmGpuLayers),
            ContextSize: _configurationManager.GetCVar(KsCCVars.LlmContextSize),
            ExtraArgs: _configurationManager.GetCVar(KsCCVars.LlmExtraArgs),
            StartupTimeout: TimeSpan.FromSeconds(_configurationManager.GetCVar(KsCCVars.LlmStartupTimeout)),
            ApiKey: _configurationManager.GetCVar(KsCCVars.LlmApiKey).Trim(),
            HttpClient: _httpClient,
            UserData: _resourceManager.UserData);

        _contextSize = launchConfig.ContextSize;
        _apiKey = launchConfig.ApiKey;
        _remoteEndpoint = launchConfig.Endpoint.Length > 0;

        if (!_remoteEndpoint)
            _sawmill.Info($"Launching llama-server (attempt {_restartAttempt + 1}).");
        else if (_restartAttempt == 0)
            _sawmill.Info($"Connecting to remote backend at {launchConfig.Endpoint}.");
        else
            _sawmill.Debug($"Retrying remote backend at {launchConfig.Endpoint} (attempt {_restartAttempt + 1}).");

        var cancellationToken = _backendCancellationTokenSource.Token;
        _ = Task.Run(() => LaunchAsync(generation, launchConfig, cancellationToken));
    }

    /// <summary>
    ///     Invalidates the current backend and kills its process, if any, off the main thread.
    /// </summary>
    private void StopBackend()
    {
        _generation++;

        _backendCancellationTokenSource.Cancel();
        _backendCancellationTokenSource.Dispose();
        _backendCancellationTokenSource = new CancellationTokenSource();

        _baseUrl = string.Empty;

        Process? process;
        KsLlmJobObject? jobObject;
        lock (_processLock)
        {
            process = _process;
            jobObject = _jobObject;
            _process = null;
            _jobObject = null;
        }

        if (process != null)
            _ = Task.Run(() => KillProcess(process, jobObject, wait: true));
    }

    private void ScheduleRestart(bool retryable)
    {
        StopBackend();
        FailAllTurns();

        if (!retryable || !_remoteEndpoint && _restartAttempt >= _maxRestartAttempts)
        {
            State = KsLlmState.Failed;
            _sawmill.Error(retryable
                ? $"Backend failed {_restartAttempt + 1} times; giving up. Use 'ks_llm restart' once the cause is fixed."
                : "Backend cannot start; not retrying. Fix the configuration, then use 'ks_llm restart'.");
            return;
        }

        var delaySeconds = 2 * Math.Pow(2, _restartAttempt);
        if (_remoteEndpoint)
            delaySeconds = Math.Min(delaySeconds, MaxRemoteRetryDelaySeconds);

        var delay = TimeSpan.FromSeconds(delaySeconds);
        _restartAttempt++;
        _restartAt = _gameTiming.RealTime + delay;
        State = KsLlmState.Restarting;

        if (!_remoteEndpoint)
            _sawmill.Warning($"Restarting backend in {delay.TotalSeconds}s (attempt {_restartAttempt + 1} of {_maxRestartAttempts + 1}).");
        else if (_restartAttempt == 1)
            _sawmill.Warning($"Remote backend unreachable; retrying until it answers, at most {MaxRemoteRetryDelaySeconds}s apart. Faxes go unanswered meanwhile.");
    }

    private void HandleEvent(BackendEvent backendEvent)
    {
        switch (backendEvent)
        {
            case CompletionEvent completionEvent:
                HandleCompletion(completionEvent);
                break;

            case BackendReadyEvent readyEvent when readyEvent.Generation == _generation:
                _baseUrl = readyEvent.BaseUrl;
                if (readyEvent.ContextSize is > 0)
                    _contextSize = readyEvent.ContextSize.Value;

                State = _activeTurn != null ? KsLlmState.Busy : KsLlmState.Ready;
                _sawmill.Info($"Backend ready at {_baseUrl} with a {_contextSize}-token context.");

                // A remote backend that answers is simply back; its retry backoff starts over.
                if (_remoteEndpoint)
                    _restartAttempt = 0;
                break;

            case BackendFailedEvent failedEvent when failedEvent.Generation == _generation:
                // An unreachable remote is routine and retried forever, so only its first failure is worth a
                // warning. Everything else is a real fault.
                if (_remoteEndpoint && failedEvent.Retryable)
                {
                    if (_restartAttempt == 0)
                        _sawmill.Warning($"Could not reach the remote backend: {failedEvent.Reason}");
                    else
                        _sawmill.Debug($"Could not reach the remote backend: {failedEvent.Reason}");
                }
                else
                {
                    _sawmill.Error($"Backend failed to start: {failedEvent.Reason}");
                }

                ScheduleRestart(failedEvent.Retryable);
                break;

            case BackendExitedEvent exitedEvent when exitedEvent.Generation == _generation:
                _sawmill.Error($"llama-server exited unexpectedly with code {exitedEvent.ExitCode}. Last output:\n{GetOutputTail()}");
                ScheduleRestart(retryable: true);
                break;
        }
    }

    #endregion

    #region Background

    /// <summary>
    ///     Background only. Brings a backend up and reports how it went, via <see cref="_events"/> alone.
    /// </summary>
    private async Task LaunchAsync(int generation, LaunchConfig launchConfig, CancellationToken cancellationToken)
    {
        try
        {
            string baseUrl;
            Process? process = null;

            if (launchConfig.Endpoint.Length > 0)
            {
                baseUrl = launchConfig.Endpoint;
            }
            else
            {
                if (!File.Exists(launchConfig.ServerPath))
                {
                    _events.Enqueue(new BackendFailedEvent(generation, $"llama-server executable not found at '{launchConfig.ServerPath}' (klovn.llm.server_path).", Retryable: false));
                    return;
                }

                if (!File.Exists(launchConfig.ModelPath))
                {
                    _events.Enqueue(new BackendFailedEvent(generation, $"Model file not found at '{launchConfig.ModelPath}' (klovn.llm.model_path).", Retryable: false));
                    return;
                }

                ReapStaleProcess(launchConfig.UserData);

                var port = GetFreeLoopbackPort();
                baseUrl = $"http://127.0.0.1:{port}";
                process = StartProcess(generation, launchConfig, port, cancellationToken);
                if (process == null)
                    return;
            }

            if (!await WaitForHealthAsync(launchConfig.HttpClient, baseUrl, launchConfig.ApiKey, process, launchConfig.StartupTimeout, cancellationToken).ConfigureAwait(false))
            {
                // A process that died while loading reports through its exit event instead.
                if (process is not { HasExited: true })
                    _events.Enqueue(new BackendFailedEvent(generation, $"no healthy response from {baseUrl} within {launchConfig.StartupTimeout.TotalSeconds}s.", Retryable: true));

                return;
            }

            // llama-server answers /health without a key, so check the key against something that needs one
            // now, rather than find out on the first fax.
            if (!await IsApiKeyAcceptedAsync(launchConfig.HttpClient, baseUrl, launchConfig.ApiKey, cancellationToken).ConfigureAwait(false))
            {
                _events.Enqueue(new BackendFailedEvent(generation, $"{baseUrl} rejected the API key; check klovn.llm.api_key.", Retryable: false));
                return;
            }

            var contextSize = await TryReadContextSizeAsync(launchConfig.HttpClient, baseUrl, launchConfig.ApiKey, cancellationToken).ConfigureAwait(false);
            _events.Enqueue(new BackendReadyEvent(generation, baseUrl, contextSize));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded by a stop or restart.
        }
        catch (Exception exception)
        {
            _events.Enqueue(new BackendFailedEvent(generation, exception.ToString(), Retryable: true));
        }
    }

    private Process? StartProcess(int generation, LaunchConfig launchConfig, int port, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(launchConfig.ServerPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(launchConfig.ModelPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--ctx-size");
        startInfo.ArgumentList.Add(launchConfig.ContextSize.ToString());
        startInfo.ArgumentList.Add("--n-gpu-layers");
        startInfo.ArgumentList.Add(launchConfig.GpuLayers.ToString());
        startInfo.ArgumentList.Add("--parallel");
        startInfo.ArgumentList.Add("1");
        // Chat-template rendering; native tool calling does not parse without it.
        startInfo.ArgumentList.Add("--jinja");

        foreach (var argument in launchConfig.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => OnServerOutput(args.Data);
        process.ErrorDataReceived += (_, args) => OnServerOutput(args.Data);
        process.Exited += (_, _) => _events.Enqueue(new BackendExitedEvent(generation, GetExitCode(process)));

        process.Start();

        var jobObject = OperatingSystem.IsWindows() ? KsLlmJobObject.TryCreateFor(process, _sawmill) : null;

        lock (_processLock)
        {
            // Stopped while we were launching: this child belongs to nobody, so it goes straight away.
            if (cancellationToken.IsCancellationRequested)
            {
                KillProcess(process, jobObject, wait: false);
                return null;
            }

            _process = process;
            _jobObject = jobObject;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        WritePidFile(launchConfig.UserData, process.Id);
        _serverSawmill.Info($"Started llama-server (PID {process.Id}) on port {port}.");
        return process;
    }

    private static async Task<bool> WaitForHealthAsync(HttpClient httpClient,
        string baseUrl,
        string apiKey,
        Process? process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = TimeSpan.FromMilliseconds(250);

        while (stopwatch.Elapsed < timeout)
        {
            if (process is { HasExited: true })
                return false;

            try
            {
                using var attemptCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));

                // 503 while the model is still loading, 200 once it can serve.
                using var request = CreateRequest(HttpMethod.Get, baseUrl + "/health", apiKey);
                using var response = await httpClient.SendAsync(request, attemptCancellationTokenSource.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // This attempt timed out; try again.
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
        }

        return false;
    }

    /// <summary>
    ///     The context size the backend actually runs with, which may differ from what was asked for.
    /// </summary>
    private static async Task<bool> IsApiKeyAcceptedAsync(HttpClient httpClient, string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var attemptCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            using var request = CreateRequest(HttpMethod.Get, baseUrl + "/v1/models", apiKey);
            using var response = await httpClient.SendAsync(request, attemptCancellationTokenSource.Token).ConfigureAwait(false);
            return response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Inconclusive; the first request will say for certain.
            return true;
        }
    }

    private static async Task<int?> TryReadContextSizeAsync(HttpClient httpClient, string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var attemptCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

            using var request = CreateRequest(HttpMethod.Get, baseUrl + "/props", apiKey);
            using var response = await httpClient.SendAsync(request, attemptCancellationTokenSource.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(attemptCancellationTokenSource.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("default_generation_settings", out var settings)
                && settings.TryGetProperty("n_ctx", out var nestedContext)
                && nestedContext.TryGetInt32(out var nestedValue))
                return nestedValue;

            if (root.TryGetProperty("n_ctx", out var context) && context.TryGetInt32(out var value))
                return value;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Not every OpenAI-compatible server has /props; the cvar stands in.
        }

        return null;
    }

    private static int GetFreeLoopbackPort()
    {
        // Let the OS choose rather than hardcoding, so an orphan from a previous crash can't collide.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void ReapStaleProcess(IWritableDirProvider userData)
    {
        try
        {
            if (!userData.TryReadAllText(PidFilePath, out var pidText))
                return;

            userData.Delete(PidFilePath);

            if (!int.TryParse(pidText.Trim(), out var pid))
                return;

            using var staleProcess = Process.GetProcessById(pid);
            if (!staleProcess.ProcessName.Contains("llama-server", StringComparison.OrdinalIgnoreCase))
                return;

            _sawmill.Warning($"Killing llama-server (PID {pid}) left behind by a previous run.");
            staleProcess.Kill(entireProcessTree: true);
            staleProcess.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // No process with that PID any more.
        }
        catch (Exception exception)
        {
            _sawmill.Warning($"Could not reap a stale llama-server: {exception.Message}");
        }
    }

    private void WritePidFile(IWritableDirProvider userData, int pid)
    {
        try
        {
            userData.CreateDir(PidFilePath.Directory);
            userData.WriteAllText(PidFilePath, pid.ToString());
        }
        catch (Exception exception)
        {
            _sawmill.Warning($"Could not write the llama-server PID file: {exception.Message}");
        }
    }

    private void TryDeletePidFile()
    {
        try
        {
            if (_resourceManager.UserData.Exists(PidFilePath))
                _resourceManager.UserData.Delete(PidFilePath);
        }
        catch (Exception exception)
        {
            _sawmill.Warning($"Could not delete the llama-server PID file: {exception.Message}");
        }
    }

    private void OnServerOutput(string? line)
    {
        if (line == null)
            return;

        _serverSawmill.Debug(line);

        lock (_outputTail)
        {
            _outputTail.Enqueue(line);
            while (_outputTail.Count > OutputTailLength)
                _outputTail.Dequeue();
        }
    }

    private string GetOutputTail()
    {
        lock (_outputTail)
        {
            return string.Join('\n', _outputTail);
        }
    }

    #endregion

    #region Killing

    /// <summary>
    ///     Safe from any thread.
    /// </summary>
    private void KillCurrentProcess(bool wait)
    {
        Process? process;
        KsLlmJobObject? jobObject;
        lock (_processLock)
        {
            process = _process;
            jobObject = _jobObject;
            _process = null;
            _jobObject = null;
        }

        if (process != null)
            KillProcess(process, jobObject, wait);
    }

    private static void KillProcess(Process process, KsLlmJobObject? jobObject, bool wait)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            if (wait)
                process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Already gone, or going. Nothing more to do either way.
        }
        finally
        {
            // Closing the job kills anything still in it.
            jobObject?.Dispose();
            process.Dispose();
        }
    }

    private static int GetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    #endregion

    #region Types

    private sealed record LaunchConfig(
        string Endpoint,
        string ServerPath,
        string ModelPath,
        int GpuLayers,
        int ContextSize,
        string ExtraArgs,
        TimeSpan StartupTimeout,
        string ApiKey,
        HttpClient HttpClient,
        IWritableDirProvider UserData);

    private sealed record BackendReadyEvent(int Generation, string BaseUrl, int? ContextSize) : BackendEvent;

    private sealed record BackendFailedEvent(int Generation, string Reason, bool Retryable) : BackendEvent;

    private sealed record BackendExitedEvent(int Generation, int ExitCode) : BackendEvent;

    #endregion
}
