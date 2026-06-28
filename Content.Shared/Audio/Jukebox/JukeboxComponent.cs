using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Audio.Jukebox;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedJukeboxSystem))]
public sealed partial class JukeboxComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<JukeboxPrototype>? SelectedSongId;

    [DataField, AutoNetworkedField]
    public EntityUid? AudioStream;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OnState;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OffState;

    /// <summary>
    /// RSI state for the jukebox track being selected.
    /// </summary>
    [DataField]
    public string? SelectState;

    [ViewVariables]
    public bool Selecting;

    [ViewVariables]
    public float SelectAccumulator;

    // _sin start
    [ViewVariables]
    public float ChatAccumulator;

    /// <summary>Был ли трек запущен на прошлом тике — для мгновенного ♫ при старте.</summary>
    [ViewVariables]
    public bool WasPlaying;

    /// <summary>
    /// Флаг ручного переключения трека. Выставляется OnJukeboxSelected при смене трека
    /// и сбрасывается в Update() после первого кадра нового трека.
    /// Предотвращает ложное срабатывание CurrentQueueIndex++ в Update,
    /// когда WasPlaying=false из-за смены трека, а не из-за его естественного завершения.
    /// </summary>
    [ViewVariables]
    public bool TrackSwitchedManually;

    /// <summary>Громкость 0..100. Сохраняется в компоненте для восстановления позиции слайдера.</summary>
    [DataField, AutoNetworkedField]
    public float Volume = 100f;

    /// <summary>Включено ли автопроигрывание.</summary>
    [DataField, AutoNetworkedField]
    public bool AutoplayEnabled = false;

    /// <summary>Индекс текущего играющего трека в очереди прототипа.</summary>
    [DataField, AutoNetworkedField]
    public int CurrentQueueIndex = 0;

    /// <summary>Очередь треков для автопроигрывания (до 200 штук).</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<JukeboxPrototype>> Queue = new();

    /// <summary>
    /// Задержка до анонса "Играет" (секунды). Каждый тик уменьшается на frameTime.
    /// 0 или меньше = не запланирован.
    /// </summary>
    [ViewVariables]
    public float PlayingAnnouncementDelay = 0f;

    /// <summary>
    /// Задержка до анонса "Следующий" (секунды). Каждый тик уменьшается на frameTime.
    /// 0 или меньше = не запланирован. Сбрасывается при остановке.
    /// </summary>
    [ViewVariables]
    public float NextAnnouncementDelay = 0f;

    /// <summary>
    /// Был ли трек на паузе на предыдущем тике. Для определения "разблокировка после паузы".
    /// </summary>
    [ViewVariables]
    public bool WasPaused = false;

    /// <summary>
    /// Имя текущего играющего трека для отложенного анонса "Играет".
    /// </summary>
    [ViewVariables]
    public string? CurrentTrackName;

    /// <summary>
    /// Имя СЛЕДУЮЩЕГО трека для отложенного анонса "Следующий" (только при автоплее).
    /// </summary>
    [ViewVariables]
    public string? PendingNextTrackName;
    // _sin end
}

[Serializable, NetSerializable]
public sealed class JukeboxPlayingMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxPauseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxSelectedMessage(ProtoId<JukeboxPrototype> songId) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> SongId { get; } = songId;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetTimeMessage(float songTime) : BoundUserInterfaceMessage
{
    public float SongTime { get; } = songTime;
}

// _sin start
[Serializable, NetSerializable]
public sealed class JukeboxSetVolumeMessage(float volume) : BoundUserInterfaceMessage
{
    public float Volume { get; } = volume;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetAutoplayMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetQueueMessage(List<ProtoId<JukeboxPrototype>> queue) : BoundUserInterfaceMessage
{
    public List<ProtoId<JukeboxPrototype>> Queue { get; } = queue;
}
// _sin end

[Serializable, NetSerializable]
public enum JukeboxVisuals : byte
{
    VisualState
}

[Serializable, NetSerializable]
public enum JukeboxVisualState : byte
{
    On,
    Off,
    Select,
}

public enum JukeboxVisualLayers : byte
{
    Base
}
