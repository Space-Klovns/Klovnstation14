using System.Linq;
using System.Threading.Tasks;
using Content.Shared._KS14.TTS;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.TTS;

/// <summary>
///     Bakes one preview clip per selectable TTS voice, the first time TTS is available, and hands them to every
///         client: all of them on connect, then each new one as it is baked. The character editor plays them, so a
///         player choosing a voice never has to wait on, or load, the TTS endpoint.
/// </summary>
/// <remarks>
///     A manager rather than a system: the character editor lives in the lobby, and the message has to be registered
///         before any client connects.
/// </remarks>
public sealed partial class KsTtsPreviewManager
{
    [Dependency] private IServerNetManager _netManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ILocalizationManager _localizationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ILogManager _logManager = default!;

    /// <summary>
    ///     How long to leave a failing endpoint before trying the voices it failed on again. Settable for tests.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    private readonly Dictionary<ProtoId<TtsVoicePrototype>, KsTtsPreview> _previews = new();

    /// <summary>
    ///     The back-end voice each preview was baked with, to tell when a reload has pointed a prototype at another.
    /// </summary>
    private readonly Dictionary<ProtoId<TtsVoicePrototype>, string> _bakedBackends = new();

    private ISawmill _sawmill = default!;
    private bool _baking;
    private TimeSpan _nextAttempt;

    /// <summary>
    ///     Whether a voice may lack a preview. Cleared once every one has one, so a server that is done doesn't walk the
    ///         voice prototypes every tick; set again by a reload.
    /// </summary>
    private bool _mayBeMissing = true;

    public KsTtsPreviewStatus Status { get; private set; } = KsTtsPreviewStatus.Unavailable;

    public IReadOnlyDictionary<ProtoId<TtsVoicePrototype>, KsTtsPreview> Previews => _previews;

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("ks.tts.preview");
        _netManager.RegisterNetMessage<KsTtsPreviewMessage>();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;
    }

    public void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _prototypeManager.PrototypesReloaded -= OnPrototypesReloaded;
    }

    /// <summary>
    ///     Drops the previews a reload made wrong: voices removed, made unselectable, or pointed at a different back-end
    ///         voice. <see cref="Update"/> then bakes whatever is missing, which covers new and changed voices alike.
    /// </summary>
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<TtsVoicePrototype>())
            return;

        var stale = _previews.Keys
            .Where(id => !_prototypeManager.TryIndex(id, out var voice) ||
                         !voice.Selectable ||
                         _bakedBackends.GetValueOrDefault(id) != voice.Voice)
            .ToList();

        foreach (var id in stale)
        {
            _previews.Remove(id);
            _bakedBackends.Remove(id);
        }

        _mayBeMissing = true;

        if (MissingVoices().Any() && Status == KsTtsPreviewStatus.Ready)
            Status = KsTtsPreviewStatus.Baking;

        // A failed endpoint's back-off shouldn't delay voices that were just added.
        _nextAttempt = TimeSpan.Zero;

        if (stale.Count > 0 || Status == KsTtsPreviewStatus.Baking)
            SendToAll(FullState());
    }

    public void Update()
    {
        if (!_mayBeMissing ||
            _baking ||
            !_entitySystemManager.TryGetEntitySystem<TtsSystem>(out var ttsSystem) ||
            !ttsSystem.Enabled)
            return;

        // Also catches voices added by a prototype reload after everything else was baked.
        if (!MissingVoices().Any())
        {
            _mayBeMissing = false;
            Status = KsTtsPreviewStatus.Ready;
            SendToAll(new KsTtsPreviewMessage { Status = Status });
            return;
        }

        if (_gameTiming.RealTime < _nextAttempt)
            return;

        _ = Bake(ttsSystem);
    }

    private IEnumerable<TtsVoicePrototype> MissingVoices()
    {
        return _prototypeManager.EnumeratePrototypes<TtsVoicePrototype>()
            .Where(voice => voice.Selectable && !_previews.ContainsKey(voice.ID));
    }

    /// <summary>
    ///     Asks for the missing voices one at a time, rather than all at once, so as not to bury the endpoint while
    ///         players are also talking. Continuations resume on the game thread.
    /// </summary>
    private async Task Bake(TtsSystem ttsSystem)
    {
        _baking = true;
        Status = KsTtsPreviewStatus.Baking;
        SendToAll(new KsTtsPreviewMessage { Status = Status });

        var text = _localizationManager.GetString("tts-preview-text");
        var failed = false;

        try
        {
            foreach (var voice in MissingVoices().ToList())
            {
                if (!ttsSystem.Enabled)
                    break;

                if (await ttsSystem.Synthesize(voice, text) is not { } clip)
                {
                    // Stop at the first failure: a down endpoint would otherwise log a warning for every voice.
                    failed = true;
                    break;
                }

                // A reload during the request may have removed or repointed this voice: don't file it under a
                //      prototype it no longer describes.
                if (!_prototypeManager.TryIndex<TtsVoicePrototype>(voice.ID, out var current) ||
                    !current.Selectable ||
                    current.Voice != voice.Voice)
                    continue;

                var preview = new KsTtsPreview(voice.ID, clip.Codec, clip.Data);
                _previews[voice.ID] = preview;
                _bakedBackends[voice.ID] = voice.Voice;
                SendToAll(new KsTtsPreviewMessage { Status = Status, Previews = [preview] });
            }
        }
        catch (Exception exception)
        {
            // Nothing awaits this task, so anything escaping would vanish, and leave the next Update to start again at
            //      once: a bug would become a request every tick. Treat it as a failure, with the back-off.
            _sawmill.Error($"Baking TTS voice previews threw: {exception}");
            failed = true;
        }
        finally
        {
            _baking = false;
        }

        if (failed)
        {
            _nextAttempt = _gameTiming.RealTime + RetryDelay;
            Status = KsTtsPreviewStatus.Failed;
            SendToAll(new KsTtsPreviewMessage { Status = Status });
            _sawmill.Warning($"Couldn't bake every TTS voice preview ({_previews.Count} done); retrying in {RetryDelay.TotalSeconds} s.");
            return;
        }

        // Ready, or TTS was turned off partway: Update works out which on its next run.
        _sawmill.Info($"Baked {_previews.Count} TTS voice previews.");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected)
            return;

        _netManager.ServerSendMessage(FullState(), args.Session.Channel);
    }

    private KsTtsPreviewMessage FullState()
    {
        return new KsTtsPreviewMessage
        {
            Status = Status,
            Replace = true,
            Previews = _previews.Values.ToList(),
        };
    }

    private void SendToAll(KsTtsPreviewMessage message)
    {
        _netManager.ServerSendToAll(message);
    }
}
