using System.Linq; // _sin
using Content.Server.Chat.Systems; // _sin
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Chat; // _sin
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using JukeboxComponent = Content.Shared.Audio.Jukebox.JukeboxComponent; // _sin

namespace Content.Server.Audio.Jukebox;


public sealed partial class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    // _sin start
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>Maximum volume percent accepted from clients. Matches the UI slider cap.</summary>
    private const float MaxVolumePercent = 200f;

    /// <summary>
    /// Converts volume percent (0–200) to dB for SetVolume.
    /// 100% → 1.0 gain (0 dB), 150% → 2.0 gain (+3 dB), 200% → 4.0 gain (+6 dB).
    /// Formula: gain = 2^((percent - 100) / 50).
    /// </summary>
    private static float VolumePercentToDb(float percent)
    {
        if (percent <= 0f)
            return float.NegativeInfinity;
        // Exponential: 100% → gain=1.0, 150% → gain=2.0, 200% → gain=4.0
        var gain = MathF.Pow(2f, (percent - 100f) / 50f);
        return 10f * MathF.Log10(gain);
    }

    /// <summary>Returns true if the jukebox can accept player input (powered, or has no power component).</summary>
    private bool CanInteract(EntityUid uid)
        => !HasComp<ApcPowerReceiverComponent>(uid) || this.IsPowered(uid, EntityManager);
    // _sin end

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, JukeboxSelectedMessage>(OnJukeboxSelected);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlayingMessage>(OnJukeboxPlay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPauseMessage>(OnJukeboxPause);
        SubscribeLocalEvent<JukeboxComponent, JukeboxStopMessage>(OnJukeboxStop);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetTimeMessage>(OnJukeboxSetTime);
        // _sin start
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetVolumeMessage>(OnJukeboxSetVolume);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetAutoplayMessage>(OnJukeboxSetAutoplay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetQueueMessage>(OnJukeboxSetQueue);
        // _sin end
        SubscribeLocalEvent<JukeboxComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnComponentShutdown);

        SubscribeLocalEvent<JukeboxComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnComponentInit(EntityUid uid, JukeboxComponent component, ComponentInit args)
    {
        if (HasComp<ApcPowerReceiverComponent>(uid))
        {
            TryUpdateVisualState(uid, component);
        }
    }

    private void OnJukeboxPlay(EntityUid uid, JukeboxComponent component, ref JukeboxPlayingMessage args)
    {
        // _sin start: more logic
        if (!CanInteract(uid))
            return;

        if (Exists(component.AudioStream))
        {
            Audio.SetState(component.AudioStream, AudioState.Playing);
        }
        else
        {
            component.AudioStream = Audio.Stop(component.AudioStream);

            if (string.IsNullOrEmpty(component.SelectedSongId) ||
                !_protoManager.TryIndex(component.SelectedSongId, out var jukeboxProto))
            {
                return;
            }

            component.AudioStream = Audio.PlayPvs(jukeboxProto.Path, uid,
                AudioParams.Default.WithMaxDistance(10f).WithVolume(VolumePercentToDb(component.Volume)))?.Entity;
            Dirty(uid, component);
        }
        // _sin end
    }

    private void OnJukeboxPause(Entity<JukeboxComponent> ent, ref JukeboxPauseMessage args)
    {
        // _sin start
        if (!CanInteract(ent.Owner))
            return;

        Audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
        ent.Comp.WasPaused = true;
        // _sin end
    }

    // _sin start: rework logic
    private void OnJukeboxSetTime(EntityUid uid, JukeboxComponent component, JukeboxSetTimeMessage args)
    {
        if (!CanInteract(uid))
            return;
        if (component.AudioStream == null || !Exists(component.AudioStream))
            return;
        if (float.IsNaN(args.SongTime) || float.IsInfinity(args.SongTime) || args.SongTime < 0f)
            return;

        var offset = 0f;
        if (TryComp(args.Actor, out ActorComponent? actorComp))
            offset = Math.Min(actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f, 1f);

        Audio.SetPlaybackPosition(component.AudioStream, args.SongTime + offset);
    }

    private void OnJukeboxSetVolume(EntityUid uid, JukeboxComponent component, JukeboxSetVolumeMessage args)
    {
        if (!CanInteract(uid))
            return;
        if (float.IsNaN(args.Volume) || float.IsInfinity(args.Volume))
            return;

        component.Volume = Math.Clamp(args.Volume, 0f, MaxVolumePercent);
        Dirty(uid, component);
        // Обновляем громкость через SetVolume (dB) чтобы Params.Volume синхронизировался на клиент.
        // 100% → 0 dB, 200% → +3 dB.
        if (component.AudioStream != null)
            Audio.SetVolume(component.AudioStream, VolumePercentToDb(component.Volume));
    }

    private void OnJukeboxSetAutoplay(EntityUid uid, JukeboxComponent component, JukeboxSetAutoplayMessage args)
    {
        if (!CanInteract(uid))
            return;

        component.AutoplayEnabled = args.Enabled;

        if (args.Enabled && component.Queue.Count > 0)
        {
            var nextIdx = component.CurrentQueueIndex + 1;
            if (nextIdx >= component.Queue.Count)
                nextIdx = 0;
            if (_protoManager.TryIndex(component.Queue[nextIdx], out var nextProto))
            {
                _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-autoplay-enabled"), InGameICChatType.Speak, false, ignoreActionBlocker: true);
                _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-nextup-idle", ("name", nextProto.Name)), InGameICChatType.Speak, false, ignoreActionBlocker: true);
            }
        }
        else if (!args.Enabled)
        {
            _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-autoplay-disabled"), InGameICChatType.Speak, false, ignoreActionBlocker: true);
        }

        Dirty(uid, component);
    }

    private void OnJukeboxSetQueue(EntityUid uid, JukeboxComponent component, JukeboxSetQueueMessage args)
    {
        if (!CanInteract(uid))
            return;

        // Only update queue when autoplay is enabled
        if (!component.AutoplayEnabled)
            return;

        // Update the queue in the component (persists with the entity, not in prototype)
        component.Queue = args.Queue.Take(200).ToList();

        // Preserve the index of the currently selected track instead of blindly resetting to 0.
        // OnJukeboxSelected may have already set SelectedSongId before this message arrived,
        // so find its position in the fresh queue and use that; fall back to 0 if not found.
        var idx = component.Queue.FindIndex(id => id == component.SelectedSongId);
        component.CurrentQueueIndex = idx >= 0 ? idx : 0;

        // Recalculate "Следующий" from the new queue — OnJukeboxSelected peeked at the old queue.
        var peekIdx = component.CurrentQueueIndex + 1;
        if (peekIdx >= component.Queue.Count) peekIdx = 0;
        if (component.Queue.Count > 0 && _protoManager.TryIndex(component.Queue[peekIdx], out var peekProto))
            component.PendingNextTrackName = peekProto.Name;
        else
            component.PendingNextTrackName = null;

        Dirty(uid, component);
    }
    // _sin end

    private void OnPowerChanged(Entity<JukeboxComponent> entity, ref PowerChangedEvent args)
    {
        TryUpdateVisualState(entity);

        if (!this.IsPowered(entity.Owner, EntityManager))
        {
            Stop(entity);
        }
    }

    private void OnJukeboxStop(Entity<JukeboxComponent> entity, ref JukeboxStopMessage args)
    {
        Stop(entity);
    }

    private void Stop(Entity<JukeboxComponent> entity)
    {
        // _sin start
        Audio.SetState(entity.Comp.AudioStream, AudioState.Stopped);
        entity.Comp.WasPaused = false;
        entity.Comp.PlayingAnnouncementDelay = 0f;
        entity.Comp.NextAnnouncementDelay = 0f;
        entity.Comp.CurrentTrackName = null;
        entity.Comp.PendingNextTrackName = null;
        Dirty(entity);
        // _sin end
    }

    private void OnJukeboxSelected(EntityUid uid, JukeboxComponent component, JukeboxSelectedMessage args)
    {
        // _sin start: more logic
        if (!CanInteract(uid))
            return;
        // Validate the prototype exists before mutating any state.
        if (!_protoManager.TryIndex(args.SongId, out var jukeboxProto))
            return;

        var wasPlaying = Audio.IsPlaying(component.AudioStream);

        component.SelectedSongId = args.SongId;
        DirectSetVisualState(uid, JukeboxVisualState.Select);
        component.Selecting = true;
        component.AudioStream = Audio.Stop(component.AudioStream);

        // Помечаем ручную смену — Update не должен считать это «трек закончился»
        component.TrackSwitchedManually = true;

        if (wasPlaying)
        {
            component.AudioStream = Audio.PlayPvs(jukeboxProto.Path, uid,
                AudioParams.Default.WithMaxDistance(10f).WithVolume(VolumePercentToDb(component.Volume)))?.Entity;
            component.CurrentTrackName = jukeboxProto.Name;
            component.PlayingAnnouncementDelay = 1f;

            // Обновить CurrentQueueIndex — найти позицию выбранного трека в очереди
            var selectedIdx = component.Queue.FindIndex(id => id == args.SongId);
            if (selectedIdx >= 0)
                component.CurrentQueueIndex = selectedIdx;

            if (component.AutoplayEnabled && component.Queue.Count > 0)
            {
                component.NextAnnouncementDelay = 2f;
                var peekIdx = component.CurrentQueueIndex + 1;
                if (peekIdx >= component.Queue.Count) peekIdx = 0;
                if (_protoManager.TryIndex(component.Queue[peekIdx], out var peekProto))
                    component.PendingNextTrackName = peekProto.Name;
            }
            else
            {
                component.NextAnnouncementDelay = 0f;
                component.PendingNextTrackName = null;
            }
        }

        Dirty(uid, component);
        // _sin end
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Selecting)
            {
                comp.SelectAccumulator += frameTime;
                if (comp.SelectAccumulator >= 0.5f)
                {
                    comp.SelectAccumulator = 0f;
                    comp.Selecting = false;
                    TryUpdateVisualState(uid, comp);
                }
            }

            // _sin start
            // Ранний пропуск: нет аудиострима — нечего отслеживать
            if (comp.AudioStream == null)
            {
                // Автопроигрывание: если трек закончился сам (не вручную) и автоплей включён
                if (!comp.WasPlaying && !comp.TrackSwitchedManually && comp.AutoplayEnabled && comp.Queue.Count > 0)
                {
                    comp.CurrentQueueIndex++;
                    if (comp.CurrentQueueIndex >= comp.Queue.Count)
                        comp.CurrentQueueIndex = 0;

                    var nextId = comp.Queue[comp.CurrentQueueIndex];
                    if (_protoManager.TryIndex(nextId, out var nextProto))
                    {
                        comp.SelectedSongId = nextId;
                        comp.AudioStream = Audio.PlayPvs(nextProto.Path, uid,
                            AudioParams.Default.WithMaxDistance(10f)
                                .WithVolume(VolumePercentToDb(comp.Volume)))?.Entity;

                        // Имя текущего и следующего для параллельных анонсов
                        comp.CurrentTrackName = nextProto.Name;

                        var peekIdx = comp.CurrentQueueIndex + 1;
                        if (peekIdx >= comp.Queue.Count) peekIdx = 0;
                        if (_protoManager.TryIndex(comp.Queue[peekIdx], out var peekProto))
                            comp.PendingNextTrackName = peekProto.Name;
                        else
                            comp.PendingNextTrackName = null;

                        // Параллельные таймеры запускаются в блоке isPlaying
                        comp.WasPaused = false;
                        Dirty(uid, comp);
                    }
                }

                comp.WasPlaying = false;
                comp.TrackSwitchedManually = false;
                comp.ChatAccumulator = 0f;
                // Сбрасываем таймеры и имена только если нет активного таймера
                if (comp.PlayingAnnouncementDelay <= 0f)
                {
                    comp.NextAnnouncementDelay = 0f;
                    comp.CurrentTrackName = null;
                    comp.PendingNextTrackName = null;
                }
                continue;
            }

            var isPlaying = Audio.IsPlaying(comp.AudioStream);

            if (isPlaying)
            {
                // --- Переход: трек только начал играть ---
                if (!comp.WasPlaying)
                {
                    // Имя текущего трека (если ещё не задано блоком AutoPlay)
                    if (string.IsNullOrEmpty(comp.CurrentTrackName)
                        && _protoManager.TryIndex(comp.SelectedSongId, out var startedProto))
                    {
                        comp.CurrentTrackName = startedProto.Name;
                    }

                    // Параллельные таймеры — оба запускаются с одной точки отсчёта
                    comp.PlayingAnnouncementDelay = 1f;
                    if (comp.AutoplayEnabled && comp.Queue.Count > 0)
                    {
                        comp.NextAnnouncementDelay = 2f;

                        // Peek следующего трека для "Следующий"
                        var peekIdx = comp.CurrentQueueIndex + 1;
                        if (peekIdx >= comp.Queue.Count) peekIdx = 0;
                        if (_protoManager.TryIndex(comp.Queue[peekIdx], out var peekProto))
                            comp.PendingNextTrackName = peekProto.Name;
                    }
                    else
                    {
                        comp.NextAnnouncementDelay = 0f;
                    }

                    comp.WasPaused = false;
                    // Трек реально начал играть — флаг ручной смены можно снять
                    comp.TrackSwitchedManually = false;
                }

                // --- Декремент таймеров ---
                if (comp.PlayingAnnouncementDelay > 0f)
                {
                    comp.PlayingAnnouncementDelay -= frameTime;
                    if (comp.PlayingAnnouncementDelay <= 0f)
                    {
                        comp.PlayingAnnouncementDelay = 0f;
                        var name = comp.CurrentTrackName
                            ?? (_protoManager.TryIndex(comp.SelectedSongId, out var p) ? p.Name : "?");
                        _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-playing", ("name", name)),
                            InGameICChatType.Speak, true, ignoreActionBlocker: true);
                    }
                }

                if (comp.NextAnnouncementDelay > 0f)
                {
                    comp.NextAnnouncementDelay -= frameTime;
                    if (comp.NextAnnouncementDelay <= 0f)
                    {
                        comp.NextAnnouncementDelay = 0f;
                        _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-nextup-playing", ("name", comp.PendingNextTrackName ?? "??? THIS IS A BUG PLEASE REPORT ME !!!")),
                            InGameICChatType.Speak, true, ignoreActionBlocker: true);
                    }
                }

                // Фоновые ♫♫♫♫ каждые 5 сек (только когда нет активных таймеров)
                if (comp.PlayingAnnouncementDelay <= 0f && comp.NextAnnouncementDelay <= 0f)
                {
                    comp.ChatAccumulator += frameTime;
                    if (comp.ChatAccumulator >= 5f)
                    {
                        comp.ChatAccumulator = 0f;
                        _chat.TrySendInGameICMessage(uid, Loc.GetString("sin-jukebox-chat-music"),
                            InGameICChatType.Speak, true, ignoreActionBlocker: true);
                    }
                }
            }
            else
            {
                comp.PlayingAnnouncementDelay = 0f;
                comp.NextAnnouncementDelay = 0f;
                comp.ChatAccumulator = 0f;
                comp.WasPaused = true;

                if (comp.AudioStream != null && !Exists(comp.AudioStream))
                {
                    comp.AudioStream = null;
                    Dirty(uid, comp);
                }
            }

            comp.WasPlaying = isPlaying;
            // _sin end
        }
    }

    private void OnComponentShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        component.AudioStream = Audio.Stop(component.AudioStream);
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, state);
    }


    private void TryUpdateVisualState(EntityUid uid, JukeboxComponent? jukeboxComponent = null)
    {
        if (!Resolve(uid, ref jukeboxComponent))
            return;

        var finalState = JukeboxVisualState.On;

        if (!this.IsPowered(uid, EntityManager))
        {
            finalState = JukeboxVisualState.Off;
        }

        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, finalState); // _sin
    }
}
